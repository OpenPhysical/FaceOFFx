#!/usr/bin/env python3
"""Propose frozen-pass exchanges from measured decoded RGB errors, then verify full maps."""
import argparse
import base64
import io
import json
import math
import re
import subprocess
from dataclasses import dataclass
from pathlib import Path

import numpy as np
from PIL import Image, features

from analyze_passes import EDGE_KEYS, METHOD, check_ledger, checked_bytes, digest, finite_metrics, gate_candidate, inspect_jp2, quality, relative, require
from edge_quality import make_reference, measure
from decoded_response import measure_response
from score_allocator import endpoint_map, load_masks


DIAGNOSTIC_BYTE_BOUND = 32768


def baseline_guards(data, attribution, maximum_bytes, minimum_face_bytes):
    require(len(data) <= maximum_bytes and attribution["attributedFacePayloadBytes"] >= minimum_face_bytes,
            "Frozen baseline must meet its actual JP2 cap and V3 face floor")


def ready_guards(ready):
    for key in ("originalFloorReplayByteExact", "originalBalancedReplayByteExact", "tier1EncodedOnce"):
        require(ready.get(key) is True, "Scratch emitter must verify frozen replay evidence: "+key)


def candidate_rankings(candidates, baseline):
    ranking = lambda rows: sorted(rows, key=lambda row: (row["gate"]["contourErrorGeometricMeanRatio"],
        row["sourceErrors"]["face"]["sseRgb"], row["bytes"], row["id"]))
    strict, conservative = [], []
    for row in candidates:
        row["strictZeroFaceLossGate"] = gate_candidate(row, baseline, face_tolerance=0., regression_limit=.10)
        row["conservativeFaceToleranceGate"] = gate_candidate(row, baseline, face_tolerance=.10, regression_limit=.10)
        if row["hairSourceErrorNonloss"]:
            if row["strictZeroFaceLossGate"]["conservativeRepairEligible"]:
                strict.append(row)
            if row["conservativeFaceToleranceGate"]["conservativeRepairEligible"]:
                conservative.append(row)
    return dict(strictFaceNonlossRanking=[row["id"] for row in ranking(strict)],
                conservativePoint10DbRanking=[row["id"] for row in ranking(conservative)],
                acceptanceScope="Separate research goals: strict face nonloss or 0.10 dB face tolerance, hair nonloss, complete edge metrics and separate 10% other-error bounds")


def prefix_value(block, endpoint, field):
    require(type(endpoint) is int and (endpoint == -1 or endpoint in block["legalPasses"]), "Select a frozen legal native pass")
    require(-1 <= endpoint < len(block[field]), "Native pass exceeds its frozen table")
    return block[field][endpoint] if endpoint >= 0 else 0


def normalize_changes(blocks, changes):
    require(set(changes).issubset(blocks), "Unknown frozen coding block")
    for identifier, endpoint in changes.items():
        prefix_value(blocks[identifier], endpoint, "bytesByPass")
    return {identifier: endpoint for identifier, endpoint in sorted(changes.items())
            if endpoint != blocks[identifier]["terminalPass"]}


def endpoint_totals(blocks, changes):
    changes = normalize_changes(blocks, changes)
    body, face = 0, 0.
    for identifier, block in blocks.items():
        endpoint = changes.get(identifier, block["terminalPass"])
        body += prefix_value(block, endpoint, "bytesByPass")
        face += prefix_value(block, endpoint, "faceCreditByPass")
    require(math.isfinite(face), "Finite frozen face-credit tables are required")
    return body, face


def source_errors(source, candidate, masks):
    result = quality(source, candidate, masks)
    squared = (candidate.astype(np.float64)-source.astype(np.float64))**2
    for name, values in result.items():
        values["sseRgb"] = float(squared[masks[name]].sum())
    return result


def error_deltas(actual, baseline):
    return {name: actual[name]["sseRgb"]-baseline[name]["sseRgb"] for name in actual}


@dataclass(frozen=True)
class DonorState:
    freed: int
    credit_loss: float
    face_delta: float
    hair_delta: float
    changes: tuple


def donor_frontier(groups, maximum_freed, maximum_credit_loss, states_per_body=16):
    """Bounded proposals use independently measured marginal face SSE, with separate hair SSE."""
    require(type(maximum_freed) is int and maximum_freed >= 0 and math.isfinite(maximum_credit_loss)
            and maximum_credit_loss >= 0 and type(states_per_body) is int and states_per_body > 0, "Supply finite donor resource bounds")
    for options in groups.values():
        for option in options:
            require(type(option["freedBodyBytes"]) is int and option["freedBodyBytes"] > 0
                    and math.isfinite(option["faceCreditLost"]) and option["faceCreditLost"] >= 0
                    and all(math.isfinite(option["sourceErrorSseDeltas"][name]) for name in ("face", "hair")),
                    "Use finite independently measured donor errors and positive body savings")
    states = {0: [DonorState(0, 0., 0., 0., ())]}
    for identifier, options in sorted(groups.items()):
        next_states = {}
        for current in states.values():
            for previous in current:
                for option in [None]+options:
                    freed = previous.freed+(option["freedBodyBytes"] if option else 0)
                    lost = previous.credit_loss+(option["faceCreditLost"] if option else 0.)
                    if freed > maximum_freed or lost > maximum_credit_loss+1e-7:
                        continue
                    state = DonorState(freed, lost,
                        previous.face_delta+(option["sourceErrorSseDeltas"]["face"] if option else 0.),
                        previous.hair_delta+(option["sourceErrorSseDeltas"]["hair"] if option else 0.),
                        previous.changes+((identifier, option["endpoint"]),) if option else previous.changes)
                    next_states.setdefault(freed, []).append(state)
        states = {}
        for freed, possibilities in next_states.items():
            frontier = []
            for candidate in sorted(possibilities, key=lambda state: (state.face_delta, state.hair_delta, state.credit_loss, state.changes)):
                if any(other.credit_loss <= candidate.credit_loss+1e-7 and other.face_delta <= candidate.face_delta
                       and other.hair_delta <= candidate.hair_delta for other in frontier):
                    continue
                frontier.append(candidate)
            # Keep low-credit proposals as well as low-face-error proposals within this explicit beam bound.
            by_face = sorted(frontier, key=lambda state: (state.face_delta, state.hair_delta, state.credit_loss, state.changes))
            by_credit = sorted(frontier, key=lambda state: (state.credit_loss, state.face_delta, state.hair_delta, state.changes))
            retained = []
            for pair in zip(by_face, by_credit):
                for candidate in pair:
                    if candidate not in retained:
                        retained.append(candidate)
                    if len(retained) == states_per_body:
                        break
                if len(retained) == states_per_body:
                    break
            states[freed] = retained
    return sorted((state for group in states.values() for state in group),
                  key=lambda state: (state.face_delta, state.hair_delta, state.credit_loss, state.freed, state.changes))


class JsonlEmitter:
    def __init__(self, command, log_path):
        require(isinstance(command, list) and command and all(isinstance(item, str) and item for item in command), "Supply an argv JSON array for the scratch emitter")
        self.log = log_path.open("ab")
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=self.log,
                                        text=True, bufsize=1)
        self.ready = self.read()
        require(self.ready.get("ready") is True or self.ready.get("event") == "ready" or self.ready.get("status") == "ready",
                "Expected the frozen-map emitter ready message")

    def read(self):
        line = self.process.stdout.readline()
        require(bool(line), "Scratch emitter ended; inspect its stderr log")
        return json.loads(line)

    def emit(self, identifier, changes, diagnostic):
        request = dict(id=identifier, changes=changes, allowDiagnosticInfeasible=diagnostic)
        self.process.stdin.write(json.dumps(request, separators=(",", ":"))+"\n")
        self.process.stdin.flush()
        response = self.read()
        require(response.get("id") == identifier, "Scratch emitter response ID mismatch")
        return response

    def close(self):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.process.terminate()
            self.process.wait(timeout=5)
        self.log.close()


class DecodedSearch:
    def __init__(self, manifest_path, output, root):
        self.root, self.output = root, output
        self.helper_hashes = {name: digest((Path(__file__).parent/name).read_bytes()) for name in
            ("optimize_decoded.py", "analyze_passes.py", "score_allocator.py", "edge_quality.py", "decoded_response.py")}
        self.input_bytes = manifest_path.read_bytes()
        self.manifest = json.loads(self.input_bytes)
        require(self.manifest["completed"] is True and self.manifest["methodIdentifier"] == METHOD
                and self.manifest["productionForkUnchanged"] is True, "Use a completed frozen V3 research manifest")
        self.source_path = Path(self.manifest["sourceCropPath"])
        self.source_data = checked_bytes(self.source_path, self.manifest["sourceCropSha256"])
        with Image.open(io.BytesIO(self.source_data)) as image:
            require(image.mode == "RGB" and image.size == (480, 640), "Use the frozen RGB medical source")
            self.source = np.array(image)
        self.masks, self.mask_evidence = load_masks(self.source_path, self.source, self.manifest)
        self.reference = make_reference(self.source)
        self.masks["clothingInterior"] = self.reference["interior"]
        self.masks["clothingSeams"] = self.reference["seams"]
        self.cap, self.floor = self.manifest["maximumJp2Bytes"], self.manifest["minimumAttributedFacePayloadBytes"]
        require(self.cap == 11820 and self.floor == (self.manifest["roiPixelCount"]+7)//8, "Preserve the fixed portrait cap and coding-mask floor")
        replay = next(row for row in self.manifest["trials"] if row["id"] == "exact-replay-control")
        self.blocks, self.table_bytes = endpoint_map(replay["blockMapPath"], replay["blockMapSha256"])
        self.baseline_data = checked_bytes(replay["jp2Path"], replay["sha256"])
        self.structure = inspect_jp2(self.baseline_data, (480, 640))
        with Image.open(io.BytesIO(self.baseline_data)) as image:
            self.baseline_pixels = np.array(image)
        self.baseline_quality = source_errors(self.source, self.baseline_pixels, self.masks)
        self.baseline_metrics = measure(self.reference, self.baseline_pixels)
        self.baseline = dict(metrics=self.baseline_metrics, quality=self.baseline_quality, meetsByteAndRegionalGates=True)
        self.body, self.credit = endpoint_totals(self.blocks, {})
        check_ledger(replay["payloadTelemetry"], replay["sharedPayloadAttribution"], self.structure, self.manifest["roiPixelCount"])
        baseline_guards(self.baseline_data, replay["sharedPayloadAttribution"], self.cap, self.floor)
        require(self.body == replay["payloadTelemetry"]["packetBodyBytes"] and abs(self.credit-replay["sharedPayloadAttribution"]["facePayloadByteEstimate"]) < 1e-7,
                "Frozen prefix tables fail baseline body/face reconciliation")
        controls = json.loads((root/"artifacts/piv-encoding-current/landmark-v3/medical.json").read_bytes())["results"]
        balanced = next(row for row in controls if row["name"] == "balanced-start4-b64-noquota")
        self.balanced_data = checked_bytes(balanced["jp2Path"], balanced["jp2Sha256"])
        with Image.open(io.BytesIO(self.balanced_data)) as image:
            self.balanced_pixels = np.array(image)
        self.balanced_blocks, self.balanced_table_bytes = endpoint_map(self.manifest["balancedEndpointMapPath"], self.manifest["balancedEndpointMapSha256"])
        require(set(self.blocks) == set(self.balanced_blocks), "Balanced and floor coding block identities differ")
        self.cache, self.trials, self.candidates = {}, [], []
        self.output.mkdir(parents=True, exist_ok=True)

    def bind_emitter(self, ready):
        ready_guards(ready)
        for key in ("sourceCropSha256", "maskSha256", "maximumJp2Bytes", "minimumAttributedFacePayloadBytes", "methodIdentifier"):
            require(ready[key] == self.manifest[key], "Scratch emitter frozen input mismatch: "+key)
        require(ready["baselineSha256"] == digest(self.baseline_data), "Scratch emitter baseline differs from the frozen floor control")
        tables, table_data = endpoint_map(ready["nativePrefixTablePath"], ready["nativePrefixTableSha256"])
        require(set(tables) == set(self.blocks), "Scratch emitter prefix block identities changed")
        for identifier, block in self.blocks.items():
            for name in ("terminalPass", "legalPasses", "bytesByPass", "faceCreditByPass"):
                require(tables[identifier][name] == block[name], "Scratch emitter frozen prefix table mismatch: "+name)
        self.emitter_evidence = dict(codecAssemblySha256=ready["codecAssemblySha256"],
            nativePrefixTableSha256=digest(table_data), originalFloorReplayByteExact=ready["originalFloorReplayByteExact"],
            originalBalancedReplayByteExact=ready["originalBalancedReplayByteExact"], tier1EncodedOnce=ready["tier1EncodedOnce"])

    def evaluate(self, emitter, changes, kind, diagnostic):
        changes = normalize_changes(self.blocks, changes)
        key = digest(json.dumps(changes, sort_keys=True, separators=(",", ":")).encode())
        cache_key = (key, diagnostic)
        if cache_key in self.cache:
            return self.cache[cache_key]
        identifier = kind+"-"+key[:16]
        response = emitter.emit(identifier, changes, diagnostic)
        if response.get("dataBase64"):
            data = base64.b64decode(response["dataBase64"], validate=True)
        elif response.get("jp2Path"):
            data = checked_bytes(response["jp2Path"], response["sha256"])
        else:
            self.trials.append(dict(id=identifier, changes=changes, status="rejected-diagnostic", error=response.get("error"), jp2Path=None, sha256=None))
            self.cache[cache_key] = None
            return None
        require(len(data) <= DIAGNOSTIC_BYTE_BOUND and digest(data) == response["sha256"]
                and response.get("bytes", response.get("completeJp2Bytes")) == len(data), "Scratch output safety/hash/length mismatch")
        structure = inspect_jp2(data, (480, 640))
        for name in ("codingSegments", "quantizationSegments", "rgn"):
            require(structure[name] == self.structure[name], "Frozen coding/quantizer markers changed")
        telemetry, attribution = response["payloadTelemetry"], response["sharedPayloadAttribution"]
        check_ledger(telemetry, attribution, structure, self.manifest["roiPixelCount"])
        native = response["nativeEndpoints"]
        require(set(native) == set(self.blocks) and all(native[identifier] == changes.get(identifier, block["terminalPass"])
                for identifier, block in self.blocks.items()), "Scratch endpoint map changed an untargeted block")
        body, credit = endpoint_totals(self.blocks, changes)
        require(body == telemetry["packetBodyBytes"] and abs(credit-attribution["facePayloadByteEstimate"]) <= 1e-7
                and math.floor(credit) == attribution["attributedFacePayloadBytes"], "Frozen selected prefixes fail actual body/face closure")
        cap_met, floor_met = len(data) <= self.cap, attribution["attributedFacePayloadBytes"] >= self.floor
        require(response["originalCapSatisfied"] == cap_met and response["originalFaceFloorSatisfied"] == floor_met,
                "Original budget/floor response flags mismatch")
        if not diagnostic:
            require(cap_met and floor_met, "Final combined map must meet the original cap and V3 floor")
        with Image.open(io.BytesIO(data)) as image:
            image.load()
            require(image.mode == "RGB" and image.size == (480, 640), "Independent decoded RGB geometry changed")
            pixels = np.array(image)
        measured = source_errors(self.source, pixels, self.masks)
        decoded_response = measure_response(self.source, self.baseline_pixels, pixels, self.masks)
        response.pop("dataBase64", None)
        record = dict(id=identifier, kind=kind, changes=changes, nativeEndpointMapSha256=digest(json.dumps(native, sort_keys=True).encode()),
                      jp2Sha256=digest(data), bytes=len(data), originalCapSatisfied=cap_met, originalFaceFloorSatisfied=floor_met,
                      faceBytes=attribution["attributedFacePayloadBytes"], faceByteEstimate=credit,
                      bodyBytes=body, packetHeaderBytes=telemetry["packetHeaderBytes"], measurementOnly=diagnostic,
                      sourceErrors=measured, sourceErrorSseDeltas=error_deltas(measured, self.baseline_quality),
                      actualDecodedResponse=decoded_response["regions"],
                      balancedDistance=source_errors(self.balanced_pixels, pixels, self.masks),
                      response=response)
        if kind == "candidate":
            record["metrics"] = measure(self.reference, pixels)
            record["quality"] = measured
            record["meetsByteAndRegionalGates"] = cap_met and floor_met
            record["gate"] = gate_candidate(record, self.baseline, face_tolerance=0., regression_limit=.10)
            record["hairSourceErrorNonloss"] = measured["hair"]["sseRgb"] <= self.baseline_quality["hair"]["sseRgb"]+1e-7
            path = self.output/(identifier+".jp2")
            path.write_bytes(data)
            record["jp2Path"] = relative(path, self.root)
            Image.fromarray(pixels).save(path.with_suffix(".png"))
            record["decodedPngPath"] = relative(path.with_suffix(".png"), self.root)
            self.candidates.append(record)
        self.cache[cache_key] = record
        with (self.output/"measured-responses.jsonl").open("a") as stream:
            stream.write(json.dumps(record, allow_nan=False)+"\n")
        return record

    def measure_donors(self, emitter):
        groups = {}
        for identifier, block in sorted(self.blocks.items()):
            old = block["terminalPass"]
            if block["resolution"] != 5 or old < 0 or prefix_value(block, old, "faceCreditByPass") <= 0:
                continue
            for endpoint in [-1]+block["legalPasses"]:
                if endpoint >= old or prefix_value(block, endpoint, "bytesByPass") >= prefix_value(block, old, "bytesByPass"):
                    continue
                measured = self.evaluate(emitter, {identifier: endpoint}, "donor", True)
                if measured:
                    groups.setdefault(identifier, []).append(dict(endpoint=endpoint,
                        freedBodyBytes=prefix_value(block, old, "bytesByPass")-prefix_value(block, endpoint, "bytesByPass"),
                        faceCreditLost=prefix_value(block, old, "faceCreditByPass")-prefix_value(block, endpoint, "faceCreditByPass"),
                        sourceErrorSseDeltas=measured["sourceErrorSseDeltas"], measuredId=measured["id"]))
        return groups

    def receiver_bundles(self, emitter):
        bundles = []
        for identifier in ("bottom-c2-refined-donors0", "c2-bottom-plus-c1-r4-HL-donors0", "c2-bottom-plus-c1-r3r4-HL-donors0"):
            trial = next((row for row in self.manifest["trials"] if row["id"] == identifier), None)
            if trial:
                bundles.append(dict(id=identifier.replace("-donors0", ""), changes=trial["receiverChanges"]))
        useful = []
        for identifier, block in sorted(self.blocks.items()):
            goal = self.balanced_blocks[identifier]["terminalPass"]
            if block["resolution"] > 4 or goal <= block["terminalPass"]:
                continue
            record = self.evaluate(emitter, {identifier: goal}, "receiver", True)
            if record and (record["sourceErrorSseDeltas"]["face"] < 0 or record["sourceErrorSseDeltas"]["hair"] < 0):
                useful.append((identifier, goal, record))
        face = sorted(useful, key=lambda item: item[2]["sourceErrorSseDeltas"]["face"])[:4]
        hair = sorted(useful, key=lambda item: item[2]["sourceErrorSseDeltas"]["hair"])[:4]
        chosen = {identifier: (goal, record) for identifier, goal, record in face+hair}
        bases = list(bundles)
        for identifier, (goal, _) in chosen.items():
            bundles.append(dict(id="restore-"+identifier, changes={identifier: goal}))
            for base in bases:
                if identifier not in base["changes"]:
                    bundles.append(dict(id=base["id"]+"-plus-"+identifier, changes=dict(base["changes"], **{identifier: goal})))
        return bundles

    def search(self, emitter, groups, bundles, plans_per_bundle=12, states_per_body=16):
        proposals = []
        for bundle in bundles:
            receivers = normalize_changes(self.blocks, bundle["changes"])
            body, credit = endpoint_totals(self.blocks, receivers)
            added = body-self.body
            allowed_loss = credit-self.floor
            if allowed_loss < 0:
                continue
            required = max(0, added-(self.cap-len(self.baseline_data)))
            # Header effects are verified by real packet emission; this body window supplies bounded proposals.
            eligible = {identifier: choices for identifier, choices in groups.items() if identifier not in receivers}
            frontier = donor_frontier(eligible, max(required+96, 96), allowed_loss, states_per_body)
            feasible = [state for state in frontier if state.freed >= required]
            for state in feasible[:plans_per_bundle]:
                changes = dict(receivers, **dict(state.changes))
                proposal = dict(bundle=bundle["id"], changes=changes, freedBodyBytes=state.freed,
                    marginalFaceSseDelta=state.face_delta, marginalHairSseDelta=state.hair_delta,
                    marginalScope="Individual decoded responses propose maps; combined independent decode supplies actual quality")
                record = self.evaluate(emitter, changes, "candidate", False)
                proposal["actualCandidateId"] = record["id"] if record else None
                proposals.append(proposal)
            print(bundle["id"], len(feasible), "bounded donor proposals;", len(self.candidates), "full-map decodes", flush=True)
        return proposals

    def proposal_interactions(self, proposals, bundles):
        by_map = {tuple(sorted(record["changes"].items())): record for record in self.cache.values() if record and record["measurementOnly"]}
        candidate_by_id = {record["id"]: record for record in self.candidates}
        bundle_by_id = {bundle["id"]: normalize_changes(self.blocks, bundle["changes"]) for bundle in bundles}
        for proposal in proposals:
            if not proposal.get("actualCandidateId"):
                continue
            candidate = candidate_by_id[proposal["actualCandidateId"]]
            receiver_changes = bundle_by_id[proposal["bundle"]]
            donor_changes = {identifier: endpoint for identifier, endpoint in proposal["changes"].items() if identifier not in receiver_changes}
            def isolated(changes):
                found, missing = [], []
                for identifier, endpoint in changes.items():
                    record = by_map.get(((identifier, endpoint),))
                    (found if record else missing).append(record if record else identifier)
                deltas = {region: sum(record["sourceErrorSseDeltas"][region] for record in found) for region in self.masks} if not missing else None
                return deltas, missing
            receiver, receiver_missing = isolated(receiver_changes)
            donor, donor_missing = isolated(donor_changes)
            proposal["decodedInteractionEvidence"] = dict(receiverIsolatedDeltaSse=receiver, donorIsolatedDeltaSse=donor,
                missingReceiverMeasurements=receiver_missing, missingDonorMeasurements=donor_missing,
                actualJointDeltaSse=candidate["sourceErrorSseDeltas"],
                jointMinusIsolatedDeltaSse={region: candidate["sourceErrorSseDeltas"][region]-receiver[region]-donor[region] for region in self.masks}
                    if receiver is not None and donor is not None else None,
                evidenceScope="Individual measured responses supply an additive proposal; actual complete-map decode includes interacting errors, rounding and clipping")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--emitter-command-json", help="JSON argv for the persistent scratch emitter")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--finalize-existing", action="store_true", help="Reconcile an existing completed run using captured measurements; emission stays idle")
    parser.add_argument("--plans-per-bundle", type=int, default=12)
    parser.add_argument("--states-per-body", type=int, default=16)
    args = parser.parse_args()
    require(features.check("jpg_2000") and args.plans_per_bundle > 0 and args.states_per_body > 0, "Independent OpenJPEG and positive bounded search counts are required")
    root = Path(__file__).resolve().parents[2]
    if args.finalize_existing:
        search = DecodedSearch(args.manifest, args.output, root)
        original_path = args.output/"search-results-original.json"
        original_data = original_path.read_bytes()
        report = json.loads(original_data)
        require(report["completed"] is True and report["sourceManifestSha256"] == digest(search.input_bytes)
                and report["nativePrefixTableSha256"] == digest(search.table_bytes), "Captured run differs from its frozen inputs")
        ready_guards(report["emitterEvidence"])
        records_data = (args.output/"measured-responses.jsonl").read_bytes()
        records = [json.loads(line) for line in records_data.splitlines()]
        for record in records:
            key = digest(json.dumps(record["changes"], sort_keys=True, separators=(",", ":")).encode())
            search.cache[(key, record["measurementOnly"])] = record
        search.candidates = report["candidates"]
        for record in search.candidates:
            data = checked_bytes(root/record["jp2Path"], record["jp2Sha256"])
            baseline_guards(data, record["response"]["sharedPayloadAttribution"], search.cap, search.floor)
            body, credit = endpoint_totals(search.blocks, record["changes"])
            require(body == record["bodyBytes"] and abs(credit-record["faceByteEstimate"]) <= 1e-7, "Captured full-map prefix ledger changed")
        report.update(candidate_rankings(search.candidates, search.baseline))
        search.proposal_interactions(report["proposals"], report["receiverBundles"])
        report.update(baselineSourceErrors=search.baseline_quality, baselineEdgeMetrics=search.baseline_metrics,
            visualSelection="User preferred the balanced visual control; floor candidates remain research evidence",
            finalizationEvidence=dict(originalReportSha256=digest(original_data), measuredResponsesSha256=digest(records_data),
                executedScriptSnapshotPath=relative(args.output/"executed-optimize_decoded.py", root),
                executedScriptSnapshotSha256=digest(checked_bytes(args.output/"executed-optimize_decoded.py", report["analysisScriptSha256"])),
                finalizationHelperSha256=search.helper_hashes, newEmissionRequests=0))
        (args.output/"search-results.json").write_text(json.dumps(report, indent=2, allow_nan=False)+"\n")
        print(json.dumps(dict(candidates=len(search.candidates), strictFaceNonlossCandidates=len(report["strictFaceNonlossRanking"]),
            conservativeCandidates=len(report["conservativePoint10DbRanking"]), newEmissionRequests=0,
            report=relative(args.output/"search-results.json", root)), indent=2), flush=True)
        return
    require(args.emitter_command_json is not None, "Supply the scratch emitter argv for a new search")
    require(not (args.output/"measured-responses.jsonl").exists(), "Use a fresh decoded-search directory to preserve earlier evidence")
    search = DecodedSearch(args.manifest, args.output, root)
    emitter = JsonlEmitter(json.loads(args.emitter_command_json), args.output/"emitter.stderr.log")
    try:
        search.bind_emitter(emitter.ready)
        replay = search.evaluate(emitter, {}, "replay", True)
        require(replay and replay["jp2Sha256"] == digest(search.baseline_data), "Scratch full-map replay changed the baseline bytes")
        donors = search.measure_donors(emitter)
        print(sum(len(values) for values in donors.values()), "independently decoded donor-prefix measurements", flush=True)
        bundles = search.receiver_bundles(emitter)
        proposals = search.search(emitter, donors, bundles, args.plans_per_bundle, args.states_per_body)
        search.proposal_interactions(proposals, bundles)
    finally:
        emitter.close()
    rankings = candidate_rankings(search.candidates, search.baseline)
    report = dict(completed=True, methodId=METHOD, sourceCropSha256=search.manifest["sourceCropSha256"],
        codingMaskSha256=search.manifest["maskSha256"], nativePrefixTableSha256=digest(search.table_bytes),
        sourceManifestSha256=digest(search.input_bytes), analysisScriptSha256=digest(Path(__file__).read_bytes()),
        decoder="Pillow/OpenJPEG "+str(features.version("jpg_2000")), imageByteCap=search.cap, minimumAttributedFacePayloadBytes=search.floor,
        donorMeasurements=donors, receiverBundles=bundles, proposals=proposals, candidates=search.candidates, rejected=search.trials,
        emitterEvidence=search.emitter_evidence,
        fixedMaskEvidence={key: dict(value, path=relative(value["path"], root)) for key, value in search.mask_evidence.items()},
        **rankings,
        baselineSourceErrors=search.baseline_quality, baselineEdgeMetrics=search.baseline_metrics,
        importedHelperSha256=search.helper_hashes,
        searchScope="Bounded measured-marginal donor frontier; full candidate decodes evaluate rounding, clipping and interacting responses",
        measurementScope="Isolated in-memory responses use a 32768-byte safety bound with original cap/floor flags; final candidates retain original bounds",
        productionDefaults="Preserved", visualReview="Full-map candidates require native and intended-size review")
    (args.output/"search-results.json").write_text(json.dumps(report, indent=2, allow_nan=False)+"\n")
    print(json.dumps(dict(measuredMaps=len(search.cache), candidates=len(search.candidates), strictFaceNonlossCandidates=len(rankings["strictFaceNonlossRanking"]),
                         report=relative(args.output/"search-results.json", root)), indent=2), flush=True)


if __name__ == "__main__":
    main()

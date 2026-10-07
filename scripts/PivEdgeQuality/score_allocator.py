#!/usr/bin/env python3
"""Score isolated allocator variants against frozen balanced and floor controls."""
import argparse
import io
import json
import math
import re
from pathlib import Path

import numpy as np
from PIL import Image, features

from analyze_passes import (
    EDGE_KEYS, METHOD, change_costs, check_ledger, checked_bytes, digest, finite_metrics,
    face_sort_value, gate_candidate, inspect_jp2, panel, print_geometry, quality, relative, require,
)
from edge_quality import make_reference, measure


ACCEPTED_STATUSES = {"encoded", "accepted-cap-and-face-floor"}
FAILED_STATUSES = {"rejected-diagnostic", "rejected", "failed"}


def gap_closure(candidate, baseline, target):
    """Keep each error's visual-target gap separate."""
    result = {}
    for key in EDGE_KEYS:
        before, after, goal = baseline[key], candidate[key], target[key]
        if all(value is not None and math.isfinite(value) for value in (before, after, goal)) and before > goal:
            result[key] = (before-after)/(before-goal)
        else:
            result[key] = None
    return result


def psnr_deltas(actual, reference):
    return {key: actual[key]["psnrRgbDb"]-reference[key]["psnrRgbDb"]
            if actual[key]["psnrRgbDb"] is not None and reference[key]["psnrRgbDb"] is not None
            else 0. if actual[key]["exactPixelEquality"] and reference[key]["exactPixelEquality"] else None
            for key in actual}


def checked_manifest(path):
    data = Path(path).read_bytes()
    manifest = json.loads(data)
    require(manifest.get("completed") is True, "Wait for the completed variant manifest")
    require(manifest["methodIdentifier"] == METHOD, "Use the canonical V3 face-credit ledger")
    trials = manifest["trials"]
    require(manifest.get("trialCount", len(trials)) == len(trials), "Trial count mismatch")
    require(len({row["id"] for row in trials}) == len(trials), "Duplicate variant ID")
    require(all(re.fullmatch(r"[A-Za-z0-9_-]+", row["id"]) for row in trials), "Unsafe variant ID")
    require(manifest.get("productionForkUnchanged") is True, "Record the unchanged production fork for isolated research")
    require(manifest["minimumAttributedFacePayloadBytes"] == (manifest["roiPixelCount"]+7)//8,
            "Fixed mask area and face floor mismatch")
    return manifest, data


def load_masks(source_path, source, manifest):
    coding_path = Path(manifest["maskPath"])
    captured = {"codingFace": checked_bytes(coding_path, manifest["maskSha256"])}
    frozen = json.loads((source_path.parent/"research-encodings/manifest.json").read_text())
    require(frozen["crop_sha256"] == manifest["sourceCropSha256"]
            and frozen["face_mask_sha256"] == manifest["maskSha256"], "Frozen source/coding mask bundle mismatch")
    paths = dict(codingFace=coding_path, face=source_path.parent/"quality-face-mask.pgm", head=source_path.parent/"head-mask.pgm")
    captured["face"] = checked_bytes(paths["face"], frozen["quality_face_mask_sha256"])
    captured["head"] = checked_bytes(paths["head"], frozen["head_mask_sha256"])
    masks = {}
    for name, path in paths.items():
        with Image.open(io.BytesIO(captured[name])) as image:
            pixels = np.array(image)
        require(pixels.ndim == 2 and pixels.shape == source.shape[:2] and np.any(pixels), "Frozen mask geometry mismatch")
        masks[name] = pixels != 0
    require(int(masks["codingFace"].sum()) == manifest["roiPixelCount"] and np.all(masks["head"][masks["face"]]),
            "Fixed coding mask area or anatomical quality support mismatch")
    masks.update(hair=masks["head"] & ~masks["face"], outsideFace=~masks["face"],
                 wholeImage=np.ones(source.shape[:2], dtype=bool))
    return masks, {name: dict(path=str(path), sha256=digest(captured[name]), pixels=int(masks[name].sum()))
                   for name, path in paths.items()}


def endpoint_map(path, expected_hash):
    data = checked_bytes(path, expected_hash)
    blocks = json.loads(data)["blocks"]
    require(len({block["id"] for block in blocks}) == len(blocks), "Duplicate exported coding block")
    return {block["id"]: block for block in blocks}, data


def endpoint_evidence(trial, row, baseline, baseline_blocks=None):
    """Reconcile targeted body costs with the actual frozen control ledger."""
    if trial.get("changes") is None:
        return dict(scope="Global allocator coding-endpoint reoptimization")
    derived = dict(bodyByteDelta=row["packetBodyBytes"]-baseline["packetBodyBytes"],
                   packetHeaderByteDelta=row["packetHeaderBytes"]-baseline["packetHeaderBytes"],
                   completeJp2ByteDelta=row["bytes"]-baseline["bytes"])
    aliases = dict(bodyByteDelta="packetBodyDeltaBytes", packetHeaderByteDelta="packetHeaderDeltaBytes",
                   completeJp2ByteDelta="completeByteDelta")
    for key, value in derived.items():
        require(trial.get(key, trial.get(aliases[key], value)) == value, "Actual frozen-baseline delta mismatch: "+key)
    require(derived["bodyByteDelta"]+derived["packetHeaderByteDelta"] == derived["completeJp2ByteDelta"],
            "Targeted body/header costs fail complete output closure")
    changes = trial["changes"]
    map_evidence = None
    if isinstance(changes, dict):
        require(baseline_blocks is not None, "Frozen exported baseline coding map is required")
        blocks, captured = endpoint_map(trial["blockMapPath"], trial["blockMapSha256"])
        require(set(blocks) == set(baseline_blocks) and set(changes).issubset(blocks), "Frozen coding block identities changed")
        require(set(changes) == set(trial["receiverChanges"]) | set(trial["donorChanges"])
                and not set(trial["receiverChanges"]) & set(trial["donorChanges"]), "Receiver/donor endpoint partition mismatch")
        require(all(endpoint == changes[identifier] for mapping in (trial["receiverChanges"], trial["donorChanges"])
                    for identifier, endpoint in mapping.items()), "Receiver/donor endpoint values mismatch")
        def body(block, endpoint):
            require(isinstance(endpoint, int) and -1 <= endpoint < len(block["bytesByPass"]), "Coding endpoint is outside its exported pass table")
            require(endpoint == -1 or endpoint in block["legalPasses"], "Coding endpoint is outside its frozen legal pass set")
            return block["bytesByPass"][endpoint] if endpoint >= 0 else 0
        actual_changes = []
        body_total = 0
        face_total = 0.
        for identifier, block in blocks.items():
            previous = baseline_blocks[identifier]
            require(block["bytesByPass"] == previous["bytesByPass"]
                    and block["faceCreditByPass"] == previous["faceCreditByPass"]
                    and block["legalPasses"] == previous["legalPasses"], "Frozen encoded pass tables changed")
            old, new = previous["terminalPass"], block["terminalPass"]
            require(new == changes.get(identifier, old), "Untargeted endpoint changed or recorded endpoint mismatch")
            body_total += body(block, new)
            face_total += block["faceCreditByPass"][new] if new >= 0 else 0.
            if new != old:
                actual_changes.append(dict(blockId=identifier, fromPass=old, toPass=new,
                    baselineBodyBytes=body(previous, old), variantBodyBytes=body(block, new)))
        require(body_total == row["packetBodyBytes"], "Exported endpoint lengths fail actual packet-body closure")
        require(len(actual_changes) == len(changes), "Recorded endpoint changes include a no-op")
        require(math.isfinite(face_total) and abs(face_total-row["faceByteEstimate"]) <= 1e-7
                and math.floor(face_total) == row["faceBytes"], "Exported endpoint face credits fail actual ledger closure")
        require(trial["unchangedEndpointCount"] == len(blocks)-len(changes), "Frozen untargeted endpoint count mismatch")
        changes = actual_changes
        map_evidence = dict(path=trial["blockMapPath"], sha256=digest(captured), totalEndpoints=len(blocks),
                            targetedEndpoints=len(trial["changes"]), changedEndpoints=len(changes),
                            independentEndpointAndBodyReconciliation=True, independentFaceCreditReconciliation=True,
                            selectedFaceByteEstimate=face_total)
    require(len({change["blockId"] for change in changes}) == len(changes),
            "Duplicate changed coding block")
    costs = change_costs(dict(trial, changes=changes, **derived))
    for key, computed in (("receiverBodyCost", costs["receiverAddedBodyBytes"]), ("donorBodyFreed", costs["donorReleasedBodyBytes"])):
        require(trial.get(key, computed) == computed, "Recorded receiver/donor body cost mismatch: "+key)
    frozen = trial.get("allUntargetedEndpointsFrozen") is True
    unchanged = trial.get("unchangedEndpointCount")
    if frozen:
        require(isinstance(unchanged, int) and unchanged >= 0, "Record the unchanged endpoint count")
    return dict(scope="Targeted frozen-baseline coding-endpoint exchange" if frozen else "Recorded coding-endpoint changes",
                allUntargetedEndpointsFrozen=frozen, unchangedEndpointCount=unchanged,
                endpointEvidenceScope="Exported coding endpoints and actual body/header/output costs reconciled" if map_evidence else
                    "Manifest-recorded endpoint identities; independently reconciled body/header/output costs",
                baselineJp2Sha256=baseline["jp2Sha256"], costs=costs, blockMapEvidence=map_evidence)


def allocation_caption(selected):
    evidence = selected.get("endpointEvidence", {})
    if evidence.get("allUntargetedEndpointsFrozen"):
        proof = evidence.get("blockMapEvidence")
        suffix = "remaining exported endpoints verified frozen" if proof else "remaining endpoints recorded as frozen"
        return "Fixed source and masks. Targeted coding-pass exchange; "+suffix+"."
    return "Fixed source and masks. Allocator research candidates reoptimize coding endpoints."


def verify_output(data, codec, manifest, reference_structure, block=64):
    structure = inspect_jp2(data, (manifest["width"], manifest["height"]), block)
    require(len(data) <= manifest["maximumJp2Bytes"], "Variant exceeds the fixed JP2 cap")
    for key in ("quantizationSegments", "rgn"):
        require(structure[key] == reference_structure[key], "Frozen quantizer/ROI markers changed: "+key)
    if block == 64:
        require(structure["codingSegments"] == reference_structure["codingSegments"], "Frozen coding markers changed")
    check_ledger(codec["payloadTelemetry"], codec["sharedPayloadAttribution"], structure, manifest["roiPixelCount"])
    attribution = codec["sharedPayloadAttribution"]
    return structure, attribution["attributedFacePayloadBytes"] >= manifest["minimumAttributedFacePayloadBytes"]


def render(output, root, source, target, baseline, selected):
    rows = [target, baseline, selected]
    images = [Image.fromarray(source)]
    for row in rows:
        with Image.open(root/row["decodedPngPath"]) as image:
            images.append(image.convert("RGB"))
    labels = [["Frozen source", f"Native {source.shape[1]} x {source.shape[0]} RGB"]]
    labels += [[title, f'{row["bytes"]:,} JP2 B; {row["faceBytes"]:,} face B'] for title, row in zip(
        ("Balanced visual target", "Regional-floor baseline", "Allocator research candidate"), rows)]
    ledger_status = "passes recorded JP2 cap and V3 face floor" if selected["meetsByteAndRegionalGates"] else "passes JP2 cap; face credit is below recorded floor"
    face_status = "within research tolerance" if selected["gate"]["faceGuardPassed"] else "loss exceeds research tolerance"
    candidate_caption = "Candidate ledger: "+ledger_status+". Face quality: "+face_status+"."
    contour_status = "both contour errors improved" if selected["gate"]["edgePositionAndJaggednessImproved"] else "mixed or higher contour errors"
    other_status = "within separate bounds" if selected["gate"]["otherErrorBoundsPassed"] else "separate error bounds exceeded"
    tradeoff_caption = "Research tradeoffs: "+contour_status+"; "+other_status+". Human visual selection remains separate."
    panel(images, labels, output/"native-comparison.png", [
        "Native portrait samples. "+allocation_caption(selected),
        candidate_caption,
        tradeoff_caption,
        "Separate face, hair, contour, halo, contrast, seam and chroma measurements accompany human visual selection."])
    panel([image.crop((0, 480, source.shape[1], source.shape[0])) for image in images], labels,
          output/"native-neckline-comparison.png", ["Unscaled source rows 480 through 639; portrait pixels are preserved."])
    proxies = []
    for width_mm, role in ((27.75, "Recommended photograph width"), (28.25, "Requested width control")):
        geometry = print_geometry(source.shape[1], source.shape[0], width_mm)
        size = (geometry["rasterWidthPixels"], geometry["rasterHeightPixels"])
        resized = [image.resize(size, Image.Resampling.LANCZOS) for image in images]
        for name, image in zip(("source", "balanced-target", "floor-baseline", "allocator-candidate"), resized):
            image.save(output/f"print-{width_mm:.2f}mm-{name}.png", dpi=(300, 300))
        print_labels = [["Frozen source", f"RGB proxy {size[0]} x {size[1]}"]] + labels[1:]
        panel(resized, print_labels, output/f"print-{width_mm:.2f}mm-comparison.png", [
            f"{role}: {width_mm:.2f} mm at 300 dpi; rounded raster {size[0]} x {size[1]} pixels.",
            candidate_caption,
            tradeoff_caption,
            "Software raster proxy. Screen scaling and physical credential printing have separate review evidence."])
        geometry["role"] = role
        geometry["nativeErrorConversions"] = {row["id"]: {key.replace("Pixels", "Mm"): row["metrics"][key]*width_mm/source.shape[1]
            for key in ("necklinePositionRmsPixels", "necklineJaggednessRmsPixels", "transitionWidthErrorRmsPixels")}
            for row in rows if finite_metrics(row["metrics"])}
        proxies.append(geometry)
    return proxies


def score(manifest_path, output, root, face_tolerance=.10, regression_limit=.10, ids=None, render_candidate=None,
          render_outputs=True):
    manifest, original_manifest = checked_manifest(manifest_path)
    require((manifest["width"], manifest["height"]) == (480, 640), "Medical neckline preset uses the frozen 480 x 640 crop")
    source_path = Path(manifest["sourceCropPath"])
    source_data = checked_bytes(source_path, manifest["sourceCropSha256"])
    with Image.open(io.BytesIO(source_data)) as image:
        require(image.mode == "RGB" and image.size == (480, 640), "Expected frozen RGB source geometry")
        source = np.array(image)
    masks, mask_evidence = load_masks(source_path, source, manifest)
    reference = make_reference(source)
    controls_path = root/"artifacts/piv-encoding-current/landmark-v3/medical.json"
    controls = json.loads(controls_path.read_text())["results"]
    balanced_control = next(row for row in controls if row["name"] == "balanced-start4-b64-noquota")
    floor_control = next(row for row in controls if row["name"] == "balanced-start4-b64-regional")
    floor_data = checked_bytes(floor_control["jp2Path"], floor_control["jp2Sha256"])
    reference_structure = inspect_jp2(floor_data, (480, 640))
    output.mkdir(parents=True, exist_ok=True)
    decode_dir = output/"independent-decodes"
    decode_dir.mkdir(exist_ok=True)

    def decode(identifier, path, expected_hash, codec, options=None):
        data = checked_bytes(path, expected_hash)
        block = (options or {}).get("codeBlockWidth", 64)
        structure, floor_met = verify_output(data, codec, manifest, reference_structure, block)
        with Image.open(io.BytesIO(data)) as image:
            image.load()
            require(image.mode == "RGB" and image.size == (480, 640), "Independent decode RGB geometry mismatch")
            pixels = np.array(image)
        png = decode_dir/(identifier+".png")
        Image.fromarray(pixels).save(png)
        ledger = codec["sharedPayloadAttribution"]
        telemetry = codec["payloadTelemetry"]
        row = dict(id=identifier, jp2Path=relative(path, root), jp2Sha256=digest(data), bytes=len(data),
                   decodedPngPath=relative(png, root), decodedPngSha256=digest(png.read_bytes()),
                   faceBytes=ledger["attributedFacePayloadBytes"], faceByteEstimate=ledger["facePayloadByteEstimate"],
                   faceRatio=3*manifest["roiPixelCount"]/ledger["attributedFacePayloadBytes"] if ledger["attributedFacePayloadBytes"] else None,
                   packetBodyBytes=ledger["packetBodyBytes"], outsideBytes=ledger["outsidePayloadBytes"],
                   packetHeaderBytes=telemetry["packetHeaderBytes"], meetsByteAndRegionalGates=floor_met,
                   structure=structure, sharedPayloadAttribution=ledger, payloadTelemetry=telemetry,
                   metrics=measure(reference, pixels), quality=quality(source, pixels, masks), options=options)
        return row

    target = decode("balanced-visual-target", balanced_control["jp2Path"], balanced_control["jp2Sha256"], balanced_control["apiResult"])
    baseline = decode("regional-floor-baseline", floor_control["jp2Path"], floor_control["jp2Sha256"], floor_control["apiResult"])
    require(baseline["meetsByteAndRegionalGates"], "Frozen floor control misses the recorded face floor")
    baseline_blocks = None
    replay = next((trial for trial in manifest["trials"] if trial.get("id") == "exact-replay-control"), None)
    if replay is not None and replay.get("blockMapPath"):
        require(manifest.get("baselineSha256") == baseline["jp2Sha256"]
                and replay["sha256"] == baseline["jp2Sha256"], "Exact intervention baseline differs from the frozen floor control")
        baseline_blocks, _ = endpoint_map(replay["blockMapPath"], replay["blockMapSha256"])
    rows, rejected = [], []
    if ids:
        require(set(ids).issubset({row["id"] for row in manifest["trials"]}), "Requested variant ID is absent")
    for trial in manifest["trials"]:
        if ids and trial["id"] not in ids:
            continue
        if trial["status"] in FAILED_STATUSES:
            require(trial.get("jp2Path") is None and trial.get("sha256") is None, "Rejected allocator variant has portrait output")
            rejected.append(dict(id=trial["id"], status=trial["status"], error=trial.get("error"), options=trial.get("options")))
            continue
        require(trial["status"] in ACCEPTED_STATUSES, "Unknown variant status: "+trial["status"])
        row = decode(trial["id"], trial["jp2Path"], trial["sha256"], trial, trial.get("options"))
        require(row["bytes"] == trial["completeJp2Bytes"], "Manifest output size mismatch")
        if trial["status"] == "accepted-cap-and-face-floor":
            require(row["meetsByteAndRegionalGates"], "Accepted allocator variant misses the measured floor")
        row.update(status=trial["status"], experiment=trial.get("experiment", trial.get("kind")),
                   allocationDiagnostics=trial.get("allocationDiagnostics"),
                   anchors=trial.get("anchors"), anchoredBodyBytes=trial.get("anchoredBodyBytes"),
                   gate=gate_candidate(row, baseline, face_tolerance, regression_limit),
                   gapClosureByMetric=gap_closure(row["metrics"], baseline["metrics"], target["metrics"]),
                   metricDeltasToFloor={key: row["metrics"][key]-baseline["metrics"][key]
                       if row["metrics"][key] is not None else None for key in EDGE_KEYS},
                   qualityPsnrDeltasToFloor=psnr_deltas(row["quality"], baseline["quality"]),
                   qualityPsnrDeltasToBalancedTarget=psnr_deltas(row["quality"], target["quality"]),
                   sourceErrorMseDeltasToFloor={key: row["quality"][key]["mseRgb"]-baseline["quality"][key]["mseRgb"] for key in masks})
        row["endpointEvidence"] = endpoint_evidence(trial, row, baseline, baseline_blocks)
        if row["endpointEvidence"].get("blockMapEvidence"):
            row["endpointEvidence"]["blockMapEvidence"]["path"] = relative(trial["blockMapPath"], root)
        if trial["id"] == "exact-replay-control":
            require(row["jp2Sha256"] == baseline["jp2Sha256"]
                    and row["decodedPngSha256"] == baseline["decodedPngSha256"], "Exact replay changed JP2 bytes or decoded samples")
        rows.append(row)
        print(row["id"], row["bytes"], "B; face", row["faceBytes"], "B; position", row["metrics"][EDGE_KEYS[0]],
              "jagged", row["metrics"][EDGE_KEYS[1]], flush=True)
    require(manifest_path.read_bytes() == original_manifest, "Variant manifest changed during scoring; rerun the completed snapshot")
    candidates = sorted((row for row in rows if row["gate"]["conservativeRepairEligible"]),
                        key=lambda row: (row["gate"]["contourErrorGeometricMeanRatio"], -face_sort_value(row), row["bytes"], row["id"]))
    selected = candidates[0] if candidates else None
    if render_candidate:
        selected = next((row for row in rows if row["id"] == render_candidate), None)
        require(selected is not None and finite_metrics(selected["metrics"]), "Choose an encoded candidate with complete edge measurements")
    proxies = render(output, root, source, target, baseline, selected) if selected and render_outputs else []
    generated_names = ["native-comparison.png", "native-neckline-comparison.png"] if selected and render_outputs else []
    if selected and render_outputs:
        generated_names += [f"print-{width_mm:.2f}mm-{name}.png" for width_mm in (27.75, 28.25)
                            for name in ("source", "balanced-target", "floor-baseline", "allocator-candidate", "comparison")]
    report = dict(schemaVersion=1, manifestPath=relative(manifest_path, root), manifestSha256=digest(original_manifest),
                  analysisScriptSha256=digest(Path(__file__).read_bytes()),
                  verificationHelperSha256=digest((Path(__file__).parent/"analyze_passes.py").read_bytes()),
                  controlManifestSha256=digest(controls_path.read_bytes()),
                  edgeMetricModuleSha256=digest((Path(__file__).parent/"edge_quality.py").read_bytes()),
                  decoder="Pillow/OpenJPEG "+str(features.version("jpg_2000")), methodId=METHOD,
                  sourceCropPath=relative(source_path, root), sourceCropSha256=manifest["sourceCropSha256"],
                  fixedMaskEvidence={key: dict(value, path=relative(value["path"], root)) for key, value in mask_evidence.items()},
                  imageByteCap=manifest["maximumJp2Bytes"], minimumAttributedFacePayloadBytes=manifest["minimumAttributedFacePayloadBytes"],
                  codecAssemblySha256=manifest.get("codecAssemblySha256"),
                  sourceRegionEvidence=dict(region=reference["region"].__dict__,
                      contoursSha256=digest(np.asarray(reference["contours"]).tobytes()),
                      clothingInteriorSha256=digest(reference["interior"].tobytes()), seamsSha256=digest(reference["seams"].tobytes())),
                  target=target, floorBaseline=baseline, variants=rows, rejected=rejected,
                  totalManifestTrials=len(manifest["trials"]), selectedIds=ids,
                  selection=dict(faceLossToleranceDb=face_tolerance, maximumSeparateOtherErrorRegression=regression_limit,
                      toleranceScope="Explicit research acceptance guards; each measured error remains separate",
                      conservativeRanking=[row["id"] for row in candidates],
                      zeroFaceLossRanking=[row["id"] for row in candidates if row["gate"]["strictZeroFaceLossPassed"]],
                      selectedConservativeCandidate=selected["id"] if selected else None,
                      renderedCandidate=selected["id"] if selected and render_outputs else None, explicitlyRequestedRender=render_candidate,
                      renderedMeetsByteAndRegionalGates=selected["meetsByteAndRegionalGates"] if selected and render_outputs else None,
                      renderedPassesConservativeResearchGuards=selected["gate"]["conservativeRepairEligible"] if selected and render_outputs else None,
                      visualTarget="User-preferred balanced visual control", humanSelection="Pending native and intended-size review"),
                  gapClosureScope="Each metric separately: zero is the floor baseline, one reaches the balanced visual target, negative worsens the floor baseline",
                  printRasterProxies=proxies,
                  productionDefaults="Preserved; candidates use isolated allocator implementations",
                  artifacts={name: dict(path=relative(output/name, root), sha256=digest((output/name).read_bytes()))
                             for name in generated_names})
    (output/"scores.json").write_text(json.dumps(report, indent=2, allow_nan=False)+"\n")
    print(json.dumps(dict(encoded=len(rows), rejected=len(rejected), conservativeCandidates=len(candidates),
                         renderedCandidate=selected["id"] if selected and render_outputs else None, report=relative(output/"scores.json", root)), indent=2))
    return report


def main():
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--id", action="append", help="Score specified IDs, retaining both frozen visual controls")
    parser.add_argument("--render-candidate", help="Render an explicit tradeoff candidate alongside both controls")
    parser.add_argument("--maximum-face-loss-db", type=float, default=.10)
    parser.add_argument("--maximum-other-error-regression", type=float, default=.10)
    args = parser.parse_args()
    require(features.check("jpg_2000"), "Independent OpenJPEG decoder is required")
    require(math.isfinite(args.maximum_face_loss_db) and args.maximum_face_loss_db >= 0
            and math.isfinite(args.maximum_other_error_regression) and args.maximum_other_error_regression >= 0,
            "Supply finite nonnegative research acceptance guards")
    score(args.manifest, args.output or args.manifest.parent/"quality-analysis", root,
          args.maximum_face_loss_db, args.maximum_other_error_regression, args.id, args.render_candidate)


if __name__ == "__main__":
    main()

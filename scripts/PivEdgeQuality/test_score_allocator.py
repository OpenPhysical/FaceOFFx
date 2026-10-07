import copy
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from analyze_passes import EDGE_KEYS, METHOD
from score_allocator import allocation_caption, checked_manifest, endpoint_evidence, gap_closure, psnr_deltas, verify_output


class AllocatorScoringTests(unittest.TestCase):
    def setUp(self):
        self.manifest = dict(completed=True, methodIdentifier=METHOD, trialCount=1,
                             productionForkUnchanged=True, roiPixelCount=8,
                             minimumAttributedFacePayloadBytes=1, maximumJp2Bytes=100,
                             width=480, height=640, trials=[dict(id="example", status="encoded")])

    def read_manifest(self, manifest):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary)/"manifest.json"
            path.write_text(json.dumps(manifest))
            return checked_manifest(path)[0]

    def test_complete_manifest_is_accepted(self):
        self.assertEqual(self.read_manifest(self.manifest)["trialCount"], 1)

    def test_incomplete_manifest_is_rejected(self):
        self.manifest["completed"] = False
        with self.assertRaisesRegex(ValueError, "completed"):
            self.read_manifest(self.manifest)

    def test_duplicate_ids_are_rejected(self):
        self.manifest["trials"] *= 2
        self.manifest["trialCount"] = 2
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            self.read_manifest(self.manifest)

    def test_unsafe_ids_are_rejected(self):
        self.manifest["trials"][0]["id"] = "../other"
        with self.assertRaisesRegex(ValueError, "Unsafe"):
            self.read_manifest(self.manifest)

    def test_mask_floor_mismatch_is_rejected(self):
        self.manifest["minimumAttributedFacePayloadBytes"] = 2
        with self.assertRaisesRegex(ValueError, "face floor"):
            self.read_manifest(self.manifest)

    def test_unknown_production_fingerprint_state_is_rejected(self):
        del self.manifest["productionForkUnchanged"]
        with self.assertRaisesRegex(ValueError, "production fork"):
            self.read_manifest(self.manifest)

    def test_each_metric_gap_remains_separate(self):
        baseline = {key: 4. for key in EDGE_KEYS}
        target = {key: 2. for key in EDGE_KEYS}
        actual = {key: 3. for key in EDGE_KEYS}
        actual[EDGE_KEYS[-1]] = 5.
        result = gap_closure(actual, baseline, target)
        self.assertEqual(result[EDGE_KEYS[0]], .5)
        self.assertEqual(result[EDGE_KEYS[-1]], -.5)

    def test_missing_or_nonfinite_metric_gap_is_explicit(self):
        baseline = {key: 4. for key in EDGE_KEYS}
        target = {key: 2. for key in EDGE_KEYS}
        for value in (None, float("nan"), float("inf")):
            actual = {key: 3. for key in EDGE_KEYS}
            actual[EDGE_KEYS[-1]] = value
            self.assertIsNone(gap_closure(actual, baseline, target)[EDGE_KEYS[-1]])

    def test_equal_target_baseline_has_no_division_by_zero(self):
        values = {key: 0. for key in EDGE_KEYS}
        self.assertTrue(all(value is None for value in gap_closure(values, values, values).values()))

    def test_psnr_delta_records_exact_source_equality_explicitly(self):
        exact = dict(face=dict(psnrRgbDb=None, exactPixelEquality=True))
        lossy = dict(face=dict(psnrRgbDb=30., exactPixelEquality=False))
        self.assertEqual(psnr_deltas(exact, exact), {"face": 0.})
        self.assertEqual(psnr_deltas(exact, lossy), {"face": None})
        self.assertEqual(psnr_deltas(lossy, exact), {"face": None})

    def test_actual_cap_is_checked_independently_of_manifest_claim(self):
        structure = dict(quantizationSegments=[], rgn=[], codingSegments=[])
        with patch("score_allocator.inspect_jp2", return_value=structure):
            with self.assertRaisesRegex(ValueError, "JP2 cap"):
                verify_output(bytes(101), {}, self.manifest, structure)

    def test_changed_quantizer_is_rejected_before_scoring(self):
        structure = dict(quantizationSegments=[{"sha256": "changed"}], rgn=[], codingSegments=[])
        original = copy.deepcopy(structure)
        original["quantizationSegments"][0]["sha256"] = "frozen"
        with patch("score_allocator.inspect_jp2", return_value=structure):
            with self.assertRaisesRegex(ValueError, "quantizer"):
                verify_output(bytes(100), {}, self.manifest, original)

    def endpoint_fixture(self):
        baseline = dict(packetBodyBytes=100, packetHeaderBytes=10, bytes=200, jp2Sha256="frozen")
        row = dict(packetBodyBytes=98, packetHeaderBytes=11, bytes=199)
        trial = dict(allUntargetedEndpointsFrozen=True, unchangedEndpointCount=8, changes=[
            dict(blockId="t0-c2-r4-s1-b4", fromPass=1, toPass=2, baselineBodyBytes=10, variantBodyBytes=13),
            dict(blockId="t0-c0-r5-s2-b1", fromPass=3, toPass=2, baselineBodyBytes=20, variantBodyBytes=15)])
        return trial, row, baseline

    def test_exact_endpoint_costs_close_actual_body_header_and_output(self):
        evidence = endpoint_evidence(*self.endpoint_fixture())
        self.assertEqual(evidence["costs"]["receiverAddedBodyBytes"], 3)
        self.assertEqual(evidence["costs"]["donorReleasedBodyBytes"], 5)
        self.assertEqual(evidence["costs"]["completeJp2ByteDelta"], -1)
        self.assertTrue(evidence["allUntargetedEndpointsFrozen"])

    def test_claimed_endpoint_delta_must_match_actual_output(self):
        trial, row, baseline = self.endpoint_fixture()
        trial["completeJp2ByteDelta"] = 0
        with self.assertRaisesRegex(ValueError, "delta mismatch"):
            endpoint_evidence(trial, row, baseline)

    def test_exact_endpoint_scope_requires_remaining_count(self):
        trial, row, baseline = self.endpoint_fixture()
        del trial["unchangedEndpointCount"]
        with self.assertRaisesRegex(ValueError, "unchanged endpoint count"):
            endpoint_evidence(trial, row, baseline)

    def test_duplicate_endpoint_changes_are_rejected(self):
        trial, row, baseline = self.endpoint_fixture()
        trial["changes"][1]["blockId"] = trial["changes"][0]["blockId"]
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            endpoint_evidence(trial, row, baseline)

    def test_global_allocator_scope_remains_distinct_from_exact_exchange(self):
        evidence = endpoint_evidence({}, {}, {})
        self.assertIn("Global", evidence["scope"])
        self.assertIn("reoptimize", allocation_caption(dict(endpointEvidence=evidence)))
        frozen = endpoint_evidence(*self.endpoint_fixture())
        self.assertIn("remaining endpoints recorded as frozen", allocation_caption(dict(endpointEvidence=frozen)))

    def frozen_map_fixture(self):
        old = {"t0-c2-r4-s1-b4": dict(terminalPass=0, legalPasses=[0, 1], bytesByPass=[10, 13], faceCreditByPass=[1., 2.]),
               "t0-c0-r5-s2-b1": dict(terminalPass=1, legalPasses=[0, 1], bytesByPass=[15, 20], faceCreditByPass=[10., 15.]),
               "t0-c0-r0-s0-b0": dict(terminalPass=0, legalPasses=[0], bytesByPass=[70], faceCreditByPass=[20.])}
        updated = copy.deepcopy(old)
        updated["t0-c2-r4-s1-b4"]["terminalPass"] = 1
        updated["t0-c0-r5-s2-b1"]["terminalPass"] = 0
        trial, row, baseline = self.endpoint_fixture()
        row.update(faceByteEstimate=32., faceBytes=32)
        trial.update(changes={"t0-c2-r4-s1-b4": 1, "t0-c0-r5-s2-b1": 0},
                     receiverChanges={"t0-c2-r4-s1-b4": 1}, donorChanges={"t0-c0-r5-s2-b1": 0},
                     unchangedEndpointCount=1, blockMapPath="map.json", blockMapSha256="hash")
        return trial, row, baseline, old, updated

    def test_exported_untargeted_endpoints_and_actual_body_close(self):
        trial, row, baseline, old, updated = self.frozen_map_fixture()
        with patch("score_allocator.endpoint_map", return_value=(updated, b"captured")):
            evidence = endpoint_evidence(trial, row, baseline, old)
        self.assertTrue(evidence["blockMapEvidence"]["independentEndpointAndBodyReconciliation"])
        self.assertEqual(evidence["blockMapEvidence"]["changedEndpoints"], 2)

    def test_changed_untargeted_endpoint_is_rejected(self):
        trial, row, baseline, old, updated = self.frozen_map_fixture()
        updated["t0-c0-r0-s0-b0"]["terminalPass"] = -1
        with patch("score_allocator.endpoint_map", return_value=(updated, b"captured")):
            with self.assertRaisesRegex(ValueError, "Untargeted endpoint"):
                endpoint_evidence(trial, row, baseline, old)

    def test_changed_encoded_pass_table_is_rejected(self):
        trial, row, baseline, old, updated = self.frozen_map_fixture()
        updated["t0-c2-r4-s1-b4"]["bytesByPass"][1] += 1
        with patch("score_allocator.endpoint_map", return_value=(updated, b"captured")):
            with self.assertRaisesRegex(ValueError, "pass tables"):
                endpoint_evidence(trial, row, baseline, old)

    def test_nonlegal_native_endpoint_is_rejected(self):
        trial, row, baseline, old, updated = self.frozen_map_fixture()
        old["t0-c2-r4-s1-b4"]["legalPasses"] = updated["t0-c2-r4-s1-b4"]["legalPasses"] = [0]
        with patch("score_allocator.endpoint_map", return_value=(updated, b"captured")):
            with self.assertRaisesRegex(ValueError, "legal pass"):
                endpoint_evidence(trial, row, baseline, old)

    def test_selected_face_credit_sum_must_match_actual_ledger(self):
        trial, row, baseline, old, updated = self.frozen_map_fixture()
        row["faceByteEstimate"] += .25
        with patch("score_allocator.endpoint_map", return_value=(updated, b"captured")):
            with self.assertRaisesRegex(ValueError, "face credits"):
                endpoint_evidence(trial, row, baseline, old)

    def test_recorded_receiver_endpoint_must_match_changes(self):
        trial, row, baseline, old, updated = self.frozen_map_fixture()
        trial["receiverChanges"]["t0-c2-r4-s1-b4"] = 0
        with patch("score_allocator.endpoint_map", return_value=(updated, b"captured")):
            with self.assertRaisesRegex(ValueError, "values mismatch"):
                endpoint_evidence(trial, row, baseline, old)

    def test_recorded_noop_cannot_inflate_targeted_count(self):
        trial, row, baseline, old, updated = self.frozen_map_fixture()
        trial["changes"]["t0-c0-r0-s0-b0"] = trial["receiverChanges"]["t0-c0-r0-s0-b0"] = 0
        trial["unchangedEndpointCount"] = 0
        with patch("score_allocator.endpoint_map", return_value=(updated, b"captured")):
            with self.assertRaisesRegex(ValueError, "no-op"):
                endpoint_evidence(trial, row, baseline, old)


if __name__ == "__main__":
    unittest.main()

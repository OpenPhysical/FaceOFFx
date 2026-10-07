import copy
import struct
import tempfile
import unittest
from pathlib import Path

import numpy as np

from analyze_passes import (
    EDGE_KEYS, change_costs, checked_bytes, digest, finite_metrics, gate_candidate,
    inspect_jp2, pixel_changes, print_geometry,
)


class PassAnalysisTests(unittest.TestCase):
    def setUp(self):
        self.baseline = dict(metrics={key: 2. for key in EDGE_KEYS},
                             quality={"face": {"psnrRgbDb": 35.}}, meetsByteAndRegionalGates=True)
        self.baseline["metrics"].update(necklineMissingRows=0, transitionMissingSamples=0,
                                        clothingInteriorPixels=100, seamSupportPixels=10, necklineSamples=20)
        self.candidate = copy.deepcopy(self.baseline)
        self.candidate["metrics"][EDGE_KEYS[0]] = 1.
        self.candidate["metrics"][EDGE_KEYS[1]] = 1.

    def test_complete_improvement_passes_conservative_guard(self):
        self.assertTrue(gate_candidate(self.candidate, self.baseline)["conservativeRepairEligible"])

    def test_missing_contour_is_ineligible(self):
        self.candidate["metrics"]["necklineMissingRows"] = 1
        self.assertFalse(gate_candidate(self.candidate, self.baseline)["eligible"])

    def test_missing_transition_is_ineligible(self):
        self.candidate["metrics"]["transitionMissingSamples"] = 1
        self.assertFalse(gate_candidate(self.candidate, self.baseline)["eligible"])

    def test_nonfinite_and_none_metrics_are_ineligible(self):
        for value in (None, float("nan"), float("inf")):
            candidate = copy.deepcopy(self.candidate)
            candidate["metrics"][EDGE_KEYS[-1]] = value
            self.assertFalse(finite_metrics(candidate["metrics"]))
            self.assertFalse(gate_candidate(candidate, self.baseline)["eligible"])

    def test_face_guard_tolerance_is_explicit(self):
        self.candidate["quality"]["face"]["psnrRgbDb"] = 34.91
        gate = gate_candidate(self.candidate, self.baseline)
        self.assertTrue(gate["faceGuardPassed"])
        self.assertFalse(gate["strictZeroFaceLossPassed"])
        self.candidate["quality"]["face"]["psnrRgbDb"] = 34.89
        self.assertFalse(gate_candidate(self.candidate, self.baseline)["eligible"])

    def test_exact_face_equality_is_a_quality_gain(self):
        self.candidate["quality"]["face"].update(psnrRgbDb=None, exactPixelEquality=True)
        gate = gate_candidate(self.candidate, self.baseline)
        self.assertTrue(gate["faceGuardPassed"])
        self.assertTrue(gate["strictZeroFaceLossPassed"])
        self.assertIsNone(gate["facePsnrDeltaDb"])

    def test_source_exact_face_baseline_requires_preservation(self):
        self.baseline["quality"]["face"].update(psnrRgbDb=None, exactPixelEquality=True)
        self.assertFalse(gate_candidate(self.candidate, self.baseline)["faceGuardPassed"])
        self.candidate["quality"]["face"].update(psnrRgbDb=None, exactPixelEquality=True)
        self.assertEqual(gate_candidate(self.candidate, self.baseline)["facePsnrDeltaDb"], 0)

    def test_missing_face_psnr_requires_exact_equality_evidence(self):
        self.candidate["quality"]["face"]["psnrRgbDb"] = None
        with self.assertRaisesRegex(ValueError, "exact pixel equality"):
            gate_candidate(self.candidate, self.baseline)

    def test_other_metric_regression_remains_separate(self):
        self.candidate["metrics"]["edgeHaloExcessRmsSampleUnits"] = 2.21
        gate = gate_candidate(self.candidate, self.baseline)
        self.assertTrue(gate["eligible"])
        self.assertFalse(gate["conservativeRepairEligible"])

    def test_unchanged_zero_error_passes_and_new_error_fails(self):
        key = "edgeHaloExcessRmsSampleUnits"
        self.baseline["metrics"][key] = self.candidate["metrics"][key] = 0.
        self.assertTrue(gate_candidate(self.candidate, self.baseline)["conservativeRepairEligible"])
        self.candidate["metrics"][key] = .01
        self.assertFalse(gate_candidate(self.candidate, self.baseline)["conservativeRepairEligible"])

    def test_zero_baseline_contour_error_preserves_valid_gate(self):
        self.baseline["metrics"][EDGE_KEYS[0]] = self.candidate["metrics"][EDGE_KEYS[0]] = 0.
        self.assertIsNone(gate_candidate(self.candidate, self.baseline)["contourErrorGeometricMeanRatio"])

    def test_face_floor_gate_is_required(self):
        self.candidate["meetsByteAndRegionalGates"] = False
        self.assertFalse(gate_candidate(self.candidate, self.baseline)["conservativeRepairEligible"])

    def test_both_contour_errors_must_improve(self):
        self.candidate["metrics"][EDGE_KEYS[1]] = 2.1
        self.assertFalse(gate_candidate(self.candidate, self.baseline)["conservativeRepairEligible"])

    def test_exact_receiver_and_donor_costs_close(self):
        trial = dict(changes=[
            dict(blockId="t0-c0-r4-s2-b4", fromPass=-1, toPass=4, baselineBodyBytes=0, variantBodyBytes=40),
            dict(blockId="t0-c0-r5-s3-b2", fromPass=12, toPass=11, baselineBodyBytes=80, variantBodyBytes=45),
        ], bodyByteDelta=5, packetHeaderByteDelta=2, completeJp2ByteDelta=7)
        costs = change_costs(trial)
        self.assertEqual(costs["receiverAddedBodyBytes"], 40)
        self.assertEqual(costs["donorReleasedBodyBytes"], 35)
        trial["bodyByteDelta"] = 6
        with self.assertRaisesRegex(ValueError, "closure"):
            change_costs(trial)

    def test_reversed_endpoint_cost_direction_is_rejected(self):
        trial = dict(changes=[dict(blockId="t0-c0-r4-s2-b4", fromPass=4, toPass=3,
                                  baselineBodyBytes=10, variantBodyBytes=20)],
                     bodyByteDelta=10, packetHeaderByteDelta=0, completeJp2ByteDelta=10)
        with self.assertRaisesRegex(ValueError, "direction"):
            change_costs(trial)

    def test_signed_change_uses_actual_source_error(self):
        source = np.array([[[100, 100, 100], [100, 100, 100]]], dtype=np.uint8)
        baseline = np.array([[[110, 110, 110], [100, 100, 100]]], dtype=np.uint8)
        candidate = np.array([[[105, 105, 105], [103, 103, 103]]], dtype=np.uint8)
        delta, improvement = pixel_changes(source, baseline, candidate)
        np.testing.assert_array_equal(delta, [[[-5, -5, -5], [3, 3, 3]]])
        np.testing.assert_array_equal(improvement, [[75, -9]])

    def test_exact_replay_has_zero_change(self):
        source = np.full((2, 2, 3), 100, dtype=np.uint8)
        delta, improvement = pixel_changes(source, source+3, source+3)
        self.assertFalse(delta.any())
        self.assertFalse(improvement.any())

    def test_changed_pixels_can_preserve_source_error(self):
        source = np.full((2, 2, 3), 100, dtype=np.uint8)
        delta, improvement = pixel_changes(source, source+3, source-3)
        self.assertTrue(delta.any())
        self.assertFalse(improvement.any())

    def test_requested_and_recommended_print_rounding(self):
        requested = print_geometry(480, 640, 28.25)
        recommended = print_geometry(480, 640, 27.75)
        self.assertEqual((requested["rasterWidthPixels"], requested["rasterHeightPixels"]), (334, 445))
        self.assertEqual((recommended["rasterWidthPixels"], recommended["rasterHeightPixels"]), (328, 437))
        self.assertAlmostEqual(requested["realizedWidthMm"], 28.278666666666666)

    def test_source_hash_tampering_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary)/"fixture.bin"
            path.write_bytes(b"original")
            self.assertEqual(checked_bytes(path, digest(b"original")), b"original")
            path.write_bytes(b"changed")
            with self.assertRaisesRegex(ValueError, "SHA256"):
                checked_bytes(path, digest(b"original"))

    def test_truncated_jp2_is_rejected(self):
        with self.assertRaises(ValueError):
            inspect_jp2(b"\x00\x00", (480, 640))

    def test_nonzero_siz_profile_is_rejected(self):
        def box(kind, payload):
            return struct.pack(">I4s", 8+len(payload), kind)+payload
        siz = struct.pack(">HHIIIIIIIIH", 47, 1, 480, 640, 0, 0, 480, 640, 0, 0, 3)+b"\x07\x01\x01"*3
        data = (box(b"jP  ", b"\r\n\x87\n")
                +box(b"jp2h", box(b"colr", b"\x01\x00\x00\x00\x00\x00\x10"))
                +box(b"jp2c", b"\xffO\xffQ"+siz+b"\xff\xd9"))
        with self.assertRaisesRegex(ValueError, "Part 1 Rsiz"):
            inspect_jp2(data, (480, 640))


if __name__ == "__main__":
    unittest.main()

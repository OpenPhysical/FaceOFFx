import unittest

import numpy as np

from decoded_response import measure_response, predict_composed_response, response_interference


class DecodedResponseTests(unittest.TestCase):
    def setUp(self):
        self.source = np.full((4, 5, 3), 100, dtype=np.uint8)
        self.baseline = np.full_like(self.source, 102)
        self.masks = {"face": np.ones((4, 5), dtype=bool)}

    def test_identical_response_has_zero_delta(self):
        result = measure_response(self.source, self.baseline, self.baseline, self.masks)
        self.assertEqual(result["regions"]["face"]["deltaSse"], 0)
        self.assertFalse(result["deltaRgb"].flags.writeable)

    def test_source_improvement_has_negative_error_delta(self):
        result = measure_response(self.source, self.baseline, self.source, self.masks)
        self.assertEqual(result["regions"]["face"]["deltaSse"], -240)
        self.assertIsNone(result["regions"]["face"]["candidatePsnrDb"])

    def test_overlapping_changes_expose_cross_terms(self):
        deltas = [np.ones_like(self.source, dtype=float)]*2
        result = response_interference(self.source, self.baseline, deltas, self.masks)["face"]
        self.assertEqual(result["crossTermSse"], 120)
        self.assertEqual(result["unclippedLinearSse"], 960)
        self.assertEqual(result["algebraClosureResidual"], 0)

    def test_disjoint_changes_have_zero_cross_terms(self):
        first = np.zeros_like(self.source, dtype=float)
        first[:2] = 1
        second = np.zeros_like(first)
        second[2:] = 1
        result = response_interference(self.source, self.baseline, [first, second], self.masks)["face"]
        self.assertEqual(result["crossTermSse"], 0)

    def test_proposal_clipping_is_recorded(self):
        delta = np.full_like(self.source, 200, dtype=float)
        predicted = predict_composed_response(self.baseline, [delta])
        self.assertEqual(predicted.max(), 255)
        self.assertFalse(predicted.flags.writeable)
        result = response_interference(self.source, self.baseline, [delta], self.masks)["face"]
        self.assertEqual(result["clippedSamples"], 60)

    def test_invalid_geometry_and_samples_are_rejected(self):
        with self.assertRaises(ValueError):
            measure_response(self.source, self.baseline, self.source[:2], self.masks)
        with self.assertRaises(ValueError):
            predict_composed_response(self.baseline, [np.full(self.source.shape, np.nan)])
        with self.assertRaises(ValueError):
            predict_composed_response(self.baseline, [np.full(self.source.shape, 256)])

    def test_empty_or_nonboolean_masks_are_rejected(self):
        for mask in (np.zeros((4, 5), dtype=bool), np.ones((4, 5), dtype=np.uint8)):
            with self.assertRaises(ValueError):
                measure_response(self.source, self.baseline, self.source, {"face": mask})

    def test_uint8_differences_keep_their_sign(self):
        result = measure_response(self.source, self.baseline, self.source, self.masks)
        self.assertEqual(result["deltaRgb"].min(), -2)

    def test_cancelling_responses_preserve_baseline(self):
        first = np.full(self.source.shape, 5.)
        result = response_interference(self.source, self.baseline, [first, -first], self.masks)["face"]
        self.assertEqual(result["unclippedLinearSse"], result["baselineSse"])
        self.assertEqual(result["crossTermSse"], -3000)

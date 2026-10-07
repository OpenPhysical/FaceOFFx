import unittest

import numpy as np

from edge_quality import NecklineRegion, blur, make_reference, measure


class EdgeQualityTests(unittest.TestCase):
    def setUp(self):
        self.region = NecklineRegion(30, 100, 64, 8)
        y, x = np.mgrid[:128, :128]
        self.skin = (x > 24 + .25 * y) & (x < 104 - .25 * y)
        self.source = np.where(self.skin[..., None], np.array([200., 145., 115.]), np.array([30., 130., 155.]))
        # Stable garment folds provide gradient support independent of candidate pixels.
        self.source += np.where(self.skin, 0, 3 * np.sin(x / 5 + y / 11))[..., None]
        self.reference = make_reference(self.source, self.region)

    def test_exact_source_has_zero_errors(self):
        result = measure(self.reference, self.source.copy())
        for key, value in result.items():
            if "Rms" in key or "Rmse" in key:
                self.assertEqual(value, 0, key)

    def test_shift_increases_position_error(self):
        actual = np.roll(self.source, 2, axis=1)
        result = measure(self.reference, actual)
        self.assertAlmostEqual(result["necklinePositionRmsPixels"], 2, places=3)

    def test_zigzag_increases_jaggedness(self):
        actual = np.array([np.roll(row, 2 if index % 4 < 2 else -2, axis=0) for index, row in enumerate(self.source)])
        self.assertGreater(measure(self.reference, actual)["necklineJaggednessRmsPixels"], 1)

    def test_blur_increases_transition_width_error(self):
        self.assertGreater(measure(self.reference, blur(self.source, 2))["transitionWidthErrorRmsPixels"], 1)

    def test_color_patch_increases_blotch_error(self):
        actual = self.source.copy()
        actual[50:90, 5:20, 2] += 20
        self.assertGreater(measure(self.reference, actual)["clothingChromaBlotchRmseSampleUnits"], 1)

    def test_disappearing_neckline_is_reported(self):
        result = measure(self.reference, np.full_like(self.source, 100))
        self.assertGreater(result["necklineMissingRows"], 0)
        self.assertIsNone(result["necklinePositionRmsPixels"])

    def test_mismatched_candidate_is_rejected(self):
        with self.assertRaises(ValueError):
            measure(self.reference, self.source[:-1])

    def test_reference_regions_do_not_follow_candidate(self):
        before = self.reference["interior"].copy()
        measure(self.reference, blur(self.source, 2))
        np.testing.assert_array_equal(before, self.reference["interior"])

    def test_flat_clothing_has_no_seam_support(self):
        source = np.where(self.skin[..., None], np.array([200., 145., 115.]), np.array([30., 130., 155.]))
        with self.assertRaises(ValueError):
            make_reference(source, self.region)

    def test_missing_one_side_invalidates_jaggedness(self):
        actual = self.source.copy()
        actual[:, :64] = [200., 145., 115.]
        result = measure(self.reference, actual)
        self.assertGreater(result["necklineMissingRows"], 0)
        self.assertIsNone(result["necklineJaggednessRmsPixels"])

    def test_overshoot_increases_halo_measurement(self):
        actual = self.source.copy()
        for index, y in enumerate(self.reference["rows"]):
            x = int(round(self.reference["contours"][0][index]))
            actual[y, x + 2:x + 4] = [245., 210., 180.]
        self.assertGreater(measure(self.reference, actual)["edgeHaloExcessRmsSampleUnits"], 1)

    def test_reference_owns_immutable_source_pixels(self):
        original = self.source.copy()
        self.source[:] = 0
        np.testing.assert_array_equal(self.reference["source"], original)
        self.assertFalse(self.reference["source"].flags.writeable)

    def test_profiles_without_native_support_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "source-pixel support"):
            make_reference(self.source, NecklineRegion(0, 127, 64, 8))

    def test_contrast_loss_increases_edge_contrast_error(self):
        result = measure(self.reference, 128 + .1 * (self.source - 128))
        self.assertGreater(result["edgeContrastRelativeErrorRms"], .8)
        self.assertGreater(result["edgeNormalRgbProfileRmseSampleUnits"], 10)

    def test_nonfinite_candidates_are_rejected(self):
        for value in (float("nan"), float("inf")):
            actual = self.source.copy()
            actual[50, 50, 0] = value
            with self.assertRaises(ValueError):
                measure(self.reference, actual)

    def test_too_few_source_rows_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "13 consecutive"):
            make_reference(self.source, NecklineRegion(30, 32, 64, 8))


if __name__ == "__main__":
    unittest.main()

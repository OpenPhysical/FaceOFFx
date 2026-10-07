import unittest

import numpy as np

from analyze_passes import EDGE_KEYS
from score_balanced import balanced_distances, distance_closure, rank_balanced


class BalancedDistanceTests(unittest.TestCase):
    def test_distance_is_actual_equal_weight_rgb_error(self):
        balanced = np.zeros((1, 2, 3), dtype=np.uint8)
        candidate = balanced.copy()
        candidate[0, 0] = [1, 2, 3]
        masks = dict(face=np.array([[True, False]]), hair=np.array([[False, True]]), wholeImage=np.ones((1, 2), dtype=bool))
        result = balanced_distances(candidate, balanced, masks)
        self.assertEqual(result["face"]["sseRgb"], 14.)
        self.assertEqual(result["wholeImage"]["mseRgb"], 14/6)
        self.assertEqual(result["hair"]["sseRgb"], 0.)

    def test_distance_rejects_shape_or_precision_changes(self):
        pixels = np.zeros((1, 1, 3), dtype=np.uint8)
        for other in (np.zeros((2, 1, 3), dtype=np.uint8), pixels.astype(np.float32)):
            with self.assertRaises(ValueError):
                balanced_distances(pixels, other, {})

    def test_zero_distance_reference_and_finite_bounds(self):
        self.assertEqual(distance_closure(0., 0.), 0.)
        self.assertIsNone(distance_closure(1., 0.))
        self.assertEqual(distance_closure(1., 2.), .5)
        for value in (-1., float("nan"), float("inf")):
            with self.assertRaises(ValueError):
                distance_closure(value, 2.)

    def fixture(self, identifier="candidate", distance=1., face_mse=10., hair_mse=10.):
        metrics = {key: 1. for key in EDGE_KEYS}
        metrics.update(necklineMissingRows=0, transitionMissingSamples=0)
        quality = dict(face=dict(mseRgb=face_mse, psnrRgbDb=30., exactPixelEquality=False),
                       hair=dict(mseRgb=hair_mse, psnrRgbDb=30., exactPixelEquality=False))
        return dict(id=identifier, bytes=100, meetsByteAndRegionalGates=True, metrics=metrics,
                    quality=quality, balancedDistance=dict(wholeImage=dict(mseRgb=distance)))

    def test_balanced_distance_orders_after_separate_source_guards(self):
        baseline = self.fixture(distance=10.)
        baseline["metrics"] = {key: 2. for key in EDGE_KEYS} | dict(necklineMissingRows=0, transitionMissingSamples=0)
        best, other = self.fixture("best", 1.), self.fixture("other", 2.)
        rankings = rank_balanced([other, best], baseline)
        self.assertEqual(rankings["strictSourceFaceHairContourRanking"], ["best", "other"])

    def test_distance_gain_cannot_hide_hair_loss_or_missed_floor(self):
        baseline = self.fixture(distance=10.)
        baseline["metrics"] = {key: 2. for key in EDGE_KEYS} | dict(necklineMissingRows=0, transitionMissingSamples=0)
        hair_loss = self.fixture("hair", .1, hair_mse=11.)
        below_floor = self.fixture("floor", .2)
        below_floor["meetsByteAndRegionalGates"] = False
        ranking = rank_balanced([hair_loss, below_floor], baseline)
        self.assertEqual(ranking["strictSourceFaceHairContourRanking"], [])
        self.assertEqual(ranking["feasibleDistanceRanking"], ["hair"])

    def test_nonfinite_balanced_distance_is_rejected(self):
        with self.assertRaises(ValueError):
            rank_balanced([self.fixture(distance=float("nan"))], self.fixture())

    def test_small_face_loss_has_its_own_research_ranking(self):
        baseline = self.fixture(distance=10.)
        baseline["metrics"] = {key: 2. for key in EDGE_KEYS} | dict(necklineMissingRows=0, transitionMissingSamples=0)
        candidate = self.fixture()
        candidate["quality"]["face"]["psnrRgbDb"] -= .05
        ranking = rank_balanced([candidate], baseline)
        self.assertEqual(ranking["strictSourceFaceHairContourRanking"], [])
        self.assertEqual(ranking["conservativePoint10DbSourceRanking"], ["candidate"])


if __name__ == "__main__":
    unittest.main()

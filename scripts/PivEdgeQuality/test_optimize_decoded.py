import unittest

import numpy as np

from optimize_decoded import baseline_guards, donor_frontier, endpoint_totals, error_deltas, normalize_changes, prefix_value, ready_guards, source_errors


def block():
    return dict(terminalPass=2, legalPasses=[0, 2], bytesByPass=[3, 7, 12],
                faceCreditByPass=[1., 2., 8.])


def donor(endpoint, freed, credit, face, hair=0.):
    return dict(endpoint=endpoint, freedBodyBytes=freed, faceCreditLost=credit,
                sourceErrorSseDeltas=dict(face=face, hair=hair))


class DecodedOptimizerTests(unittest.TestCase):
    def test_zero_prefix_and_native_endpoints(self):
        self.assertEqual(prefix_value(block(), -1, "bytesByPass"), 0)
        self.assertEqual(prefix_value(block(), 2, "bytesByPass"), 12)
        for endpoint in (1, -2, 3, True):
            with self.assertRaises(Exception):
                prefix_value(block(), endpoint, "bytesByPass")

    def test_untouched_blocks_remain_in_complete_totals(self):
        blocks = dict(a=block(), b=block())
        self.assertEqual(endpoint_totals(blocks, dict(a=0)), (15, 9.))
        self.assertEqual(endpoint_totals(blocks, dict(a=-1)), (12, 8.))

    def test_noop_changes_are_removed_and_unknown_ids_rejected(self):
        self.assertEqual(normalize_changes(dict(a=block()), dict(a=2)), {})
        with self.assertRaises(Exception):
            normalize_changes(dict(a=block()), dict(b=0))

    def test_options_for_one_block_are_exclusive(self):
        states = donor_frontier(dict(a=[donor(0, 3, 1, 2), donor(-1, 5, 2, 4)]), 8, 3)
        self.assertEqual({state.freed for state in states}, {0, 3, 5})
        self.assertTrue(all(len(state.changes) <= 1 for state in states))

    def test_body_and_credit_limits_are_enforced(self):
        groups = dict(a=[donor(0, 3, 2, 4)], b=[donor(0, 4, 3, 5)])
        states = donor_frontier(groups, 6, 4)
        self.assertEqual({state.freed for state in states}, {0, 3, 4})
        self.assertTrue(all(state.credit_loss <= 4 for state in states))

    def test_hair_and_credit_tradeoffs_survive_frontier(self):
        groups = dict(a=[donor(0, 3, 2, 1, 5), donor(-1, 3, 1, 2, 0)])
        states = [state for state in donor_frontier(groups, 3, 2) if state.freed == 3]
        self.assertEqual(len(states), 2)

    def test_negative_measured_error_is_allowed(self):
        states = donor_frontier(dict(a=[donor(0, 3, 1, -5)]), 3, 1)
        self.assertEqual(states[0].face_delta, -5)

    def test_beam_bound_is_explicit(self):
        groups = dict(a=[donor(i, 3, i, 10-i, i) for i in range(5)])
        states = donor_frontier(groups, 3, 4, states_per_body=2)
        self.assertLessEqual(sum(state.freed == 3 for state in states), 2)

    def test_invalid_resources_and_nonfinite_measurements_rejected(self):
        for option in (donor(0, 0, 1, 2), donor(0, 3, -1, 2), donor(0, 3, 1, float("nan"))):
            with self.assertRaises(Exception):
                donor_frontier(dict(a=[option]), 3, 2)
        with self.assertRaises(Exception):
            donor_frontier({}, 3, 2, states_per_body=0)

    def test_actual_face_error_ranks_ahead_of_credit_only_tie_break(self):
        groups = dict(first=[donor(0, 10, 8., 1.)], second=[donor(1, 10, 1., 50.)])
        states = [state for state in donor_frontier(groups, 10, 8.) if state.freed >= 10]
        self.assertEqual(states[0].changes, (("first", 0),))
        self.assertEqual(states[0].face_delta, 1.)
        self.assertTrue(any(state.credit_loss == 1. for state in states))

    def test_credit_resource_bound_remains_separate_from_face_quality(self):
        groups = dict(first=[donor(0, 10, 8., 1.)], second=[donor(1, 10, 1., 50.)])
        states = [state for state in donor_frontier(groups, 10, 1.) if state.freed >= 10]
        self.assertEqual(states[0].changes, (("second", 1),))

    def test_exact_source_sse_has_distinct_face_and_hair_denominators(self):
        source = np.full((1, 2, 3), 100, dtype=np.uint8)
        candidate = source.copy()
        candidate[0, 0] += 2
        masks = dict(face=np.array([[True, False]]), hair=np.array([[False, True]]))
        baseline = source_errors(source, source, masks)
        actual = source_errors(source, candidate, masks)
        self.assertEqual(actual["face"]["sseRgb"], 12.)
        self.assertEqual(actual["face"]["mseRgb"], 4.)
        self.assertTrue(actual["hair"]["exactPixelEquality"])
        self.assertEqual(error_deltas(actual, baseline), {"face": 12., "hair": 0.})

    def test_baseline_requires_actual_cap_and_face_floor(self):
        baseline_guards(bytes(10), dict(attributedFacePayloadBytes=5), 10, 5)
        for data, face in ((bytes(11), 5), (bytes(10), 4)):
            with self.assertRaisesRegex(ValueError, "actual JP2 cap"):
                baseline_guards(data, dict(attributedFacePayloadBytes=face), 10, 5)

    def test_ready_requires_both_exact_replays_and_single_tier1_capture(self):
        ready = dict(originalFloorReplayByteExact=True, originalBalancedReplayByteExact=True, tier1EncodedOnce=True)
        ready_guards(ready)
        for key in ready:
            broken = dict(ready, **{key: False})
            with self.assertRaisesRegex(ValueError, "frozen replay"):
                ready_guards(broken)


if __name__ == "__main__":
    unittest.main()

import copy
import json
import tempfile
import unittest
from pathlib import Path

import numpy as np

from verify_gallery import METHOD, boxes, checked_bytes, decode, digest, gallery_markup, inspect, verify_ledger

ROOT = Path(__file__).resolve().parents[2]


class GalleryVerificationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.data = (ROOT / "docs/samples/piv/medical_minimum.jp2").read_bytes()
        cls.structure = inspect(cls.data, (480, 640))

    def test_all_sources_have_pinned_hashes_and_supplied_watermarks(self):
        rows = json.loads((ROOT / "tests/readme-sources.json").read_bytes())
        self.assertEqual(len(rows), 8)
        for row in rows:
            self.assertEqual(digest((ROOT / row["path"]).read_bytes()), row["expectedSha256"])
        for row in rows:
            if row["id"] in ("construction", "medical"):
                self.assertIn("source_watermarked/", row["path"])

    def test_native_decode_is_rgb_and_unscaled(self):
        pixels = decode(self.data, (480, 640))
        self.assertEqual(pixels.shape, (640, 480, 3))
        self.assertEqual(pixels.dtype, np.uint8)
        np.testing.assert_array_equal(pixels, decode(self.data, (480, 640)))

    def test_captured_bytes_hash_mismatch_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "source"
            path.write_bytes(b"fixed")
            self.assertEqual(checked_bytes(path, digest(b"fixed")), b"fixed")
            with self.assertRaises(AssertionError):
                checked_bytes(path, digest(b"changed"))

    def test_wrong_rsiz_fails(self):
        changed = bytearray(self.data)
        offset = changed.index(b"\xffQ")
        changed[offset + 4:offset + 6] = b"\x00\x01"
        with self.assertRaises(AssertionError):
            inspect(bytes(changed), (480, 640))

    def test_wrong_dimensions_fail(self):
        with self.assertRaises(AssertionError):
            inspect(self.data, (432, 576))

    def test_wrong_progression_fails(self):
        changed = bytearray(self.data)
        offset = changed.index(b"\xffR")
        changed[offset + 5] = 1
        with self.assertRaises(AssertionError):
            inspect(bytes(changed), (480, 640))

    def test_truncated_box_fails(self):
        with self.assertRaises(AssertionError):
            list(boxes(self.data[:-1]))

    def ledger_fixture(self):
        row = {"Tile": 0, "Component": 0, "ResolutionLevel": 0, "Subband": "LL", "PayloadBytes": 10}
        telemetry = {"TotalOutputBytes": 100, "PacketBodyBytes": 10, "PacketHeaderBytes": 3, "Subbands": [row]}
        codec = {"PayloadTelemetry": telemetry, "CodestreamBytes": 80, "ContainerBytes": 20}
        ledger = {"MethodId": METHOD, "PacketBodyBytes": 10, "FacePayloadByteEstimate": 3.5,
                  "AttributedFacePayloadBytes": 3, "OutsidePayloadBytes": 7,
                  "Subbands": [dict(row, FacePayloadByteEstimate=3.5)]}
        structure = {"CodestreamBytes": 80, "ContainerBytes": 20, "PacketHeaderAndBodyBytes": 13}
        return codec, {"Attribution": ledger}, structure

    def test_ledger_conserves_bytes_without_a_floor(self):
        codec, regional, structure = self.ledger_fixture()
        verify_ledger(codec, regional, structure, 100)

    def test_ledger_rejects_each_mismatched_band(self):
        codec, regional, structure = self.ledger_fixture()
        regional["Attribution"]["Subbands"][0]["Component"] = 1
        with self.assertRaises(AssertionError):
            verify_ledger(codec, regional, structure, 100)

    def test_ledger_rejects_nonfinite_credit_and_requested_floor(self):
        codec, regional, structure = self.ledger_fixture()
        for value in (float("nan"), float("inf"), -1):
            changed = copy.deepcopy(regional)
            changed["Attribution"]["FacePayloadByteEstimate"] = value
            with self.assertRaises(AssertionError):
                verify_ledger(codec, changed, structure, 100)
        codec["RequestedMinimumAttributedFacePayloadBytes"] = 1
        with self.assertRaises(AssertionError):
            verify_ledger(codec, regional, structure, 100)

    def test_gallery_retains_both_target_decisions(self):
        common = {"Id": "example", "Label": "Example", "SourcePreview": "source.png", "Status": "ReviewRequired"}
        markup = gallery_markup([dict(common, SizeProfile="Minimum"), dict(common, SizeProfile="Preferred")])
        self.assertIn("| Source | Minimum | Preferred |", markup)
        self.assertIn("example_minimum.json", markup)
        self.assertIn("example_preferred.json", markup)


if __name__ == "__main__":
    unittest.main()

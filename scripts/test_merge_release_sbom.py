import contextlib
import io
import json
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

from merge_release_sbom import merge, sha256


class ReleaseSbomTests(unittest.TestCase):
    def fixture(self):
        version = ET.parse("Directory.Build.props").findtext("./PropertyGroup/Version")
        names = ("CoreJ2K.FaceOFFx", "FaceOFFx.Core", "FaceOFFx.Infrastructure", "FaceOFFx.Models")
        return {"bomFormat": "CycloneDX", "specVersion": "1.6",
                "metadata": {"component": {"name": "FaceOFFx", "version": version, "bom-ref": "FaceOFFx@" + version}},
                "components": [{"name": name, "version": version, "bom-ref": name + "@" + version, "type": "library"} for name in names],
                "dependencies": [{"ref": name + "@" + version, "dependsOn": []} for name in names]}

    def test_merge_preserves_raw_graph_and_adds_verified_models_and_notices(self):
        with tempfile.TemporaryDirectory() as directory:
            raw, output = Path(directory) / "raw.json", Path(directory) / "combined.json"
            source = json.dumps(self.fixture()).encode()
            raw.write_bytes(source)
            with contextlib.redirect_stdout(io.StringIO()):
                merge(raw, output)
            self.assertEqual(raw.read_bytes(), source)
            data = json.loads(output.read_bytes())
            self.assertEqual(len(data["components"]), 6)
            codec = next(row for row in data["components"] if row["name"] == "CoreJ2K.FaceOFFx")
            self.assertEqual(codec["licenses"][0]["license"]["id"], "BSD-3-Clause")
            self.assertEqual(codec["licenses"][1]["license"]["name"], "JJ2000 COPYRIGHT 5.1")
            self.assertEqual(next(p["value"] for p in data["properties"] if p["name"] == "faceoffx:raw-cyclonedx-sha256"), sha256(source))
            self.assertEqual(len([r for r in data["components"] if r["type"] == "machine-learning-model"]), 2)

    def test_stale_project_version_is_rejected(self):
        data = self.fixture()
        data["components"][0]["version"] = "1.0.0"
        with tempfile.TemporaryDirectory() as directory:
            raw, output = Path(directory) / "raw.json", Path(directory) / "combined.json"
            raw.write_text(json.dumps(data))
            with self.assertRaises(AssertionError):
                merge(raw, output)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()

import tempfile
import unittest
from pathlib import Path
import zipfile
from unittest.mock import patch

from verify_package import ASSEMBLIES, verify


class PackageVerificationTests(unittest.TestCase):
    def package(self, folder, extra=None, exclude=None, version="4.0.0", dependency=None):
        content = {f"lib/net8.0/{name}.{extension}": b"fixture" for name in ASSEMBLIES for extension in ("dll", "xml")}
        content.update({"licenses/CoreJ2K.FaceOFFx/"+name: b"fixture" for name in ("LICENSE", "COPYRIGHT-JJ2000-5.1", "provenance.json")})
        content["README.md"] = b"Package fixture"
        deps = f'<dependencies><dependency id="{dependency}" version="1" /></dependencies>' if dependency else ""
        content["FaceOFFx.nuspec"] = f'<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata><id>FaceOFFx</id><version>{version}</version>{deps}</metadata></package>'.encode()
        content.update(extra or {})
        if exclude:
            del content[exclude]
        path = Path(folder)/"fixture.nupkg"
        with zipfile.ZipFile(path, "w") as archive:
            for name, data in content.items():
                archive.writestr(name, data)
        return path

    def test_complete_single_bundle(self):
        with tempfile.TemporaryDirectory() as folder:
            self.assertGreater(verify(self.package(folder), "4.0.0"), 0)

    def test_missing_runtime_assembly_rejected(self):
        with tempfile.TemporaryDirectory() as folder, self.assertRaises(ValueError):
            verify(self.package(folder, exclude="lib/net8.0/CoreJ2K.FaceOFFx.dll"), "4.0.0")

    def test_duplicate_model_framework_rejected(self):
        with tempfile.TemporaryDirectory() as folder, self.assertRaises(ValueError):
            verify(self.package(folder, extra={"lib/net9.0/FaceOFFx.Models.dll": b"fixture"}), "4.0.0")

    def test_separate_codec_dependency_rejected(self):
        with tempfile.TemporaryDirectory() as folder, self.assertRaises(ValueError):
            verify(self.package(folder, dependency="CoreJ2K.ImageSharp"), "4.0.0")

    def test_version_mismatch_rejected(self):
        with tempfile.TemporaryDirectory() as folder, self.assertRaises(ValueError):
            verify(self.package(folder, version="3.0.0"), "4.0.0")

    def test_license_required(self):
        with tempfile.TemporaryDirectory() as folder, self.assertRaises(ValueError):
            verify(self.package(folder, exclude="licenses/CoreJ2K.FaceOFFx/COPYRIGHT-JJ2000-5.1"), "4.0.0")

    def test_dependency_identity_is_case_insensitive(self):
        with tempfile.TemporaryDirectory() as folder, self.assertRaises(ValueError):
            verify(self.package(folder, dependency="corej2k.imagesharp"), "4.0.0")

    def test_size_allowance_is_enforced(self):
        with tempfile.TemporaryDirectory() as folder:
            package = self.package(folder)
            with patch("verify_package.MAXIMUM_BYTES", package.stat().st_size), self.assertRaises(ValueError):
                verify(package, "4.0.0")

    def test_relative_gallery_image_rejected(self):
        with tempfile.TemporaryDirectory() as folder, self.assertRaises(ValueError):
            verify(self.package(folder, extra={"README.md": b'<img src="docs/samples/piv/medical.png">'}), "4.0.0")


if __name__ == "__main__":
    unittest.main()

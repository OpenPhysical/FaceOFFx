#!/usr/bin/env python3
"""Check the single FaceOFFx package before handing it to release tooling."""
import argparse
import re
from pathlib import Path
import xml.etree.ElementTree as ET
import zipfile


ASSEMBLIES = {"FaceOFFx", "FaceOFFx.Core", "FaceOFFx.Infrastructure", "FaceOFFx.Models", "CoreJ2K.FaceOFFx"}
MAXIMUM_BYTES = 250_000_000


def verify(path, version):
    path = Path(path)
    if path.stat().st_size >= MAXIMUM_BYTES:
        raise ValueError("FaceOFFx package exceeds the release size allowance.")
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError("Package contains duplicate archive entries.")
        nuspecs = [name for name in names if name.endswith(".nuspec")]
        if len(nuspecs) != 1:
            raise ValueError("Package must contain one nuspec.")
        root = ET.fromstring(archive.read(nuspecs[0]))
        namespace = root.tag.partition("}")[0]+"}" if root.tag.startswith("{") else ""
        metadata = root.find(namespace+"metadata")
        if metadata is None or metadata.findtext(namespace+"id") != "FaceOFFx" or metadata.findtext(namespace+"version") != version:
            raise ValueError("Package identity/version differs from the release.")
        dependencies = {node.get("id", "") for node in metadata.iter(namespace+"dependency")}
        if any(name.casefold().startswith(("corej2k", "faceoffx")) for name in dependencies):
            raise ValueError("Bundled implementation must resolve within FaceOFFx.")
        for assembly in ASSEMBLIES:
            for extension in ("dll", "xml"):
                expected = f"lib/net8.0/{assembly}.{extension}"
                if expected not in names:
                    raise ValueError(f"Missing bundled assembly/documentation: {expected}")
        if any(name.startswith("lib/") and not name.startswith("lib/net8.0/") for name in names):
            raise ValueError("Ship one complete compatible framework group.")
        model_entries = [name for name in names if name.endswith("FaceOFFx.Models.dll")]
        if len(model_entries) != 1:
            raise ValueError("Embedded models must appear exactly once.")
        allowed_dlls = {f"lib/net8.0/{name}.dll" for name in ASSEMBLIES}
        if any(name.endswith(".dll") and name not in allowed_dlls for name in names):
            raise ValueError("Unexpected implementation or development assembly in package.")
        readme = archive.read("README.md").decode("utf-8")
        if re.search(r'''(?:src|href)=["']docs/samples/|\]\(docs/samples/''', readme):
            raise ValueError("NuGet gallery links require absolute release URLs.")
        if "raw.githubusercontent.com/mistial-dev/FaceOFFx/" in readme and f"/v{version}/docs/samples/" not in readme:
            raise ValueError("NuGet gallery version differs from the package.")
        for notice in ("LICENSE", "COPYRIGHT-JJ2000-5.1", "provenance.json"):
            expected = "licenses/CoreJ2K.FaceOFFx/"+notice
            if expected not in names or not archive.read(expected):
                raise ValueError(f"Missing codec provenance/notice: {notice}")
    return path.stat().st_size


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    packages = list(args.directory.glob("*.nupkg"))
    expected = args.directory/f"FaceOFFx.{args.version}.nupkg"
    if packages != [expected]:
        raise ValueError("Use a fresh output directory containing only the exact FaceOFFx package.")
    size = verify(expected, args.version)
    print(f"Verified FaceOFFx {args.version}: {size:,} bytes; one embedded model assembly.")


if __name__ == "__main__":
    main()

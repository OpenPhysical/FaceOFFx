#!/usr/bin/env python3
"""Render development diagnostic previews from exact hash-checked JP2 bytes."""
import json
import sys
from pathlib import Path
from PIL import Image, features
from verify_gallery import checked_bytes, decode, digest, inspect


def render(output):
    path = output / "manifest.json"
    manifest = json.loads(path.read_bytes())
    for row in manifest["Assets"]:
        checked_bytes(row["InputPath"], row["SourceSha256"])
        data = checked_bytes(row["EncodedPath"], row["EncodedSha256"])
        assert len(data) == row["Bytes"]
        dimensions = (row["Width"], row["Height"])
        structure = inspect(data, dimensions)
        pixels = decode(data, dimensions)
        Image.fromarray(pixels).save(row["OutputPath"])
        row["PreviewStatus"] = "IndependentlyDecoded"
        row["PreviewDecoder"] = "Pillow/OpenJPEG " + str(features.version("jpg_2000"))
        row["PreviewSha256"] = digest(Path(row["OutputPath"]).read_bytes())
        row["IndependentStructure"] = structure
    path.write_text(json.dumps(manifest, indent=2) + "\n")


if __name__ == "__main__":
    render(Path(sys.argv[1]))

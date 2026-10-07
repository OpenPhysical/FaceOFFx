#!/usr/bin/env python3
"""Augment a raw CycloneDX project graph with verified vendor notices and embedded models."""
import argparse
import base64
import hashlib
import json
import xml.etree.ElementTree as ET
from pathlib import Path


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def merge(raw_path, output_path):
    raw = raw_path.read_bytes()
    bom = json.loads(raw)
    version = ET.parse("Directory.Build.props").findtext("./PropertyGroup/Version")
    assert bom["bomFormat"] == "CycloneDX" and bom["metadata"]["component"]["version"] == version
    components = {row["name"]: row for row in bom["components"]}
    for name in ("CoreJ2K.FaceOFFx", "FaceOFFx.Core", "FaceOFFx.Infrastructure", "FaceOFFx.Models"):
        assert components[name]["version"] == version, f"Regenerate the current project graph for {name}."
    vendor = json.loads(Path("sbom/vendored-codec.json").read_bytes())
    provenance_bytes = Path(vendor["sourceProvenance"]).read_bytes()
    assert sha256(provenance_bytes) == vendor["sourceProvenanceSha256"]
    provenance = json.loads(provenance_bytes)
    for relative, expected in provenance["finalVendoredFiles"].items():
        assert sha256((Path(vendor["sourceDirectory"]) / relative).read_bytes()) == expected
    codec = components["CoreJ2K.FaceOFFx"]
    codec["licenses"] = []
    for notice in vendor["licenses"]:
        content = Path(notice["path"]).read_bytes()
        assert sha256(content) == notice["sha256"]
        identity = {"id": notice["id"]} if notice["id"] == "BSD-3-Clause" else {"name": "JJ2000 COPYRIGHT 5.1"}
        codec["licenses"].append({"license": dict(identity, text={
            "contentType": "text/plain", "encoding": "base64", "content": base64.b64encode(content).decode("ascii")})})
    codec["properties"] = [
        {"name": "faceoffx:source-path", "value": vendor["sourceDirectory"]},
        {"name": "faceoffx:source-provenance-sha256", "value": sha256(provenance_bytes)},
        {"name": "faceoffx:upstream-head", "value": vendor["upstreamHead"]},
        {"name": "faceoffx:source-basis", "value": vendor["sourceBasis"]},
        {"name": "faceoffx:source-file-count", "value": str(len(provenance["finalVendoredFiles"]))}
    ]
    codec["externalReferences"] = [{"type": "vcs", "url": vendor["upstreamRepository"]}]
    dependencies = {row["ref"]: row for row in bom["dependencies"]}
    model_parent = dependencies[components["FaceOFFx.Models"]["bom-ref"]]
    models = (
        ("FaceDetector.onnx", "RetinaFace face-detection model", "40f825cf7dd0a88b26fb61db9a3aaedc2cad35162091113f4017b3c26a4f792d"),
        ("landmarks_68_pfld.onnx", "PFLD 68-point landmark model", "7d7bbd5c6a1d9272e58d9773898284a1905d872eba9a662df9b5f20f1ba6f83e")
    )
    for filename, name, expected in models:
        path = Path("src/FaceOFFx.Models/Resources") / filename
        data = path.read_bytes()
        assert sha256(data) == expected
        reference = "onnx-model/" + filename
        assert all(row["bom-ref"] != reference for row in bom["components"])
        bom["components"].append({
            "type": "machine-learning-model", "bom-ref": reference, "name": name, "scope": "required",
            "hashes": [{"alg": "SHA-256", "content": expected}],
            "properties": [
                {"name": "faceoffx:embedded-resource", "value": "FaceOFFx.Models.Resources." + filename},
                {"name": "faceoffx:source-path", "value": path.as_posix()},
                {"name": "faceoffx:file-size-bytes", "value": str(len(data))},
                {"name": "faceoffx:distribution", "value": "Embedded in FaceOFFx.Models.dll"},
                {"name": "faceoffx:review-requirement", "value": "Retain upstream model provenance and verify weight redistribution terms for release."}
            ]})
        model_parent.setdefault("dependsOn", []).append(reference)
        bom["dependencies"].append({"ref": reference, "dependsOn": []})
    bom.setdefault("properties", []).extend([
        {"name": "faceoffx:raw-cyclonedx-sha256", "value": sha256(raw)},
        {"name": "faceoffx:augmentation-script-sha256", "value": sha256(Path(__file__).read_bytes())},
        {"name": "faceoffx:augmentation-scope", "value": "Verified vendor license/source provenance and embedded model identity; raw dependency graph retained."}
    ])
    known = {row["bom-ref"] for row in bom["components"]} | {bom["metadata"]["component"]["bom-ref"]}
    assert len(known) == len(bom["components"]) + 1
    assert all(row["ref"] in known and all(ref in known for ref in row.get("dependsOn", [])) for row in bom["dependencies"])
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(bom, indent=2) + "\n")
    print(json.dumps({"output": output_path.as_posix(), "components": len(bom["components"]),
                      "rawSha256": sha256(raw), "combinedSha256": sha256(output_path.read_bytes())}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("raw", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    merge(args.raw, args.output)

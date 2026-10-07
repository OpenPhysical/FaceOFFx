"""Score frozen medical controls and constrained encodes using reviewed source regions."""
import hashlib
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, features

from edge_quality import make_reference, measure

root = Path(__file__).resolve().parents[2]
study = root / "artifacts/piv-encoding-study/2026-10-06"
output = root / "artifacts/piv-encoding-current/edge-aware"
output.mkdir(parents=True, exist_ok=True)
source_path = study / "cardholder-medical-watermarked-landmark225/crop.png"
source = np.array(Image.open(source_path).convert("RGB"), dtype=np.float64)
reference = make_reference(source)
rows = []
control = json.loads((root / "artifacts/piv-encoding-current/landmark-v3/medical.json").read_text())["results"][0]
configurations = json.loads((root / "artifacts/piv-encoding-current/frequency-outfit/results.json").read_text())
inputs = [("balanced-visual-reference", control["jp2Path"], control["apiResult"], None)]
for configuration in configurations:
    if configuration["category"] != "medical" or configuration["status"] != "encoded":
        continue
    benchmark = json.loads(Path(configuration["benchmarkPath"]).read_text())
    inputs.append((configuration["name"], configuration["jp2Path"], benchmark["cases"][0]["result"], configuration))
for name, path, codec, configuration in inputs:
    with Image.open(path) as decoded:
        candidate = np.array(decoded.convert("RGB"), dtype=np.float64)
    attribution = codec["sharedPayloadAttribution"]
    assert attribution["methodId"] == "synthesis-energy-decoder-effective-pass-v3"
    assert attribution["attributedFacePayloadBytes"] + attribution["outsidePayloadBytes"] == attribution["packetBodyBytes"]
    assert len(Path(path).read_bytes()) == codec["completeOutputBytes"] <= 11820
    row = dict(name=name, jp2Path=path, jp2Sha256=hashlib.sha256(Path(path).read_bytes()).hexdigest(),
               bytes=codec["completeOutputBytes"], faceRatio=codec["attributedRegionalRatio"],
               meetsByteAndRegionalGates=attribution["attributedFacePayloadBytes"] >= 7540,
               metrics=measure(reference, candidate), configuration=configuration,
               packetBodyBytes=attribution["packetBodyBytes"], faceBytes=attribution["attributedFacePayloadBytes"],
               outsideBytes=attribution["outsidePayloadBytes"])
    rows.append(row)
    print(name, "position", round(row["metrics"]["necklinePositionRmsPixels"], 3),
          "jagged", round(row["metrics"]["necklineJaggednessRmsPixels"], 3),
          "seams", round(row["metrics"]["seamGradientRmseSampleUnitsPerPixel"], 3), flush=True)

baseline = next(row for row in rows if row["name"] == "baseline")
keys = ("necklinePositionRmsPixels", "necklineJaggednessRmsPixels", "transitionWidthErrorRmsPixels",
        "edgeHaloExcessRmsSampleUnits", "edgeContrastRelativeErrorRms", "edgeNormalRgbProfileRmseSampleUnits",
        "seamGradientRmseSampleUnitsPerPixel", "clothingChromaBlotchRmseSampleUnits")
for row in rows:
    row["metricRatiosToRegionalBaseline"] = {key: row["metrics"][key] / baseline["metrics"][key]
                                            if baseline["metrics"][key] and row["metrics"][key] is not None else None for key in keys}
    # Reporting remains multi-objective; a single average can hide a damaged neckline.
    row["edgePositionAndJaggednessImproved"] = (row["metrics"]["necklineMissingRows"] == 0
        and row["metrics"]["necklinePositionRmsPixels"] < baseline["metrics"]["necklinePositionRmsPixels"]
        and row["metrics"]["necklineJaggednessRmsPixels"] < baseline["metrics"]["necklineJaggednessRmsPixels"])
eligible = [row for row in rows if row["meetsByteAndRegionalGates"] and row["metrics"]["necklineMissingRows"] == 0
            and row["metrics"]["transitionMissingSamples"] == 0
            and all(row["metrics"][key] is not None for key in keys)]
for row in eligible:
    row["paretoDominated"] = any(all(other["metrics"][key] <= row["metrics"][key] for key in keys)
                                and any(other["metrics"][key] < row["metrics"][key] for key in keys)
                                for other in eligible if other is not row)
overlay = source.copy()
overlay[reference["interior"]] = overlay[reference["interior"]] * .7 + np.array([40, 100, 255]) * .3
overlay[reference["seams"]] = [255, 30, 190]
image = Image.fromarray(np.uint8(np.clip(overlay, 0, 255)))
draw = ImageDraw.Draw(image)
for contour in reference["contours"]:
    draw.line(list(zip(contour, reference["rows"])), fill=(20, 255, 80), width=2)
image.save(output / "medical-reference-regions.png")
reference_evidence = dict(sourcePath=str(source_path), sourceSha256=hashlib.sha256(source_path.read_bytes()).hexdigest(),
                         region=reference["region"].__dict__,
                         rows=reference["rows"].tolist(), contours=[values.tolist() for values in reference["contours"]],
                         interiorMaskSha256=hashlib.sha256(reference["interior"].tobytes()).hexdigest(),
                         seamMaskSha256=hashlib.sha256(reference["seams"].tobytes()).hexdigest(),
                         scope="Frozen teal medical garment; red-green color-boundary preset, source-only contours and garment support.",
                         seamScope="Top-decile source luma gradients in eroded clothing, at least 0.5 sample units/pixel. Support includes folds and stitching.",
                         printQualification="Native pixel measurements. Actual intended-size printing and human review remain separate.")
for name in ("interior", "seams", "clothing"):
    Image.fromarray(reference[name].astype(np.uint8) * 255).save(output / f"medical-{name}-mask.png")
(output / "medical-reference.json").write_text(json.dumps(reference_evidence, indent=2) + "\n")
report = dict(schemaVersion=1, decoder="Pillow/OpenJPEG " + str(features.version("jpg_2000")),
              sourceReference=reference_evidence,
              metricDirections="Lower error is better. Metrics remain separate and Pareto-ranked; color averages do not override contour regressions.",
              methodId="source-neckline-clothing-v1", measurementMethod="synthesis-energy-decoder-effective-pass-v3",
              imageByteCap=11820, fixedFacePixelCount=60318, minimumAttributedFacePayloadBytes=7540,
              candidates=rows)
(output / "medical-edge-results.json").write_text(json.dumps(report, indent=2, allow_nan=False) + "\n")
print(output / "medical-edge-results.json")

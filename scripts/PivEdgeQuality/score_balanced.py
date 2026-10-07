#!/usr/bin/env python3
"""Rank frozen-prefix candidates by decoded RGB distance to the balanced reference."""
import argparse
import io
import json
import math
from pathlib import Path

import numpy as np
from PIL import Image

from analyze_passes import checked_bytes, digest, finite_metrics, gate_candidate, panel, print_geometry, relative, require
from optimize_decoded import endpoint_totals, source_errors
from edge_quality import make_reference
from score_allocator import checked_manifest, endpoint_map, load_masks, score


def captured_pixels(path, sha256):
    data = checked_bytes(path, sha256)
    with Image.open(io.BytesIO(data)) as image:
        image.load()
        require(image.mode == "RGB" and image.size == (480, 640), "Frozen RGB geometry changed")
        return np.array(image)


def balanced_distances(candidate, balanced, masks):
    """Use equal RGB sample weights; keep each spatial region's distance visible."""
    require(candidate.shape == balanced.shape and candidate.ndim == 3 and candidate.shape[2] == 3,
            "Balanced-distance inputs must share RGB geometry")
    require(candidate.dtype == balanced.dtype == np.uint8, "Use independently decoded RGB8 samples")
    return source_errors(balanced, candidate, masks)


def distance_closure(candidate_mse, baseline_mse):
    require(all(math.isfinite(value) and value >= 0 for value in (candidate_mse, baseline_mse)),
            "Use finite nonnegative decoded distances")
    return (baseline_mse-candidate_mse)/baseline_mse if baseline_mse else 0. if candidate_mse == 0 else None


def rank_balanced(rows, baseline, maximum_other_regression=.10):
    require(math.isfinite(maximum_other_regression) and maximum_other_regression >= 0,
            "Use a finite nonnegative separate error tolerance")
    feasible, strict, conservative = [], [], []
    for row in rows:
        require(math.isfinite(row["balancedDistance"]["wholeImage"]["mseRgb"]), "Finite balanced distance required")
        row["strictSourceGate"] = gate_candidate(row, baseline, 0., maximum_other_regression)
        row["conservativeSourceGate"] = gate_candidate(row, baseline, .10, maximum_other_regression)
        row["hairSourceErrorNonloss"] = row["quality"]["hair"]["mseRgb"] <= baseline["quality"]["hair"]["mseRgb"]
        row["balancedDistanceImproved"] = row["balancedDistance"]["wholeImage"]["mseRgb"] < baseline["balancedDistance"]["wholeImage"]["mseRgb"]
        if row["meetsByteAndRegionalGates"] and finite_metrics(row["metrics"]):
            feasible.append(row)
            if row["hairSourceErrorNonloss"] and row["balancedDistanceImproved"]:
                if row["strictSourceGate"]["conservativeRepairEligible"]:
                    strict.append(row)
                if row["conservativeSourceGate"]["conservativeRepairEligible"]:
                    conservative.append(row)
    order = lambda values: [row["id"] for row in sorted(values, key=lambda row: (
        row["balancedDistance"]["wholeImage"]["mseRgb"], row["quality"]["face"]["mseRgb"], row["bytes"], row["id"]))]
    return dict(feasibleDistanceRanking=order(feasible), strictSourceFaceHairContourRanking=order(strict),
                conservativePoint10DbSourceRanking=order(conservative),
                objective="Minimum full-image decoded RGB MSE to the user-preferred balanced reference",
                sourceGuards="Separate source-RGB face tolerance (0 or 0.10 dB), hair nonloss, complete contours, both contour errors improved, six other errors individually within 10%")


def reconcile_complete_map(trial, row, baseline_blocks):
    blocks, data = endpoint_map(trial["blockMapPath"], trial["blockMapSha256"])
    require(set(blocks) == set(baseline_blocks), "Complete frozen block identities changed")
    for identifier, block in blocks.items():
        for field in ("legalPasses", "bytesByPass", "faceCreditByPass"):
            require(block[field] == baseline_blocks[identifier][field], "Frozen prefix tables changed: "+field)
    selected = {identifier: block["terminalPass"] for identifier, block in blocks.items()}
    body, credit = endpoint_totals(baseline_blocks, selected)
    require(body == row["packetBodyBytes"] and abs(credit-row["faceByteEstimate"]) < 1e-7
            and math.floor(credit) == row["faceBytes"], "Complete endpoint body and face credit fail actual ledger closure")
    return dict(path=trial["blockMapPath"], sha256=digest(data), endpoints=len(blocks),
                selectedBodyBytes=body, selectedFaceCredit=credit,
                actualBodyAndFaceCreditReconciled=True)


def render_balanced(output, root, source, target, baseline, candidate):
    rows = [target, baseline, candidate]
    images = [Image.fromarray(source)] + [Image.fromarray(captured_pixels(root/row["decodedPngPath"], row["decodedPngSha256"])) for row in rows]
    labels = [["Frozen source", "480 x 640 native RGB pixels"]]
    labels += [[name, f'{row["bytes"]:,} JP2 B; {row["faceBytes"]:,} face B'] for name, row in zip(
        ("Balanced visual reference", "Regional-floor baseline", "Balanced-first research candidate"), rows)]
    status = "Strict separate source guards passed" if candidate["strictSourceGate"]["conservativeRepairEligible"] and candidate["hairSourceErrorNonloss"] else (
        "0.10 dB research face tolerance + separate guards passed" if candidate["conservativeSourceGate"]["conservativeRepairEligible"] and candidate["hairSourceErrorNonloss"] else "Separate source-quality tradeoffs recorded")
    captions = ["Fixed source, masks, quantizer, 11,820 B cap and 7,540 B V3 face floor. Whole coding maps are evaluated.",
                f'Candidate balanced-reference RGB MSE: {candidate["balancedDistance"]["wholeImage"]["mseRgb"]:.4f}; floor: {baseline["balancedDistance"]["wholeImage"]["mseRgb"]:.4f}.',
                status+". Native and physical credential visual review remain separate evidence."]
    panel(images, labels, output/"native-comparison.png", captions)
    panel([image.crop((0, 480, 480, 640)) for image in images], labels, output/"native-neckline-comparison.png",
          ["Native rows 480 through 639. Portrait samples are preserved.", status+"."])
    proxies = []
    for width, role in ((27.75, "Recommended photograph width"), (28.25, "Requested width control")):
        geometry = print_geometry(480, 640, width)
        size = (geometry["rasterWidthPixels"], geometry["rasterHeightPixels"])
        proxies.append(dict(geometry, role=role))
        panel([image.resize(size, Image.Resampling.LANCZOS) for image in images], labels,
              output/f"print-{width:.2f}mm-comparison.png", [
                  f'{role}: {width:.2f} mm at 300 dpi; rounded software raster {size[0]} x {size[1]} pixels.',
                  status+". Source and all decoded candidates use the same Lanczos resampling.",
                  "Physical print size, printer behavior and viewing conditions require intended-credential review."])
    return proxies


def analyze(manifest_path, output, root, render_candidate=None, ids=None):
    helpers = {name: digest((Path(__file__).parent/name).read_bytes()) for name in
               ("score_balanced.py", "score_allocator.py", "analyze_passes.py", "optimize_decoded.py", "edge_quality.py")}
    manifest, captured_manifest = checked_manifest(manifest_path)
    report = score(manifest_path, output/"verification", root, ids=ids, render_outputs=False)
    source = captured_pixels(root/report["sourceCropPath"], report["sourceCropSha256"])
    masks, _ = load_masks(Path(manifest["sourceCropPath"]), source, manifest)
    reference = make_reference(source)
    masks.update(clothingInterior=reference["interior"], clothingSeams=reference["seams"])
    neckline = np.zeros(source.shape[:2], dtype=bool)
    for contour in reference["contours"]:
        for y, x in zip(reference["rows"], contour):
            center = int(math.floor(x+.5))
            neckline[y, max(0, center-12):min(480, center+13)] = True
    neckline.flags.writeable = False
    masks["sourceNecklineStrip"] = neckline
    target, baseline, rows = report["target"], report["floorBaseline"], report["variants"]
    balanced = captured_pixels(root/target["decodedPngPath"], target["decodedPngSha256"])
    floor_pixels = captured_pixels(root/baseline["decodedPngPath"], baseline["decodedPngSha256"])
    target["sourceErrors"] = source_errors(source, balanced, masks)
    baseline["sourceErrors"] = source_errors(source, floor_pixels, masks)
    target["balancedDistance"] = balanced_distances(balanced, balanced, masks)
    baseline["balancedDistance"] = balanced_distances(floor_pixels, balanced, masks)
    replay = next(row for row in manifest["trials"] if row["id"] == "exact-replay-control")
    baseline_blocks, _ = endpoint_map(replay["blockMapPath"], replay["blockMapSha256"])
    trials = {row["id"]: row for row in manifest["trials"]}
    for row in rows:
        pixels = captured_pixels(root/row["decodedPngPath"], row["decodedPngSha256"])
        row["balancedDistance"] = balanced_distances(pixels, balanced, masks)
        row["balancedDistanceGapClosureByRegion"] = {name: distance_closure(values["mseRgb"], baseline["balancedDistance"][name]["mseRgb"])
            for name, values in row["balancedDistance"].items()}
        row["sourceErrors"] = source_errors(source, pixels, masks)
        row["completeEndpointMapEvidence"] = reconcile_complete_map(trials[row["id"]], row, baseline_blocks)
        row["completeEndpointMapEvidence"]["path"] = relative(row["completeEndpointMapEvidence"]["path"], root)
    ranking = rank_balanced(rows, baseline)
    selected = ranking["strictSourceFaceHairContourRanking"] or ranking["conservativePoint10DbSourceRanking"]
    identifier = render_candidate or (selected[0] if selected else None)
    candidate = next((row for row in rows if row["id"] == identifier), None)
    require(identifier is None or candidate is not None and candidate["meetsByteAndRegionalGates"] and finite_metrics(candidate["metrics"]),
            "Render a fully decoded cap/floor candidate with complete source-edge measurements")
    proxies = render_balanced(output, root, source, target, baseline, candidate) if candidate else []
    require(manifest_path.read_bytes() == captured_manifest, "Completed balanced-first manifest changed during scoring")
    require(all(digest((Path(__file__).parent/name).read_bytes()) == expected for name, expected in helpers.items()),
            "Frozen scoring helpers changed during the run")
    report.update(balancedFirstSelection=dict(ranking, renderedCandidate=identifier,
            humanVisualSelection="Pending; balanced control remains the visual reference", productionDefaults="Preserved"),
        printRasterProxies=proxies, balancedScorerSha256=helpers["score_balanced.py"], scoringHelperSha256=helpers,
        derivedSourceMaskEvidence={name: dict(pixels=int(masks[name].sum()), sha256=digest(masks[name].tobytes())) for name in
                                   ("clothingInterior", "clothingSeams", "sourceNecklineStrip")},
        sourceNecklineStripScope="Source-derived contour rows 495 through 631, nearest-column strip plus or minus 12 pixels; mask fixed across candidates")
    report["artifacts"] = {name: dict(path=relative(output/name, root), sha256=digest((output/name).read_bytes())) for name in
        ["native-comparison.png", "native-neckline-comparison.png", "print-27.75mm-comparison.png", "print-28.25mm-comparison.png"]} if candidate else {}
    (output/"scores.json").write_text(json.dumps(report, indent=2, allow_nan=False)+"\n")
    print(json.dumps(dict(ranking, report=relative(output/"scores.json", root)), indent=2))
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--render-candidate", help="Render an explicitly selected feasible research tradeoff")
    parser.add_argument("--id", action="append", help="Verify selected IDs against the unchanged controls")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    analyze(args.manifest, args.output or args.manifest.parent/"balanced-quality", root, args.render_candidate, args.id)


if __name__ == "__main__":
    main()

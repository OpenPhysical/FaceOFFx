#!/usr/bin/env python3
"""Independently score frozen coding-pass interventions on the medical portrait."""
import argparse
import hashlib
import io
import json
import math
import re
import struct
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont, features

from edge_quality import make_reference, measure


METHOD = "synthesis-energy-decoder-effective-pass-v3"
EDGE_KEYS = (
    "necklinePositionRmsPixels", "necklineJaggednessRmsPixels",
    "transitionWidthErrorRmsPixels", "edgeHaloExcessRmsSampleUnits",
    "edgeContrastRelativeErrorRms", "edgeNormalRgbProfileRmseSampleUnits",
    "seamGradientRmseSampleUnitsPerPixel", "clothingChromaBlotchRmseSampleUnits",
)
TRADEOFF_KEYS = EDGE_KEYS[2:]
PAYLOAD_CATEGORIES = (
    "protectedMaxshiftPhaseBytes", "wholeBandPromotedBytes", "codeBlockPromotedBytes",
    "outsideRoiCodeBlockBytes", "mixedOrRefinementBytes",
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def checked_bytes(path, expected_hash):
    data = Path(path).read_bytes()
    require(digest(data) == expected_hash, "SHA256 mismatch: " + str(path))
    return data


def relative(path, root):
    return Path(path).resolve().relative_to(root.resolve()).as_posix()


def boxes(data):
    position = 0
    while position < len(data):
        require(position + 8 <= len(data), "Truncated JP2 box header")
        length, kind = struct.unpack_from(">I4s", data, position)
        header = 8
        if length == 1:
            require(position + 16 <= len(data), "Truncated extended JP2 box header")
            length = struct.unpack_from(">Q", data, position + 8)[0]
            header = 16
        elif length == 0:
            length = len(data) - position
        require(header <= length <= len(data) - position, "Invalid JP2 box length")
        yield kind, data[position + header:position + length]
        position += length


def inspect_jp2(data, dimensions, block=64):
    """Parse structure independently; telemetry supplies the packet header/body split."""
    top = list(boxes(data))
    require(top and top[0] == (b"jP  ", b"\r\n\x87\n"), "JP2 signature mismatch")
    headers = [payload for kind, payload in top if kind == b"jp2h"]
    streams = [payload for kind, payload in top if kind == b"jp2c"]
    require(len(headers) == len(streams) == 1, "Expected one JP2 header and codestream")
    colors = [payload for kind, payload in boxes(headers[0]) if kind == b"colr"]
    require(colors == [b"\x01\x00\x00\x00\x00\x00\x10"], "Expected enumerated sRGB")
    stream = streams[0]
    require(stream[:2] == b"\xffO" and stream[-2:] == b"\xff\xd9", "SOC/EOC mismatch")
    position, tile_end, structural, packet_bytes = 2, None, 2, 0
    rgn, quantizers, coding, siz_seen, tiles = [], [], [], False, 0
    while position < len(stream):
        require(position + 2 <= len(stream), "Truncated codestream marker")
        marker = struct.unpack_from(">H", stream, position)[0]
        if marker == 0xffd9:
            require(position + 2 == len(stream), "Bytes after EOC")
            structural += 2
            break
        if marker == 0xff93:
            require(tile_end is not None and position + 2 < tile_end <= len(stream), "Invalid tile packet range")
            structural += 2
            packet_bytes += tile_end - position - 2
            position, tile_end = tile_end, None
            continue
        require(position + 4 <= len(stream), "Truncated codestream segment")
        length = struct.unpack_from(">H", stream, position + 2)[0]
        require(length >= 2 and position + 2 + length <= len(stream), "Invalid segment length")
        segment = stream[position + 2:position + 2 + length]
        structural += 2 + length
        if marker == 0xff51:
            require(length == 47, "Expected three-component SIZ")
            require(struct.unpack_from(">H", segment, 2)[0] == 0, "Expected Part 1 Rsiz profile 0")
            count = struct.unpack_from(">H", segment, 36)[0]
            xs, ys, xo, yo = struct.unpack_from(">IIII", segment, 4)
            xt, yt, xto, yto = struct.unpack_from(">IIII", segment, 20)
            require((xs, ys) == dimensions and (xo, yo, xto, yto) == (0, 0, 0, 0)
                    and (xt, yt) == dimensions and count == 3, "Expected full-image single tile")
            require(all(segment[38 + 3*c:41 + 3*c] == b"\x07\x01\x01" for c in range(3)),
                    "Expected three unsigned 8-bit full-grid components")
            siz_seen = True
        elif marker == 0xff52:
            require(length == 12, "Unexpected COD payload")
            require(segment[2] == 0 and segment[3] == 0 and struct.unpack_from(">H", segment, 4)[0] == 1
                    and segment[6] == 1 and segment[7] == 5 and segment[10] == 0 and segment[11] == 0,
                    "Expected LRCP/single-layer/ICT/five-level/9-7 COD")
            require((1 << (segment[8]+2), 1 << (segment[9]+2)) == (block, block), "Code-block mismatch")
            coding.append(segment.hex())
        elif marker == 0xff90:
            require(length == 10 and struct.unpack_from(">H", segment, 2)[0] == 0, "Unexpected SOT")
            tile_end = position + struct.unpack_from(">I", segment, 4)[0]
            tiles += 1
        elif marker == 0xff5e:
            require(length == 5 and segment[3] == 0, "Expected Maxshift RGN")
            rgn.append([segment[2], segment[4]])
        elif marker in (0xff5c, 0xff5d):
            quantizers.append({"marker": hex(marker), "sha256": digest(segment)})
        elif marker in (0xff53, 0xff5f):
            raise ValueError("Unexpected component/progression override")
        position += 2 + length
    require(siz_seen and coding and tiles == 1 and sorted(item[0] for item in rgn) == [0, 1, 2],
            "Missing expected SIZ/COD/RGN/single tile")
    require(structural + packet_bytes == len(stream), "Independent structural byte closure failed")
    return dict(completeOutputBytes=len(data), codestreamBytes=len(stream), containerBytes=len(data)-len(stream),
                structuralHeaderBytes=structural, packetHeaderAndBodyBytes=packet_bytes,
                packetSplitEvidence="Codec telemetry, checked against independently parsed combined packet bytes",
                quantizationSegments=quantizers, rgn=rgn, codingSegments=coding)


def band_key(band):
    return tuple(band[key] for key in ("tile", "component", "resolutionLevel", "subband"))


def check_ledger(telemetry, attribution, structure, roi_pixels):
    require(attribution["methodId"] == METHOD and attribution["roiPixelCount"] == roi_pixels, "Attribution method/ROI mismatch")
    require(telemetry["totalOutputBytes"] == structure["completeOutputBytes"]
            and telemetry["codestreamBytes"] == structure["codestreamBytes"]
            and telemetry["containerBytes"] == structure["containerBytes"], "Output telemetry mismatch")
    require(telemetry["mainAndTileHeaderBytes"] + telemetry["endOfCodestreamBytes"] == structure["structuralHeaderBytes"]
            and telemetry["packetHeaderBytes"] + telemetry["packetBodyBytes"] == structure["packetHeaderAndBodyBytes"],
            "Header/body closure mismatch")
    require(sum(telemetry[key] for key in PAYLOAD_CATEGORIES) == telemetry["packetBodyBytes"], "Payload categories mismatch")
    telemetry_rows = {band_key(band): band["payloadBytes"] for band in telemetry["subbands"]}
    attribution_rows = {band_key(band): band["payloadBytes"] for band in attribution["subbands"]}
    require(len(telemetry_rows) == len(telemetry["subbands"]) and len(attribution_rows) == len(attribution["subbands"])
            and telemetry_rows == attribution_rows and sum(telemetry_rows.values()) == telemetry["packetBodyBytes"],
            "Per-band payload mismatch")
    require(all(sum(band[key] for band in telemetry["subbands"]) == telemetry[key] for key in PAYLOAD_CATEGORIES),
            "Per-band payload category mismatch")
    estimate = attribution["facePayloadByteEstimate"]
    require(math.isfinite(estimate) and 0 <= estimate <= telemetry["packetBodyBytes"]
            and attribution["attributedFacePayloadBytes"] == math.floor(estimate)
            and attribution["packetBodyBytes"] == telemetry["packetBodyBytes"]
            and attribution["attributedFacePayloadBytes"] + attribution["outsidePayloadBytes"] == attribution["packetBodyBytes"],
            "Regional ledger conservation failed")
    require(all(math.isfinite(band["facePayloadByteEstimate"]) and 0 <= band["facePayloadByteEstimate"] <= band["payloadBytes"]
                for band in attribution["subbands"])
            and abs(sum(band["facePayloadByteEstimate"] for band in attribution["subbands"]) - estimate) < 1e-7,
            "Per-band face estimate mismatch")


def change_costs(trial):
    receivers, donors = [], []
    for change in trial["changes"]:
        require(re.fullmatch(r"t\d+-c\d+-r\d+-s\d+-b\d+", change["blockId"]), "Unsafe block ID")
        delta = change["variantBodyBytes"] - change["baselineBodyBytes"]
        require((change["toPass"] > change["fromPass"] and delta >= 0)
                or (change["toPass"] < change["fromPass"] and delta <= 0), "Endpoint/cost direction mismatch")
        entry = dict(change, bodyByteDelta=delta)
        (receivers if delta >= 0 else donors).append(entry)
    require(sum(item["bodyByteDelta"] for item in receivers + donors) == trial["bodyByteDelta"], "Targeted block costs fail body closure")
    return dict(receivers=receivers, donors=donors,
                receiverAddedBodyBytes=sum(item["bodyByteDelta"] for item in receivers),
                donorReleasedBodyBytes=-sum(item["bodyByteDelta"] for item in donors),
                netBodyBytes=trial["bodyByteDelta"], packetHeaderByteDelta=trial["packetHeaderByteDelta"],
                completeJp2ByteDelta=trial["completeJp2ByteDelta"])


def finite_metrics(metrics):
    return (metrics["necklineMissingRows"] == 0 and metrics["transitionMissingSamples"] == 0
            and all(isinstance(value, (int, float)) and math.isfinite(value) for value in metrics.values()))


def quality(source, candidate, masks):
    squared = (candidate.astype(np.float64) - source.astype(np.float64)) ** 2
    output = {}
    for name, mask in masks.items():
        mse = float(squared[mask].mean())
        output[name] = dict(pixels=int(mask.sum()), mseRgb=mse,
                            psnrRgbDb=10 * math.log10(255**2/mse) if mse else None,
                            exactPixelEquality=mse == 0)
    return output


def gate_candidate(row, baseline, face_tolerance=.10, regression_limit=.10):
    metrics, previous = row["metrics"], baseline["metrics"]
    metric_gate = finite_metrics(metrics)
    actual_face, previous_face = row["quality"]["face"], baseline["quality"]["face"]
    actual_psnr, previous_psnr = actual_face["psnrRgbDb"], previous_face["psnrRgbDb"]
    if actual_psnr is None or previous_psnr is None:
        require(actual_psnr is not None or actual_face.get("exactPixelEquality") is True, "Missing face PSNR needs exact pixel equality")
        require(previous_psnr is not None or previous_face.get("exactPixelEquality") is True, "Missing baseline face PSNR needs exact pixel equality")
        equal_exact = actual_psnr is previous_psnr is None
        face_delta = 0. if equal_exact else None
        face_no_loss = actual_psnr is None
        face_guard = face_no_loss
        face_comparison = "Exact pixel equality preserved" if equal_exact else (
            "Exact pixel equality gained" if face_no_loss else "Source-exact baseline changed")
    else:
        require(math.isfinite(actual_psnr) and math.isfinite(previous_psnr), "Supply finite face PSNR or explicit exact equality")
        face_delta = actual_psnr-previous_psnr
        face_guard = face_delta >= -face_tolerance-1e-12
        face_no_loss = face_delta >= -1e-12
        face_comparison = "Finite source-referenced PSNR difference"
    def relative_regression(value, original):
        if value is None or not math.isfinite(value):
            return None
        if original == 0:
            return 0. if value == 0 else None
        return value / original - 1
    tradeoffs = {key: relative_regression(metrics[key], previous[key]) for key in TRADEOFF_KEYS}
    edge_gain = (metric_gate and metrics[EDGE_KEYS[0]] < previous[EDGE_KEYS[0]]
                 and metrics[EDGE_KEYS[1]] < previous[EDGE_KEYS[1]])
    bounded_tradeoffs = all(value is not None and math.isfinite(value) and value <= regression_limit + 1e-12
                           for value in tradeoffs.values())
    eligible = row["meetsByteAndRegionalGates"] and metric_gate and face_guard
    return dict(completeMetrics=metric_gate, facePsnrDeltaDb=face_delta,
                faceComparison=face_comparison, faceGuardPassed=face_guard,
                strictZeroFaceLossPassed=face_no_loss,
                edgePositionAndJaggednessImproved=edge_gain,
                otherErrorRelativeChanges=tradeoffs, otherErrorBoundsPassed=bounded_tradeoffs,
                eligible=eligible, conservativeRepairEligible=eligible and edge_gain and bounded_tradeoffs,
                contourErrorGeometricMeanRatio=math.sqrt(metrics[EDGE_KEYS[0]] / previous[EDGE_KEYS[0]]
                    * metrics[EDGE_KEYS[1]] / previous[EDGE_KEYS[1]])
                    if metric_gate and previous[EDGE_KEYS[0]] > 0 and previous[EDGE_KEYS[1]] > 0 else None)


def face_sort_value(row):
    value = row["gate"]["facePsnrDeltaDb"]
    return value if value is not None else (math.inf if row["gate"]["strictZeroFaceLossPassed"] else -math.inf)


def pixel_changes(source, baseline, candidate):
    delta = candidate.astype(np.int16) - baseline.astype(np.int16)
    before = baseline.astype(np.float64) - source.astype(np.float64)
    after = candidate.astype(np.float64) - source.astype(np.float64)
    improvement = np.mean(before**2 - after**2, axis=2)
    return delta, improvement


def print_geometry(width, height, width_mm, dpi=300):
    ideal = width_mm / 25.4 * dpi
    pixels = int(math.floor(ideal + .5))
    proxy_height = int(math.floor(height * pixels / width + .5))
    return dict(requestedWidthMm=width_mm, dpi=dpi, idealWidthPixels=ideal,
                rasterWidthPixels=pixels, rasterHeightPixels=proxy_height,
                realizedWidthMm=pixels / dpi * 25.4, realizedHeightMm=proxy_height / dpi * 25.4,
                widthRoundingPixels=pixels-ideal,
                rounding="Nearest integer width; nearest integer aspect-preserving height",
                resampling="Identical Pillow Lanczos RGB resampling for source and every candidate",
                nativePixelDistanceToMm=width_mm/width,
                reviewScope="Software raster proxy. Physical print, color management, halftoning, substrate and intended-size review are separate measurements.")


def panel(images, labels, path, footnotes):
    require(len(images) == len(labels) and len({image.size for image in images}) == 1, "Panel geometry mismatch")
    width, height = images[0].size
    gap, margin, top = 16, 20, 64
    canvas = Image.new("RGB", (margin*2 + width*len(images) + gap*(len(images)-1), height+top+28+20*len(footnotes)), "white")
    font = ImageFont.load_default(size=16)
    draw = ImageDraw.Draw(canvas)
    for index, (image, lines) in enumerate(zip(images, labels)):
        x = margin+index*(width+gap)
        for number, line in enumerate(lines):
            draw.text((x, 10+number*22), line, fill=(20, 25, 30), font=font)
        canvas.paste(image, (x, top))
        require(np.array_equal(np.asarray(canvas)[top:top+height, x:x+width], np.asarray(image)), "Panel altered portrait samples")
    for index, line in enumerate(footnotes):
        draw.text((margin, top+height+10+index*20), line, fill=(35, 40, 45), font=font)
    canvas.save(path)


def render_comparisons(output, root, source, control, baseline, winner):
    rows = [control, baseline, winner]
    images = [Image.fromarray(source)] + [Image.open(root/row["decodedPngPath"]).convert("RGB") for row in rows]
    winner_label = "Selected local pass repair" if winner["id"] != baseline["id"] else "Baseline retained"
    labels = [["Frozen source", "Native 480 x 640 RGB"]] + [
        [title, f'{row["bytes"]:,} JP2 B; {row["faceBytes"]:,} face B']
        for title, row in zip(("Balanced visual control", "Regional-floor baseline", winner_label), rows)]
    panel(images, labels, output/"native-comparison.png", [
        "Native portrait pixels, labels outside pixels. Pass repair uses fixed masks and all unrelated endpoints frozen.",
        "Visual control carries its measured face credit; the baseline and repair meet the recorded V3 byte floor.",
        "Research comparison. Capture, anatomical measurements and actual credential printing have separate review evidence."])
    panel([image.crop((0, 480, source.shape[1], source.shape[0])) for image in images], labels,
          output/"native-neckline-comparison.png", [
              "Unscaled native neckline extracts, source rows 480 through 639. All candidates share the frozen source reference."])
    proxies = []
    for width_mm, role in ((27.75, "Recommended photograph width"), (28.25, "Requested width control")):
        geometry = print_geometry(source.shape[1], source.shape[0], width_mm)
        size = (geometry["rasterWidthPixels"], geometry["rasterHeightPixels"])
        resized = [image.resize(size, Image.Resampling.LANCZOS) for image in images]
        for name, image in zip(("source", "balanced-control", "floor-baseline", "best-repair"), resized):
            image.save(output/f"print-{width_mm:.2f}mm-{name}.png", dpi=(300, 300))
        proxy_labels = [["Frozen source", f"RGB proxy {size[0]} x {size[1]}"]] + labels[1:]
        panel(resized, proxy_labels, output/f"print-{width_mm:.2f}mm-comparison.png", [
            f"{role}: {width_mm:.2f} mm, 300 dpi software proxy; {size[0]} x {size[1]} pixels after rounding.",
            "On-screen size depends on the viewer. Intended-size physical printing requires separate review."])
        geometry["role"] = role
        geometry["nativeErrorConversions"] = {row["id"]: {key.replace("Pixels", "Mm"): row["metrics"][key]*width_mm/source.shape[1]
            for key in ("necklinePositionRmsPixels", "necklineJaggednessRmsPixels", "transitionWidthErrorRmsPixels")}
            for row in rows if finite_metrics(row["metrics"])}
        proxies.append(geometry)
    return proxies


def analyze(manifest_path, output, root, face_tolerance=.10, regression_limit=.10):
    raw_manifest = manifest_path.read_bytes()
    manifest = json.loads(raw_manifest)
    require(manifest["completed"] and manifest["trialCount"] == len(manifest["trials"]), "Wait for completed probe manifest")
    require(manifest["methodIdentifier"] == METHOD and manifest["productionForkUnchanged"] is True, "Probe scope mismatch")
    require(manifest["minimumAttributedFacePayloadBytes"] == (manifest["roiPixelCount"]+7)//8, "Face floor mismatch")
    options = manifest["fixedOptions"]
    require(options["maximumOutputBytes"] == manifest["maximumJp2Bytes"] and options["outputFormat"] == 0
            and options["lossless"] is False and options["targetBitsPerPixel"] is None
            and options["codeBlockWidth"] == options["codeBlockHeight"] == 64
            and options["decompositionLevels"] == 5 and options["applyComponentTransform"] is True
            and options["useCoefficientDistortionForAllocation"] is False
            and options["backgroundSubbandWeights"] is None and options["quantizationStep"] is None
            and options["roi"]["startResolutionLevel"] == 4 and options["roi"]["alignToCodeBlocks"] is False
            and options["roi"]["minimumProtectedPayloadBytes"] is None
            and options["roi"]["minimumAttributedFacePayloadBytes"] == manifest["minimumAttributedFacePayloadBytes"],
            "Frozen balanced research options mismatch")
    weights = options["subbandWeights"]["entries"]
    expected_bands = {(0, 0, 0)} | {(0, resolution, subband) for resolution in range(1, 6) for subband in (1, 2, 3)}
    require(len(weights) == 16 and {(entry["component"], entry["resolutionLevel"], entry["subband"]) for entry in weights} == expected_bands
            and all(entry["weight"] == 1.25 for entry in weights), "Frozen all-luma 1.25 weights mismatch")
    dimensions = (manifest["width"], manifest["height"])
    require(dimensions == (480, 640), "Medical source preset requires frozen 480 x 640 geometry")
    source_path, coding_path = Path(manifest["sourceCropPath"]), Path(manifest["maskPath"])
    source_data = checked_bytes(source_path, manifest["sourceCropSha256"])
    coding_data = checked_bytes(coding_path, manifest["maskSha256"])
    with Image.open(io.BytesIO(source_data)) as image:
        require(image.mode == "RGB" and image.size == dimensions, "Source RGB geometry mismatch")
        source = np.array(image)
    with Image.open(io.BytesIO(coding_data)) as image:
        coding = np.array(image) != 0
    require(coding.shape == source.shape[:2] and int(coding.sum()) == manifest["roiPixelCount"], "Coding mask mismatch")
    mask_manifest = json.loads((source_path.parent/"research-encodings/manifest.json").read_text())
    require(mask_manifest["crop_sha256"] == manifest["sourceCropSha256"]
            and mask_manifest["face_mask_sha256"] == manifest["maskSha256"], "Quality bundle source/coding mask mismatch")
    quality_path, head_path = source_path.parent/"quality-face-mask.pgm", source_path.parent/"head-mask.pgm"
    quality_data = checked_bytes(quality_path, mask_manifest["quality_face_mask_sha256"])
    head_data = checked_bytes(head_path, mask_manifest["head_mask_sha256"])
    with Image.open(io.BytesIO(quality_data)) as image:
        face = np.array(image) != 0
    with Image.open(io.BytesIO(head_data)) as image:
        head = np.array(image) != 0
    require(face.shape == coding.shape == head.shape and np.all(head[face]), "Quality mask support mismatch")
    masks = dict(face=face, codingFace=coding, hair=head & ~face, outsideFace=~face,
                 wholeImage=np.ones(face.shape, dtype=bool))
    reference = make_reference(source)
    output.mkdir(parents=True, exist_ok=True)
    decode_dir = manifest_path.parent/"independent-decodes"
    decode_dir.mkdir(exist_ok=True)
    baseline_data = checked_bytes(manifest["baselineJp2Path"], manifest["baselineSha256"])
    baseline_structure = inspect_jp2(baseline_data, dimensions)
    replay = next(trial for trial in manifest["trials"] if trial["id"] == "replay-control")
    baseline_codec = dict(payloadTelemetry=replay["payloadTelemetry"], sharedPayloadAttribution=replay["sharedPayloadAttribution"])
    require(len(baseline_data) == manifest["baselineJp2Bytes"] <= manifest["maximumJp2Bytes"], "Baseline cap mismatch")
    require(replay["bodyBytes"] == manifest["baselinePacketBodyBytes"]
            and replay["packetHeaderBytes"] == manifest["baselinePacketHeaderBytes"]
            and replay["attributedFacePayloadBytes"] == manifest["baselineAttributedFacePayloadBytes"]
            and replay["facePayloadByteEstimate"] == manifest["baselineFacePayloadByteEstimate"], "Baseline/replay evidence mismatch")

    def decode_row(identifier, path, expected_hash, codec, require_floor):
        require(re.fullmatch(r"[A-Za-z0-9_-]+", identifier), "Unsafe trial ID")
        data = checked_bytes(path, expected_hash)
        structure = inspect_jp2(data, dimensions)
        require(len(data) <= manifest["maximumJp2Bytes"], "JP2 cap exceeded")
        for key in ("quantizationSegments", "rgn", "codingSegments"):
            require(structure[key] == baseline_structure[key], "Frozen quantizer/coding markers changed: "+key)
        telemetry, attribution = codec["payloadTelemetry"], codec["sharedPayloadAttribution"]
        check_ledger(telemetry, attribution, structure, manifest["roiPixelCount"])
        floor_met = attribution["attributedFacePayloadBytes"] >= manifest["minimumAttributedFacePayloadBytes"]
        require(floor_met or not require_floor, "Attributed face floor failed")
        with Image.open(io.BytesIO(data)) as image:
            image.load()
            require(image.mode == "RGB" and image.size == dimensions, "Independent decode geometry mismatch")
            pixels = np.array(image)
        png = decode_dir/(identifier+".png")
        Image.fromarray(pixels).save(png)
        result = dict(id=identifier, jp2Path=relative(path, root), jp2Sha256=digest(data),
                      decodedPngPath=relative(png, root), decodedPngSha256=digest(png.read_bytes()), bytes=len(data),
                      faceBytes=attribution["attributedFacePayloadBytes"], faceByteEstimate=attribution["facePayloadByteEstimate"],
                      faceRatio=3*manifest["roiPixelCount"]/attribution["attributedFacePayloadBytes"],
                      packetBodyBytes=telemetry["packetBodyBytes"], packetHeaderBytes=telemetry["packetHeaderBytes"],
                      outsideBytes=attribution["outsidePayloadBytes"], meetsByteAndRegionalGates=floor_met,
                      structure=structure, metrics=measure(reference, pixels), quality=quality(source, pixels, masks))
        return result, pixels

    baseline, baseline_pixels = decode_row("floor-baseline", manifest["baselineJp2Path"], manifest["baselineSha256"], baseline_codec, True)
    balanced_results = json.loads((root/"artifacts/piv-encoding-current/landmark-v3/medical.json").read_text())["results"]
    balanced = next(row for row in balanced_results if row["name"] == "balanced-start4-b64-noquota")
    control, _ = decode_row("balanced-visual-control", balanced["jp2Path"], balanced["jp2Sha256"], balanced["apiResult"], False)
    rows, rejected, identifiers = [], [], set()
    for index, trial in enumerate(manifest["trials"]):
        require(re.fullmatch(r"[A-Za-z0-9_-]+", trial["id"]), "Unsafe trial ID")
        require(trial["id"] not in identifiers, "Duplicate trial ID")
        identifiers.add(trial["id"])
        require(trial["allUntargetedEndpointsFrozen"] is True, "Unrelated coding endpoints changed")
        require(trial["unchangedEndpointCount"] + len(trial["changes"]) == replay["unchangedEndpointCount"]
                and len({change["blockId"] for change in trial["changes"]}) == len(trial["changes"]), "Endpoint inventory mismatch")
        costs = change_costs(trial)
        require(trial["bodyBytes"] - manifest["baselinePacketBodyBytes"] == trial["bodyByteDelta"]
                and trial["packetHeaderBytes"] - manifest["baselinePacketHeaderBytes"] == trial["packetHeaderByteDelta"]
                and trial["bodyByteDelta"] + trial["packetHeaderByteDelta"] == trial["completeJp2ByteDelta"], "Trial delta closure failed")
        if trial["status"] == "rejected-diagnostic":
            require(trial["jp2Path"] is None and trial["sha256"] is None and trial["completeJp2Bytes"] is None
                    and not (manifest_path.parent/"variants"/(trial["id"]+".jp2")).exists(), "Rejected probe emitted a portrait")
            rejected.append(dict(id=trial["id"], costs=costs, error=trial["error"],
                                 simulatedCompleteJp2Bytes=trial["simulatedCompleteJp2Bytes"],
                                 faceBytes=trial["attributedFacePayloadBytes"], capSatisfied=trial["capSatisfied"],
                                 faceFloorSatisfied=trial["faceFloorSatisfied"]))
            continue
        require(trial["status"] == "accepted-cap-and-face-floor" and trial["capSatisfied"] and trial["faceFloorSatisfied"], "Unknown/failed accepted trial")
        row, pixels = decode_row(trial["id"], trial["jp2Path"], trial["sha256"], trial, True)
        require(row["bytes"] == trial["completeJp2Bytes"] == trial["simulatedCompleteJp2Bytes"]
                and row["bytes"] - baseline["bytes"] == trial["completeJp2ByteDelta"]
                and row["packetBodyBytes"] == trial["bodyBytes"] and row["packetHeaderBytes"] == trial["packetHeaderBytes"]
                and row["faceBytes"] == trial["attributedFacePayloadBytes"], "Emitted trial differs from probe ledger")
        require(abs(row["faceRatio"]-trial["faceCompressionRatio"]) < 1e-10, "Trial ratio mismatch")
        row.update(kind=trial["kind"], costs=costs, unchangedEndpointCount=trial["unchangedEndpointCount"],
                   allUntargetedEndpointsFrozen=True, changedRgbSamples=int(np.count_nonzero(pixels != baseline_pixels)),
                   gate=gate_candidate(row, baseline, face_tolerance, regression_limit),
                   metricDeltas={key: row["metrics"][key]-baseline["metrics"][key] if row["metrics"][key] is not None else None for key in EDGE_KEYS})
        if trial["kind"] == "control":
            require(digest(baseline_data) == row["jp2Sha256"] and row["changedRgbSamples"] == 0, "Exact pass replay changed bytes or pixels")
        rows.append(row)
        if index % 25 == 0:
            print(f"Verified/scored {index+1}/{len(manifest['trials'])} trials", flush=True)
    require(manifest_path.read_bytes() == raw_manifest, "Probe manifest changed during analysis; rerun completed snapshot")
    eligible = sorted((row for row in rows if row["gate"]["conservativeRepairEligible"]),
                      key=lambda row: (row["gate"]["contourErrorGeometricMeanRatio"], -face_sort_value(row), row["bytes"], row["id"]))
    strict = [row for row in eligible if row["gate"]["strictZeroFaceLossPassed"]]
    tradeoffs = sorted((row for row in rows if row["gate"]["eligible"] and row["gate"]["edgePositionAndJaggednessImproved"]),
                      key=lambda row: row["gate"]["contourErrorGeometricMeanRatio"])
    winner = eligible[0] if eligible else baseline
    winner_pixels = np.array(Image.open(root/winner["decodedPngPath"]))
    delta, improvement = pixel_changes(source, baseline_pixels, winner_pixels)
    np.save(output/"winner-signed-rgb-change.npy", delta, allow_pickle=False)
    np.save(output/"winner-signed-error-improvement.npy", improvement, allow_pickle=False)
    limit = max(1., float(np.quantile(np.abs(improvement[improvement != 0]), .98))) if np.any(improvement) else 1.
    amount = np.clip(np.abs(improvement)/limit, 0, 1)
    colors = np.full((*improvement.shape, 3), 255., dtype=np.float64)
    target = np.where((improvement > 0)[..., None], [25., 100., 220.], [220., 45., 35.])
    colors += (target-colors)*amount[..., None]
    Image.fromarray(np.uint8(np.rint(colors))).save(output/"winner-signed-error-improvement.png")
    print_proxies = render_comparisons(output, root, source, control, baseline, winner)
    report = dict(schemaVersion=1, manifestPath=relative(manifest_path, root), manifestSha256=digest(raw_manifest),
                  trialCount=len(manifest["trials"]), independentlyDecodedAcceptedTrials=len(rows), rejectedTrials=len(rejected),
                  decoder="Pillow/OpenJPEG "+str(features.version("jpg_2000")), sourceCropSha256=manifest["sourceCropSha256"],
                  codingMaskSha256=manifest["maskSha256"], qualityMaskSha256=mask_manifest["quality_face_mask_sha256"],
                  headMaskSha256=mask_manifest["head_mask_sha256"], codecAssemblySha256=manifest["codecAssemblySha256"],
                  methodId=METHOD, fixedOptions=options,
                  analysisScriptSha256=digest(Path(__file__).read_bytes()),
                  edgeMetricModuleSha256=digest((Path(__file__).parent/"edge_quality.py").read_bytes()),
                  imageByteCap=manifest["maximumJp2Bytes"], minimumAttributedFacePayloadBytes=manifest["minimumAttributedFacePayloadBytes"],
                  sourceRegionEvidence=dict(region=reference["region"].__dict__,
                      contoursSha256=digest(np.asarray(reference["contours"]).tobytes()),
                      clothingInteriorSha256=digest(reference["interior"].tobytes()), seamsSha256=digest(reference["seams"].tobytes()),
                      scope="Fixed source-derived teal clothing boundaries, source-only seam/fold support and reviewed anatomical quality masks"),
                  selection=dict(faceLossToleranceDb=face_tolerance, otherErrorRegressionLimit=regression_limit,
                      toleranceScope="Explicit research acceptance tolerances",
                      ranking="Both contour errors improve; rank their geometric-mean baseline ratios after face and separate error guards",
                      conservativeRanking=[row["id"] for row in eligible], zeroFaceLossRanking=[row["id"] for row in strict],
                      tradeoffRanking=[row["id"] for row in tradeoffs], winner=winner["id"],
                      scope="Best among the tested conservative exact-pass candidates; human visual preference is recorded separately",
                      status="Local research repair selected" if eligible else "Baseline retained; tradeoff candidates recorded"),
                  balancedVisualControl=control, regionalFloorBaseline=baseline, candidates=rows, rejected=rejected,
                  signedChangeMap=dict(rgbChange="candidate minus baseline, exact signed RGB sample differences",
                      errorImprovement="mean RGB squared source error of baseline minus candidate; positive means improvement",
                      display="Blue improves; red worsens; white has zero net source-error MSE change. Symmetric color saturation clips at the 98th absolute nonzero percentile",
                      saturationSampleSquaredUnits=limit, meanErrorImprovement=float(improvement.mean()),
                      changedPixels=int(np.count_nonzero(np.any(delta != 0, axis=2)))),
                  printRasterProxies=print_proxies,
                  printReferences=["https://pages.nist.gov/FIPS201/FIPS201/images/cardfront-required.png", "https://pages.nist.gov/FIPS201/frontend/"],
                  productionDefaults="Preserved; exact-pass interventions use an isolated codec copy",
                  reviewRequirements="Human native-view and intended-size print review; source capture, anatomy, color and issuer evidence remain separately recorded")
    report["artifacts"] = {path.name: dict(path=relative(path, root), sha256=digest(path.read_bytes()))
                           for path in sorted(output.iterdir()) if path.suffix in (".png", ".npy")}
    (output/"rankings.json").write_text(json.dumps(report, indent=2, allow_nan=False)+"\n")
    print(json.dumps(dict(winner=winner["id"], conservativeCandidates=len(eligible), accepted=len(rows),
                         rejected=len(rejected), report=relative(output/"rankings.json", root)), indent=2))
    return report


def main():
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=root/"artifacts/piv-encoding-current/pass-probe/medical/manifest.json")
    parser.add_argument("--output", type=Path, default=root/"artifacts/piv-encoding-current/pass-probe/medical/edge-analysis")
    parser.add_argument("--maximum-face-loss-db", type=float, default=.10)
    parser.add_argument("--maximum-other-error-regression", type=float, default=.10)
    args = parser.parse_args()
    require(math.isfinite(args.maximum_face_loss_db) and args.maximum_face_loss_db >= 0
            and math.isfinite(args.maximum_other_error_regression) and args.maximum_other_error_regression >= 0, "Supply finite nonnegative research guards")
    require(features.check("jpg_2000"), "Independent OpenJPEG decoder is required")
    analyze(args.manifest, args.output, root, args.maximum_face_loss_db, args.maximum_other_error_regression)


if __name__ == "__main__":
    main()

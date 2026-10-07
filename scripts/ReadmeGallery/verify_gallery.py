#!/usr/bin/env python3
"""Development-only structural verification and native OpenJPEG gallery previews."""
import hashlib
import html
import io
import json
import math
import struct
import sys
from pathlib import Path

import numpy as np
from PIL import Image, features

METHOD = "synthesis-energy-decoder-effective-pass-v3"


def digest(data):
    return hashlib.sha256(data).hexdigest()


def checked_bytes(path, expected):
    data = Path(path).read_bytes()
    assert digest(data) == expected, f"Hash mismatch: {path}"
    return data


def boxes(data):
    position = 0
    while position < len(data):
        assert len(data) - position >= 8
        size, kind = struct.unpack_from(">I4s", data, position)
        header = 8
        if size == 1:
            assert len(data) - position >= 16
            size = struct.unpack_from(">Q", data, position + 8)[0]
            header = 16
        elif size == 0:
            size = len(data) - position
        assert header <= size <= len(data) - position
        yield kind, data[position + header:position + size]
        position += size
    assert position == len(data)


def inspect(data, dimensions, block=64):
    top = list(boxes(data))
    assert top[0] == (b"jP  ", b"\r\n\x87\n")
    headers = [payload for kind, payload in top if kind == b"jp2h"]
    assert len(headers) == 1
    assert [p for k, p in boxes(headers[0]) if k == b"colr"] == [b"\x01\x00\x00\x00\x00\x00\x10"]
    streams = [p for k, p in top if k == b"jp2c"]
    assert len(streams) == 1
    stream = streams[0]
    assert stream[:2] == b"\xffO" and stream[-2:] == b"\xff\xd9"
    position, tile_end, structural, packet_bytes = 2, None, 2, 0
    rgn, quantizers, seen = [], [], set()
    while position < len(stream):
        marker = struct.unpack_from(">H", stream, position)[0]
        if marker == 0xffd9:
            assert position + 2 == len(stream)
            structural += 2
            break
        if marker == 0xff93:
            assert tile_end is not None and position + 2 <= tile_end <= len(stream)
            structural += 2
            packet_bytes += tile_end - position - 2
            position, tile_end = tile_end, None
            continue
        length = struct.unpack_from(">H", stream, position + 2)[0]
        assert length >= 2 and position + 2 + length <= len(stream)
        segment = stream[position + 2:position + 2 + length]
        structural += 2 + length
        seen.add(marker)
        if marker == 0xff51:
            assert struct.unpack_from(">H", segment, 2)[0] == 0
            count = struct.unpack_from(">H", segment, 36)[0]
            xs, ys, xo, yo = struct.unpack_from(">IIII", segment, 4)
            assert (xs - xo, ys - yo) == dimensions and count == 3
            xt, yt, xto, yto = struct.unpack_from(">IIII", segment, 20)
            assert (xo, yo, xto, yto) == (0, 0, 0, 0) and (xt, yt) == dimensions
            assert all(segment[38 + 3*c:41 + 3*c] == b"\x07\x01\x01" for c in range(3))
        elif marker == 0xff52:
            assert segment[2:8] == b"\x00\x00\x00\x01\x01\x05"
            assert segment[10:12] == b"\x00\x00"
            assert (1 << (segment[8] + 2), 1 << (segment[9] + 2)) == (block, block)
        elif marker == 0xff90:
            tile_end = position + struct.unpack_from(">I", segment, 4)[0]
        elif marker == 0xff5e:
            assert length == 5 and segment[3] == 0
            rgn.append((segment[2], segment[4]))
        elif marker in (0xff5c, 0xff5d):
            quantizers.append({"Marker": hex(marker), "SegmentSha256": digest(segment)})
        position += 2 + length
    assert {0xff51, 0xff52, 0xff5c, 0xff90} <= seen
    assert sorted(c for c, _ in rgn) == [0, 1, 2]
    assert structural + packet_bytes == len(stream)
    return {"CodestreamBytes": len(stream), "ContainerBytes": len(data) - len(stream),
            "StructuralHeaderBytes": structural, "PacketHeaderAndBodyBytes": packet_bytes,
            "QuantizationSegments": quantizers, "Rgn": rgn}


def verify_ledger(codec, regional, structure, size):
    telemetry, ledger = codec["PayloadTelemetry"], regional["Attribution"]
    assert telemetry["TotalOutputBytes"] == size
    assert codec["CodestreamBytes"] == structure["CodestreamBytes"]
    assert codec["ContainerBytes"] == structure["ContainerBytes"]
    assert structure["PacketHeaderAndBodyBytes"] == telemetry["PacketBodyBytes"] + telemetry["PacketHeaderBytes"]
    assert ledger["MethodId"] == METHOD and ledger["PacketBodyBytes"] == telemetry["PacketBodyBytes"]
    assert math.isfinite(ledger["FacePayloadByteEstimate"]) and ledger["FacePayloadByteEstimate"] >= 0
    assert ledger["AttributedFacePayloadBytes"] == math.floor(ledger["FacePayloadByteEstimate"])
    assert ledger["AttributedFacePayloadBytes"] + ledger["OutsidePayloadBytes"] == ledger["PacketBodyBytes"]
    def keyed(rows):
        result = {tuple(r[k] for k in ("Tile", "Component", "ResolutionLevel", "Subband")): r["PayloadBytes"] for r in rows}
        assert len(result) == len(rows)
        return result
    assert keyed(telemetry["Subbands"]) == keyed(ledger["Subbands"])
    assert sum(r["PayloadBytes"] for r in telemetry["Subbands"]) == ledger["PacketBodyBytes"]
    assert abs(sum(r["FacePayloadByteEstimate"] for r in ledger["Subbands"]) - ledger["FacePayloadByteEstimate"]) < 1e-7
    for band in ledger["Subbands"]:
        assert math.isfinite(band["FacePayloadByteEstimate"])
        assert 0 <= band["FacePayloadByteEstimate"] <= band["PayloadBytes"] + 1e-7
    assert codec.get("RequestedMinimumAttributedFacePayloadBytes") in (None, 0)
    assert codec.get("RequestedMinimumProtectedPayloadBytes") in (None, 0)


def decode(data, dimensions):
    assert features.check("jpg_2000"), "Install Pillow with OpenJPEG support."
    with Image.open(io.BytesIO(data)) as image:
        image.load()
        assert image.mode == "RGB" and image.size == dimensions
        return np.asarray(image).copy()


def gallery_markup(assets):
    lines = []
    for source_id in dict.fromkeys(r["Id"] for r in assets):
        rows = [r for r in assets if r["Id"] == source_id]
        first = rows[0]
        label = html.escape(first["Label"])
        lines += ["### " + first["Label"], "", "| Source | Minimum | Preferred |", "| --- | --- | --- |"]
        def picture(path, alt):
            return f'<a href="{path}"><img src="{path}" width="160" alt="{alt}"></a>'
        cells = [picture(first["SourcePreview"], label + " source")]
        for capacity in ("Minimum", "Preferred"):
            row = next(r for r in rows if r["SizeProfile"] == capacity)
            evidence = f'docs/samples/piv/{source_id}_{capacity.lower()}.json'
            if row["Status"] == "EncodedWithinBudget":
                ratio = row["EncodingEvidence"]["RegionalCompressionVerification"].get("MeasuredCompressionRatio")
                ratio_text = f"{ratio:.3f}:1 method estimate" if ratio is not None else "regional estimate pending"
                cells.append(picture(row["PreviewPath"], label + " " + capacity.lower()) +
                             f'<br>{row["FileSizeBytes"]:,} JP2 bytes<br>{ratio_text}<br>' +
                             f'[JP2]({row["ImagePath"]}) · [region]({row["RoiPreviewPath"]}) · [review]({evidence})')
            else:
                cells.append(f'Source review required<br>[decision]({evidence})')
        lines += ["| " + " | ".join(cells) + " |", ""]
    return "\n".join(lines)


def verify(output, update_readme=True):
    path = output / "manifest.json"
    manifest = json.loads(path.read_bytes())
    verified = []
    for row in manifest["Assets"]:
        checked_bytes(row["SourcePath"], row["SourceSha256"])
        if row["Status"] != "EncodedWithinBudget":
            continue
        data = checked_bytes(row["ImagePath"], row["ImageSha256"])
        assert len(data) == row["FileSizeBytes"] and len(data) <= row["MaximumJp2Bytes"]
        dimensions = (row["OutputDimensions"]["Width"], row["OutputDimensions"]["Height"])
        assert row["Recipe"] == {"StartResolutionLevel": 4, "CodeBlockSize": 64, "LumaUtility": 1.25,
                                 "FaceRegion": "LandmarkFace", "AllocationFloor": "None"}
        structure = inspect(data, dimensions)
        codec = row["EncodingEvidence"]["CodecEvidence"]
        regional = row["EncodingEvidence"]["RegionalCompressionVerification"]
        verify_ledger(codec, regional, structure, len(data))
        assert row.get("SourceColorEvidence") is not None
        with Image.open(io.BytesIO(Path(row["MaskPath"]).read_bytes())) as image:
            assert image.size == dimensions
            mask = np.asarray(image) != 0
        assert digest(mask.astype(np.uint8).tobytes()) == row["MaskSha256"] == codec["RoiMaskSha256"]
        assert int(mask.sum()) == regional["RoiPixelCount"]
        assert regional["UncompressedRegionBytes"] == 3 * int(mask.sum())
        pixels = decode(data, dimensions)
        Image.fromarray(pixels).save(row["PreviewPath"])
        interior = np.zeros_like(mask)
        interior[1:-1, 1:-1] = mask[1:-1, 1:-1] & mask[:-2, 1:-1] & mask[2:, 1:-1] & mask[1:-1, :-2] & mask[1:-1, 2:]
        overlay = pixels.copy()
        overlay[mask] = (pixels[mask] * 0.85 + np.array([0, 255, 0]) * 0.15).astype(np.uint8)
        overlay[mask & ~interior] = [0, 255, 80]
        Image.fromarray(overlay).save(row["RoiPreviewPath"])
        row["PreviewStatus"] = "IndependentlyDecoded"
        row["PreviewDecoder"] = "Pillow/OpenJPEG " + str(features.version("jpg_2000"))
        row["PreviewSha256"] = digest(Path(row["PreviewPath"]).read_bytes())
        row["IndependentVerification"] = {"Decoder": row["PreviewDecoder"], "Structure": structure,
            "RgbPixelSha256": digest(pixels.tobytes()), "ActualMaskHashVerified": True,
            "RegionalLedgerConservationVerified": True, "RegionalScope": "Versioned engineering estimate; enrollment review required"}
        Path(row["ImagePath"]).with_suffix(".json").write_text(json.dumps(row, indent=2) + "\n")
        verified.append({"Id": row["Id"], "SizeProfile": row["SizeProfile"], "Jp2Bytes": len(data),
                         "RegionalRatioEstimate": regional.get("MeasuredCompressionRatio")})
    manifest["IndependentVerification"] = verified
    path.write_text(json.dumps(manifest, indent=2) + "\n")
    if update_readme:
        readme = Path("README.md")
        text = readme.read_text()
        before, remainder = text.split("<!-- gallery:start -->", 1)
        _, after = remainder.split("<!-- gallery:end -->", 1)
        readme.write_text(before + "<!-- gallery:start -->\n" + gallery_markup(manifest["Assets"]) + "<!-- gallery:end -->" + after)
    return verified


if __name__ == "__main__":
    print(json.dumps(verify(Path(sys.argv[1] if len(sys.argv) > 1 else "docs/samples")), indent=2))

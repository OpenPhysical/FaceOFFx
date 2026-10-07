# FaceOFFx diagnostics validation

The release product prepares PIV images. The separate diagnostics executable supplies detection overlays, controlled sharpness variations and PIV sample assets.

## Inputs and controls

The tracked people corpus supports detection and overlay regressions. The local source-watermarked corpus supports portrait studies. Preserve original sources and record hashes; untagged inputs retain an automatic sRGB-assumption record.

```bash
faceoffx-diagnostics detect --corpus people --output artifacts/diagnostics/detect --verify
faceoffx-diagnostics sharpness blur --corpus people
faceoffx-diagnostics sharpness measure --input artifacts/diagnostics/sharpness/blur-progressions --json
faceoffx photo.jpg --filesize-target minimum --output portrait.jp2
```

Export fixed-recipe PIV JP2s through `docs samples` with an explicit input, named size target and a new or empty output directory. The development Pillow/OpenJPEG renderer creates previews. [Contributor instructions](CONTRIBUTING.md) contain both commands.

## Evidence to retain

- Source, crop, fixed mask and output hashes.
- Source/output dimensions, uniform transform, original crop support and anatomical measurement bases.
- Source color method, source ICC hash when present, and any automatic sRGB assumption.
- Complete JP2 size, committed packet/subband bytes, attribution convention and measured face ratio.
- Native decoded face/outer detail and controlled blur/geometry regression results.

A target succeeds after source-supported geometry and its complete JP2 byte ceiling pass. Regional compression, capture/anatomical review and complete signed-object lengths remain issuer review requirements. [Compression accounting](PIV-COMPRESSION-ACCOUNTING.md) defines the engineering regional estimate.

## Troubleshooting

Inspect detection overlays when a source lacks a usable single face. Inspect candidate traces when source margins or anatomical geometry prevent a crop. For capacity failures, retain the fixed mask and actual allocation evidence. A supported larger allowance or better native source framing can provide more encoding room.

Use [API](API.md) for the current library and [CLI architecture](CLI_ARCHITECTURE.md) for operator behavior.

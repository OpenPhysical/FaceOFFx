# FaceOFFx CLI architecture

The release CLI is a thin wrapper around `PivImageEncoder`.

```bash
faceoffx photo.jpg --filesize-target minimum --output portrait.jp2
faceoffx photo.jpg --filesize-target preferred --json
faceoffx photo.jpg --filesize-target 16000 --output portrait.jp2
```

## Controls

`--filesize-target` accepts minimum (11,820 bytes), preferred (22,000 bytes) or a positive
byte count. The balanced recipe is fixed. `--json` writes evidence to stdout; `--debug`
sends logs to stderr. Existing image/evidence paths require explicit `--overwrite`.
The default output is `INPUT.piv.jp2`, with evidence at `INPUT.piv.jp2.json`.

## Pipeline and persistence

`PivCommand` resolves an immutable `PivFileSizeTarget` and calls the library. Source color
and orientation are prepared before single-face detection and canonical landmarks.
The source-supported affine renderer constructs the fixed face mask, then the vendored
balanced encoder enforces the complete JP2 ceiling.

Success writes image bytes and evidence using staged files, individual atomic renames
and rollback. A process interruption may leave adjacent recovery files. Evidence carries
source/output hashes, target, geometry, mask coverage, color basis, regional accounting
and merged enrollment-review requirements.

## Development previews

The diagnostic tool exports JP2s and a manifest with `PendingIndependentDecode` previews.
`scripts/ReadmeGallery/render_diagnostics.py` creates hash-checked Pillow/OpenJPEG PNGs.
The renderer is development tooling. Current product usage is in [API](API.md) and
[README](../README.md).

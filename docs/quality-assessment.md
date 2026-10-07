# Quality assessment internals

FaceOFFx retains symmetry, sharpness and landmark geometry utilities for PIV diagnostics. The public product prepares a PIV JP2 and its verification evidence through `PivImageEncoder` or the release CLI.

```bash
faceoffx photo.jpg --filesize-target minimum --output portrait.jp2
faceoffx-diagnostics detect --corpus people --output artifacts/diagnostics/detect
faceoffx-diagnostics sharpness blur --corpus people
faceoffx-diagnostics sharpness measure --input artifacts/diagnostics/sharpness/blur-progressions --json
```

Symmetry uses facial left/right Gabor comparisons. Sharpness uses frequency-domain and regional measurements. Geometry uses detected landmark relationships. These engineering signals support review, and their configured percentage thresholds express implementation policy.

The production geometry evidence separately records source support, native scale and anatomical measurement bases. Region coverage remains estimated until the selected feature boundary is reviewed. The codec ledger records committed bytes and the measured regional ratio.

Backdrop diagnostics require an explicit retained-head mask/envelope and chin protection. Pixel-channel statistics and codec subband telemetry have distinct measurement bases. Low-texture border regions carry candidate status, and ambiguous regions remain unknown.

See [quality architecture](QUALITY_SYSTEM.md) for internal models, [API](API.md) for encoding, and [compression accounting](PIV-COMPRESSION-ACCOUNTING.md) for the PIV regional method.

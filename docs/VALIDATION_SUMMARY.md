# FaceOFFx validation summary

FaceOFFx verifies PIV image preparation through domain, infrastructure, public-facade and CLI behavior tests. Current release gates are the solution build and tests; historical versioned release records retain their original counts.

## Covered behavior

Canonical geometry connects coarse detection, a normalized chip, fine source-space landmarks and one-pass rendering. Tests exercise native scale, rotated source corners, crop margins and explicit anatomical measurement status.

Fixed region tests cover all detected features, localization padding, mask ownership and budget independence. Encoder tests verify complete JP2 caps, committed packet/component/subband reconciliation and the versioned shared-payload review ledger.

Source-color tests cover automatic untagged sRGB assumptions, EXIF-declared and numerically verified sRGB, bounded matrix/TRC conversion, independent Little CMS vectors and failure preservation. Public/CLI tests cover lifecycle, cancellation, structured output, staged paired persistence and source/output separation.

## Diagnostics

```bash
faceoffx-diagnostics detect --corpus people --output artifacts/diagnostics/detect --verify
faceoffx-diagnostics sharpness blur --corpus people
faceoffx-diagnostics sharpness measure --input artifacts/diagnostics/sharpness/blur-progressions --json
```

Use the production PIV encoder for final crop and byte evidence. Frozen comparison controls preserve their source, crop and mask hashes, allocation settings and attribution version.

## Issuance evidence

Automated results establish the configured image cap and source-supported geometry, and record regional estimates under the FaceOFFx convention. The issuer completes regional-method acceptance, capture/optical-resolution, true anatomical boundary, pose/expression, uniform-background/illumination and color-provenance review, then verifies and signs the complete object.

See [API](API.md), [compression accounting](PIV-COMPRESSION-ACCOUNTING.md), [diagnostics validation](QUALITY_VALIDATION_PLAN.md) and [contributor instructions](CONTRIBUTING.md).

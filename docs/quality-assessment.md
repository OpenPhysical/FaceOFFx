# Quality Assessment Internals

## Overview

FaceOFFx still contains internal quality-analysis components for symmetry, sharpness, and geometry, but they are no longer exposed as release CLI commands. The shipped product surface is document-first, and diagnostics now live in `faceoffx-diagnostics`.

Use these internals in two places:

- document workflows, where they support input suitability or advisory findings
- diagnostics runs, where you want batch measurements and visual inspection across the people corpus

## Current Operator Path

For visual or batch inspection, use the diagnostics CLI:

```bash
faceoffx-diagnostics detect --corpus people --output artifacts/diagnostics/detect
faceoffx-diagnostics detect --corpus people --output artifacts/diagnostics/detect
faceoffx-diagnostics sharpness blur --corpus people
faceoffx-diagnostics sharpness measure --input artifacts/diagnostics/sharpness/blur-progressions --json
```

## Current Product Path

For actual issuance workflows, use the document commands:

```bash
faceoffx piv photo.jpg
faceoffx us-passport photo.jpg --variant digital
faceoffx canada-proof-of-citizenship photo.jpg --variant print
```

## Internal Metrics

The current internal analyzers still cover:

- facial symmetry via Gabor-based left/right comparison
- sharpness via frequency-domain analysis and regional scoring
- geometry via landmark-derived measurements

These are useful engineering signals, but they are not presented as standalone compliance commands anymore.

## Notes

- The old `faceoffx quality` and `faceoffx process` command examples in earlier versions of this repository are obsolete.
- When you need engineering visibility, prefer `faceoffx-diagnostics`.
- When you need a user-facing result, prefer the document commands and their provenance output.

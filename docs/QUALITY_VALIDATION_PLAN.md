# FaceOFFx Diagnostics Validation Plan

## Overview

This document outlines the current validation strategy for FaceOFFx diagnostics and document workflows. The release CLI is document-only; engineering validation runs through `faceoffx-diagnostics`, the canonical people corpus, and behavior-heavy core/infrastructure tests.

## Validation Components

### 1. Corpus Setup

```bash
ls tests/test-images/people
```

The tracked people corpus is the shared source for:
- detection verification
- overlay inspection
- document crop rendering
- representative workflow tests

### 2. Diagnostics CLI Testing

```bash
faceoffx-diagnostics detect --corpus people --output artifacts/diagnostics/detect --verify
faceoffx-diagnostics detect --corpus people --output artifacts/diagnostics/detect
faceoffx-diagnostics crop --corpus people --profile canada-passport --variant print --output artifacts/diagnostics/crop
faceoffx-diagnostics sharpness blur --corpus people
faceoffx-diagnostics sharpness measure --input artifacts/diagnostics/sharpness/blur-progressions --json
```

### 3. Release Workflow Testing

```bash
faceoffx piv input.jpg
faceoffx us-passport input.jpg --variant digital
faceoffx canada-proof-of-citizenship input.jpg --variant print
```

These commands should be covered mostly through infrastructure/core behavior tests and only lightly through CLI smoke tests.

### 4. Validation Focus

- corpus manifest verification
- reverse-projected overlays on originals
- crop parity between diagnostics and release workflows
- document-specific render and validation behavior
- provenance separation between blocking, advisory, and manual checks

## Quick Start Guide

```bash
# 1. Verify detection and landmarks
faceoffx-diagnostics detect --corpus people --output artifacts/diagnostics/detect --verify

# 2. Generate overlays for manual review
faceoffx-diagnostics detect --corpus people --output artifacts/diagnostics/detect

# 3. Generate profile crops
faceoffx-diagnostics crop --corpus people --profile canada-passport --variant print --output artifacts/diagnostics/crop

# 4. Run the release workflow on a real file
faceoffx us-passport tests/test-images/people/generic-guy/source.jpg --variant digital
```

## Troubleshooting

### Common Issues

1. **No face detected**:
   - verify the corpus expectation for that subject
   - inspect the overlay output for framing or pose issues

2. **Unexpected crop**:
   - compare `faceoffx-diagnostics crop` output with the release workflow output
   - inspect the generated manifest and overlay guides

## Conclusion

This validation plan keeps the public product simple while preserving deep engineering visibility through the separate diagnostics tool.

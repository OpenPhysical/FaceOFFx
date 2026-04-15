# FaceOFFx CLI Architecture

## Overview

FaceOFFx now exposes a document-first CLI. The primary user-facing commands are:

- `piv`
- `us-passport`
- `us-permanent-resident`
- `canada-passport`
- `canada-permanent-resident`
- `canada-citizenship-grant`
- `canada-proof-of-citizenship`
- `documents`

Each document command runs one complete workflow: input analysis, portrait rendering, output validation, and provenance writing.

## Public Model

The public model is intentionally small:

- a document command selects the product being issued
- an optional `--variant` selects a named alternate output set
- `--output-dir` controls where artifacts and provenance are written
- `--json` emits a machine-readable job summary
- `--explain` prints the cited clauses behind the workflow

Examples:

```bash
faceoffx piv photo.jpg
faceoffx piv photo.jpg --variant digital
faceoffx us-passport photo.jpg --variant digital
faceoffx canada-proof-of-citizenship photo.jpg --variant digital --json
faceoffx documents
```

## Internal Shape

The current implementation keeps the workflow surface narrow instead of building a general planner:

- `DocumentCatalog` defines shipped documents, variants, citations, and typed workflow metadata.
- `DocumentJobRunner` executes one job end to end.
- `DocumentWorkflowFamily.Piv` routes to the PIV-specific processing path.
- `DocumentWorkflowFamily.PassportStyle` routes to the passport-style render/validate path.

`DeliverableKind` is typed so rendering and validation do not depend on string keys.

## Validation Model

Validation is split into two stages:

- input checks decide whether the source capture is usable for the requested document
- output checks run on the actual rendered artifact

Blocking automated checks are citation-backed. Manual-only requirements remain visible in provenance and human output, but they do not flip the automated pass/fail result.

Raw input IPD is advisory only. It is not used as a standalone blocker for source captures.

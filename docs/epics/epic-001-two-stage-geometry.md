# Epic-001: Two-Stage Geometry Pipeline

## Summary
- Replace the inference-heavy render path with one canonical geometry pipeline:
  1. coarse face detection on the original image
  2. normalized coarse chip generation using RetinaFace 5-point landmarks
  3. one fine 68-point landmark solve on that chip
  4. back-projection of those fine landmarks into the original image
  5. deterministic document rendering and diagnostics from the original-space fine landmarks
- After the fine solve, the system becomes geometry only. No re-detecting or re-extracting landmarks during rendering or diagnostics.

## Goals
- Make document rendering and diagnostics agree on one source of truth.
- Preserve landmark geometry through every transform.
- Keep command logic sparse; commands should only parse input, call a service, and render output.
- Reduce duplicated crop/rotate/projection math across PIV, passport-style documents, and diagnostics.

## Non-Goals
- Backward compatibility with the old expert CLI commands.
- A new generic workflow framework.
- Hiding advisory or failed render states behind approximate overlays.

## Architecture Decisions
- The canonical landmark representation is the fine 68-point set in original-image coordinates.
- Coarse detection uses RetinaFace and must include 5-point landmarks.
- Fine extraction runs only on the normalized chip, never on the original full image.
- Render and diagnostics code must consume the same typed geometry records and affine transforms.
- Nulls are replaced with explicit results and `Maybe<T>` where absence is valid.

## Burn-Down

### Phase 1: Canonical Geometry
- [x] Add canonical geometry records for coarse detection, chip landmarks, and original-space landmarks.
- [x] Normalize the face chip with a 5-point similarity transform.
- [x] Run one fine landmark solve on the normalized chip.
- [x] Back-project fine landmarks into original-image coordinates.
- [x] Expose canonical geometry through a public service surface instead of friend assemblies.
- [ ] Add focused tests for chip normalization and source-space back-projection.

### Phase 2: Production Render Migration
- [x] Migrate passport-style rendering to canonical original-space landmarks.
- [x] Migrate PIV landmark processing to canonical original-space landmarks.
- [x] Cut document issuance and diagnostics over to public render/geometry services.
- [ ] Remove remaining legacy geometry helpers that duplicate the new render core.
- [ ] Ensure document workflows do not re-detect or re-extract landmarks after the fine stage.

### Phase 3: Diagnostics Migration
- [x] Carry coarse 5-point landmarks into diagnostics overlays.
- [x] Carry canonical fine landmarks into diagnostics overlays.
- [x] Reuse canonical geometry when building passport-style overlays.
- [x] Reuse canonical geometry when building PIV overlays.
- [ ] Add regression tests for representative hard cases like `person-01` and `bush`.

### Phase 4: Cleanup
- [x] Remove `InternalsVisibleTo` from runtime assemblies in favor of public APIs.
- [ ] Remove superseded diagnostics-only projection helpers.
- [ ] Audit remaining services for duplicated rotate/crop/resize logic.
- [ ] Tighten tests around transform composition and rendered output bounds.
- [ ] Update architecture docs after the new pipeline is fully settled.

## Acceptance Criteria
- A successful workflow run performs one coarse detect and one fine landmark solve.
- Diagnostics overlays show:
  - coarse 5-point geometry on the original image
  - fine 68-point geometry on the original image
  - document output polygons only when a real render succeeds
- PIV and passport-style rendering use the same canonical fine landmark model.
- No document or diagnostics path performs post-fine landmark extraction.
- Command handlers remain thin and do not own crop or transform logic.

## Done Means
- Corpus-wide diagnostics review is visually coherent across the people corpus.
- Production rendering and diagnostics agree on the same geometry for a given image/profile.
- The remaining geometry code is centralized enough that fixes land in one place instead of three.

# FaceOFFx implementation guide

FaceOFFx's product path is PIV source file or bytes to bounded JPEG 2000 plus verification evidence. [API](API.md) and the [README](../README.md) document caller usage.

## Ownership and pipeline

Keep domain rules in `FaceOFFx.Core`, ImageSharp/ONNX/codec integration in `FaceOFFx.Infrastructure`, and public lifecycle/CLI wiring in `FaceOFFx` and `FaceOFFx.Cli`.

`PivImageEncoder` owns reusable ONNX services and serializes calls per instance. `ProfileEncoder` prepares source color and orientation on an owned image. `FaceGeometryPipeline` performs coarse detection, a normalized face chip and one fine landmark solve, then back-projects landmarks to source coordinates.

`PortraitPlanSolver` evaluates fixed framing candidates with native-resolution and source-corner support checks. `PortraitRenderer` applies one uniform affine transform. `AnatomicalFaceRoi` constructs the fixed immutable landmark mask before encoding. `EncodingPlanSolver` requests an exact JP2 cap from the vendored balanced encoder and records shared-payload measurement for regional review.

## Preserve invariants

Keep the source immutable at public boundaries. Color failures preserve samples and metadata; unsupported embedded profiles receive a clear conversion requirement. Source-space geometry and rendered landmarks use the same transform.

Define the face region independently of capacity. Preserve all landmark features and fixed padding. Framing and mask failures remain explicit. Encoding checks reconcile complete output bytes and emitted packet/subband rows; the regional ratio remains review evidence under its recorded convention.

Keep engineering quality scoring separate from anatomical, capture and credential evidence. Symmetry, sharpness and geometry assessors provide diagnostic signals and caller-defined quality thresholds. The issuer supplies acquisition/pose review and validates actual FAC/CBEFF/signature serialization.

## Verification

Use focused unit tests while changing domain rules, service integration or CLI persistence. Finish with the complete solution gate:

```bash
dotnet build --configuration Release
dotnet test --configuration Release
```

Behavior tests cover geometry boundaries and rotation, detached masks, capacity-independent face regions, exact-file limits, regional reconciliation, source-color assumptions/conversion, cancellation, disposal, and paired output preservation. Development preview verification uses independent OpenJPEG decoding of hash-checked JP2 bytes.

Use frozen source/crop/mask controls for allocator comparisons. Record method versions, hashes, source transformations and actual decoding evidence with each comparison. [Compression accounting](PIV-COMPRESSION-ACCOUNTING.md) specifies the implemented regional convention and its justification.

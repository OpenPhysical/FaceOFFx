# FaceOFFx 4.0 PIV API

`PivImageEncoder` prepares a source-supported portrait using the fixed balanced encoder.
The library targets .NET 8 and is consumable by .NET 8, 9 and 10 applications.

## Encode

```csharp
using FaceOFFx;

using var encoder = new PivImageEncoder();
var result = await encoder.EncodeFileAsync("photo.jpg", PivFileSizeTarget.Minimum);
if (result.IsFailure)
{
    Console.Error.WriteLine(result.Error.Message);
    return;
}
await File.WriteAllBytesAsync("portrait.jp2", result.Value.ImageData);
foreach (var requirement in result.Value.VerificationRequirements)
    Console.WriteLine(requirement);
```

`EncodeAsync(byte[], PivFileSizeTarget? = null, CancellationToken = default)` and
`EncodeFileAsync(string, PivFileSizeTarget? = null, CancellationToken = default)` return
`Result<PivEncodingResult, PipelineError>`. An omitted target selects `Minimum`.
Expected failures carry an actionable error; cancellation propagates. Reuse and dispose
the encoder, which owns its ONNX services and serializes calls per instance.

## Immutable size target

`PivFileSizeTarget.Minimum.MaximumBytes` is **11,820**; `Preferred.MaximumBytes` is
**22,000**. `FromBytes(int)` creates a custom positive JP2 ceiling with room for the
required container arithmetic within the integer range.

`RequiredBiometricValueBytes` is `MaximumBytes + 884`, covering FAC46, CBEFF88 and
signature allowance750. `RequiredObjectBytes` also includes the size-dependent BER
wrapper, 6 bytes for both named targets and 7 or 8 bytes for larger custom targets.
Issuer serialization verifies actual lengths and credential capacity.

## Fixed recipe and evidence

Encoding uses start level **4**, **64×64** blocks, all-luma utility **1.25**, one tile,
one layer, ICT and irreversible 9/7. The immutable landmark face mask is constructed
before balanced byte allocation. The complete JP2 ceiling is enforced.

`PivEncodingResult` exposes get-only `ImageData`, `MimeType`, `OutputDimensions`,
`OutputLandmarks`, `RotationDegrees`, `FaceConfidence`, `Encoding`, `CandidateTraces`,
`GeometryEvidence`, `RoiCoverage`, `SourceColorEvidence`, `FileSizeTarget` and merged
`VerificationRequirements`.

The default **480×640** frame uses a uniform source-supported transform. Ear attachment
and crown evidence retain their estimated or verified basis. Regional compression is
reported under the versioned V3 engineering convention, with packet/subband conservation
and explicit regional-review requirements. [Compression accounting](PIV-COMPRESSION-ACCOUNTING.md)
defines the method and assumptions.

## Source color and enrollment

Embedded sRGB and EXIF sRGB are recorded. Supported v2/v4 RGB matrix/TRC profiles are
converted to sRGB with relative-colorimetric conversion and XYZ D50 PCS. Unsupported
or malformed profiles request a supported color-managed export before encoding.
Untagged inputs use **AssumedSrgb** evidence and retain source-history review.

Sources are bounded to **64 million pixels**. Conversion preserves source resolution;
the pipeline owns its working pixels and normalizes EXIF orientation before geometry.

The issuer completes optical acquisition, anatomical coverage, pose/expression,
uniform-background/illumination, color-provenance, regional compression and recognition
review, then packages and signs the complete facial-image object.

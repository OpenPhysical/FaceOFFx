using JetBrains.Annotations;
using System.Text.Json.Serialization;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>Complete-output allowance for the fixed balanced JPEG 2000 recipe.</summary>
[PublicAPI]
public sealed record Jpeg2000EncodingOptions
{
    /// <summary>Maximum complete JP2 length, including boxes and codestream headers.</summary>
    public required int MaximumOutputBytes { get; init; }
}

/// <summary>JPEG 2000 wavelet band orientation.</summary>
[PublicAPI]
public enum Jpeg2000WaveletSubband
{
    /// <summary>Low-pass horizontally and vertically.</summary>
    LL = 0,
    /// <summary>High-pass horizontally, low-pass vertically.</summary>
    HL = 1,
    /// <summary>Low-pass horizontally, high-pass vertically.</summary>
    LH = 2,
    /// <summary>High-pass horizontally and vertically.</summary>
    HH = 3
}

/// <summary>Measured JPEG 2000 output and allocation evidence.</summary>
[PublicAPI]
public sealed record Jpeg2000EncodingResult(
    [property: JsonIgnore] byte[] Data,
    int CodestreamBytes,
    int ContainerBytes,
    long RoiPixelCount,
    long? ProtectedPayloadBytes,
    string ProtectedPayloadAccounting,
    int AllocationCandidateCount,
    int AllocationSimulationCount)
{
    /// <summary>Disjoint emitted payload categories and complete-output reconciliation.</summary>
    public Jpeg2000PayloadTelemetry? PayloadTelemetry { get; init; }
    /// <summary>Conserved face and outside payload under the versioned regional measurement convention.</summary>
    public Jpeg2000SharedPayloadAttribution? SharedPayloadAttribution { get; init; }
    /// <summary>SHA-256 of the complete row-major binary face mask used for encoding.</summary>
    public string? RoiMaskSha256 { get; init; }
}

/// <summary>Card-image allowance after reserving facial-record, CBEFF and issuer-signature bytes.</summary>
[PublicAPI]
public sealed record PivCardImageBudget
{
    /// <summary>Maximum 0xBC value length, including CBEFF and signature.</summary>
    public int MaximumBiometricValueBytes { get; }
    /// <summary>Maximum complete card data-object length, including mandatory tags.</summary>
    public int MaximumObjectBytes { get; }
    /// <summary>Reserved INCITS 385 record metadata length.</summary>
    public int FacialRecordBytes { get; }
    /// <summary>Reserved CBEFF metadata length.</summary>
    public int CbeffBytes { get; }
    /// <summary>Issuer-configured maximum serialized signature length.</summary>
    public int MaximumSignatureBytes { get; }
    /// <summary>Reserved outer TLV and mandatory FE-tag length.</summary>
    public int ObjectTagBytes { get; }
    /// <summary>Image allowance after every configured reservation.</summary>
    public int MaximumJpeg2000Bytes { get; }

    /// <summary>The signature reservation is an issuer configuration value. Validate the complete signed object after wrapping.</summary>
    public PivCardImageBudget(
        int maximumBiometricValueBytes = 12_704,
        int maximumObjectBytes = 12_710,
        int maximumSignatureBytes = 750,
        int facialRecordBytes = 46,
        int cbeffBytes = 88,
        int objectTagBytes = 6)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBiometricValueBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumObjectBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSignatureBytes);
        ArgumentOutOfRangeException.ThrowIfLessThan(facialRecordBytes, 46);
        ArgumentOutOfRangeException.ThrowIfLessThan(cbeffBytes, 88);
        ArgumentOutOfRangeException.ThrowIfLessThan(objectTagBytes, 6);
        var valueAllowance = Math.Min((long)maximumBiometricValueBytes, (long)maximumObjectBytes - objectTagBytes);
        var imageAllowance = valueAllowance - facialRecordBytes - cbeffBytes - maximumSignatureBytes;
        if (imageAllowance <= 0)
        {
            throw new ArgumentException("The card capacity must cover the facial record, CBEFF, signature and JPEG 2000 data.");
        }

        MaximumBiometricValueBytes = maximumBiometricValueBytes;
        MaximumObjectBytes = maximumObjectBytes;
        FacialRecordBytes = facialRecordBytes;
        CbeffBytes = cbeffBytes;
        MaximumSignatureBytes = maximumSignatureBytes;
        ObjectTagBytes = objectTagBytes;
        MaximumJpeg2000Bytes = checked((int)imageAllowance);
    }

    /// <summary>Checks actual serialized lengths, including any metadata that exceeds the reservation.</summary>
    public bool FitsSerializedObject(int biometricValueBytes, int objectBytes) =>
        biometricValueBytes > 0 && objectBytes >= (long)biometricValueBytes + ObjectTagBytes &&
        biometricValueBytes <= MaximumBiometricValueBytes && objectBytes <= MaximumObjectBytes;
}

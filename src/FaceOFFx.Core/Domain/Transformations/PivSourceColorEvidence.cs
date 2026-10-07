namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>How decoded source samples acquired an sRGB interpretation.</summary>
public enum PivSourceColorStatus
{
    /// <summary>The embedded matrix and curves describe sRGB.</summary>
    EmbeddedSrgb,
    /// <summary>The source EXIF ColorSpace field declares sRGB.</summary>
    ExifDeclaredSrgb,
    /// <summary>An embedded RGB matrix-shaper profile was converted to sRGB.</summary>
    ConvertedMatrixRgb,
    /// <summary>Untagged source samples are interpreted as sRGB by the fixed pipeline.</summary>
    AssumedSrgb
}

/// <summary>Immutable source-color provenance, separate from acquisition and anatomical verification.</summary>
public sealed record PivSourceColorEvidence
{
    /// <summary>Recorded basis for interpreting the prepared RGB samples.</summary>
    public PivSourceColorStatus Status { get; }
    /// <summary>Color preparation algorithm or declaration.</summary>
    public string Method => Status switch
    {
        PivSourceColorStatus.EmbeddedSrgb => "icc-matrix-trc-srgb-identity-v1",
        PivSourceColorStatus.ExifDeclaredSrgb => "exif-colorspace-1",
        PivSourceColorStatus.AssumedSrgb => "untagged-srgb-assumption-v1",
        _ => "icc-matrix-trc-d50-to-srgb-relative-colorimetric-v1"
    };
    /// <summary>SHA-256 of the embedded source ICC bytes, when present.</summary>
    public string? EmbeddedProfileSha256 { get; }
    /// <summary>Whether preparation converted RGB sample values.</summary>
    public bool PixelsConverted => Status == PivSourceColorStatus.ConvertedMatrixRgb;
    /// <summary>Acquisition evidence that remains for the issuer to verify.</summary>
    public IReadOnlyList<string> VerificationRequirements { get; }

    /// <summary>Creates a validated snapshot of source color preparation.</summary>
    public PivSourceColorEvidence(PivSourceColorStatus status, string? embeddedProfileSha256 = null)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        var embedded = status is PivSourceColorStatus.EmbeddedSrgb or PivSourceColorStatus.ConvertedMatrixRgb;
        if (embedded && (embeddedProfileSha256 is null || embeddedProfileSha256.Length != 64 ||
                         embeddedProfileSha256.Any(value => !Uri.IsHexDigit(value))))
            throw new ArgumentException("Embedded-profile evidence requires its SHA-256 digest.", nameof(embeddedProfileSha256));
        if (!embedded && embeddedProfileSha256 is not null)
            throw new ArgumentException("Declaration evidence applies to sources without an embedded ICC profile.", nameof(embeddedProfileSha256));
        Status = status;
        EmbeddedProfileSha256 = embeddedProfileSha256?.ToLowerInvariant();
        VerificationRequirements = Array.AsReadOnly(new[]
        {
            status == PivSourceColorStatus.AssumedSrgb
                ? "Verify the untagged-source sRGB assumption against the original capture workflow and color samples."
                : status == PivSourceColorStatus.ExifDeclaredSrgb
                ? "Verify that the source sRGB declaration agrees with the capture workflow and original samples."
                : "Verify source-profile provenance and the capture workflow's color accuracy."
        });
    }
}

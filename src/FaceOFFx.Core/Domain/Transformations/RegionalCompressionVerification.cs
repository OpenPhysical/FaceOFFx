using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>Verification state of a spatial regional-compression measurement.</summary>
[PublicAPI]
public enum RegionalCompressionVerificationStatus
{
    /// <summary>Shared wavelet payload attribution remains to be measured.</summary>
    PendingSharedBandAttribution,
    /// <summary>Measured face payload meets the regional byte requirement under the recorded method.</summary>
    MeetsLimit,
    /// <summary>Measured face payload falls below the regional byte requirement under the recorded method.</summary>
    ExceedsLimit
}

/// <summary>RGB regional byte arithmetic, independent of protected-phase allocation counters.</summary>
[PublicAPI]
public sealed record RegionalCompressionVerification
{
    /// <summary>Spatial attribution state, preserved independently of codec quota results.</summary>
    public RegionalCompressionVerificationStatus Status => Attribution is null
        ? RegionalCompressionVerificationStatus.PendingSharedBandAttribution
        : AttributedPayloadBytes >= MinimumAttributedPayloadBytes
            ? RegionalCompressionVerificationStatus.MeetsLimit
            : RegionalCompressionVerificationStatus.ExceedsLimit;
    /// <summary>Whether the measured regional ratio meets the maximum under the recorded method.</summary>
    public bool MeetsLimit => Status == RegionalCompressionVerificationStatus.MeetsLimit;
    /// <summary>Maximum compression ratio for the on-card PIV face region.</summary>
    public int MaximumCompressionRatio => 24;
    /// <summary>Number of pixels in the supplied face-region mask.</summary>
    public long RoiPixelCount { get; }
    /// <summary>Uncompressed region length for three eight-bit RGB components.</summary>
    public long UncompressedRegionBytes { get; }
    /// <summary>Minimum attributed payload length implied by the maximum ratio.</summary>
    public long MinimumAttributedPayloadBytes { get; }
    /// <summary>Spatially attributed bytes become available through regional measurement.</summary>
    public long? AttributedPayloadBytes => Attribution?.AttributedFacePayloadBytes;
    /// <summary>Measured ratio using the attributed regional payload.</summary>
    public double? MeasuredCompressionRatio => AttributedPayloadBytes is > 0
        ? (double)UncompressedRegionBytes / AttributedPayloadBytes.Value
        : null;
    /// <summary>Versioned regional measurement convention.</summary>
    public string? AttributionMethod => Attribution?.MethodId;
    /// <summary>Immutable shared-payload ledger used for the measurement.</summary>
    public Jpeg2000SharedPayloadAttribution? Attribution { get; }
    /// <summary>Regional verification requirement.</summary>
    public string Statement => Attribution is null
        ? "Regional 24:1 verification pending: document accepted attribution of shared wavelet and packet bytes to the face region."
        : $"Face payload {AttributedPayloadBytes} bytes, required {MinimumAttributedPayloadBytes} bytes at 24:1, measured by {AttributionMethod}.";
    /// <summary>Interpretation of the independent encoder allocation counter.</summary>
    public string ProtectedPayloadInterpretation => "Protected-phase payload is a codec allocation diagnostic.";

    private RegionalCompressionVerification(long roiPixelCount, Jpeg2000SharedPayloadAttribution? attribution = null)
    {
        RoiPixelCount = roiPixelCount;
        UncompressedRegionBytes = checked(3 * roiPixelCount);
        MinimumAttributedPayloadBytes = UncompressedRegionBytes / MaximumCompressionRatio +
            (UncompressedRegionBytes % MaximumCompressionRatio == 0 ? 0 : 1);
        Attribution = attribution;
    }

    /// <summary>Records face-region arithmetic before shared-payload measurement.</summary>
    public static RegionalCompressionVerification PendingPiv(long roiPixelCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roiPixelCount);
        return new RegionalCompressionVerification(roiPixelCount);
    }

    /// <summary>Measures regional compression after mask and actual-payload reconciliation.</summary>
    public static RegionalCompressionVerification MeasuredPiv(
        long roiPixelCount,
        Jpeg2000SharedPayloadAttribution attribution,
        Jpeg2000PayloadTelemetry telemetry)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roiPixelCount);
        ArgumentNullException.ThrowIfNull(attribution);
        ArgumentNullException.ThrowIfNull(telemetry);
        if (attribution.RoiPixelCount != roiPixelCount || attribution.PacketBodyBytes != telemetry.PacketBodyBytes)
        {
            throw new ArgumentException("Regional measurement must cover the complete face mask and committed payload.", nameof(attribution));
        }
        var actualBands = telemetry.Subbands.ToDictionary(
            row => (row.Tile, row.Component, row.ResolutionLevel, row.Subband), row => row.PayloadBytes);
        if (attribution.Subbands.Count != actualBands.Count || attribution.Subbands.Any(row =>
            !actualBands.TryGetValue((row.Tile, row.Component, row.ResolutionLevel, row.Subband), out var bytes) ||
            bytes != row.PayloadBytes))
        {
            throw new ArgumentException("Regional rows must match every committed wavelet-band payload.", nameof(attribution));
        }
        return new RegionalCompressionVerification(roiPixelCount, attribution);
    }
}

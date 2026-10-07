using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using SixLabors.ImageSharp;

namespace FaceOFFx.Core.Domain.Quality;

/// <summary>Spatial evidence used to protect facial and retained portrait content.</summary>
public enum PortraitRegionLabel
{
    /// <summary>Scene content awaiting classification.</summary>
    Unknown,
    /// <summary>The caller-supplied facial region, with anatomical coverage reviewed separately.</summary>
    Face,
    /// <summary>Caller-protected head content, shoulders, and the default below-chin area.</summary>
    RetainedForeground,
    /// <summary>A low-texture, border-connected backdrop estimate.</summary>
    BackdropCandidate
}

/// <summary>The amount of semantic backdrop evidence available from pixel statistics.</summary>
public enum PortraitBackdropPresenceStatus
{
    /// <summary>The source requires additional scene-boundary evidence.</summary>
    Undetermined,
    /// <summary>A candidate region satisfies the configured spatial and pixel-statistic checks.</summary>
    EstimatedCandidate
}

/// <summary>Caller-reviewed foreground boundaries used by backdrop diagnostics.</summary>
public sealed record PortraitForegroundProtection(float ChinY)
{
    /// <summary>Caller-reviewed full head, hair, forehead, and ear protection in source-image coordinates.</summary>
    public FacialRoiMask? RetainedHeadMask { get; init; }
    /// <summary>An alternative complete head envelope in source-image coordinates.</summary>
    public Rectangle? RetainedHeadEnvelope { get; init; }
    /// <summary>Additional caller-reviewed shoulder or clothing protection.</summary>
    public FacialRoiMask? ShoulderMask { get; init; }
}

/// <summary>Explicit pixel-statistic thresholds for conservative backdrop candidates.</summary>
public sealed record PortraitBackdropOptions
{
    /// <summary>Maximum average neighbor difference in each decoded 8-bit RGB channel.</summary>
    public double MaximumNeighborGradient { get; init; } = 8;
    /// <summary>Maximum channel variance within the native 3-by-3 neighborhood.</summary>
    public double MaximumLocalVariance { get; init; } = 64;
    /// <summary>Maximum channel distance from the robust border color.</summary>
    public double MaximumColorDistance { get; init; } = 24;
    /// <summary>Native channel difference used for the reported edge-density statistic.</summary>
    public double EdgeDifferenceThreshold { get; init; } = 16;
    /// <summary>Minimum accepted candidate area in pixels.</summary>
    public int MinimumCandidatePixels { get; init; } = 16;
    /// <summary>Minimum accepted candidate fraction of the image.</summary>
    public double MinimumCandidateFraction { get; init; } = 0.01;
    /// <summary>Per-call analysis memory guard, up to 16,777,216 pixels.</summary>
    public int MaximumPixelCount { get; init; } = 16_777_216;
}

/// <summary>Statistics for one decoded RGB pixel channel, using population variance and 16 entropy bins.</summary>
public sealed record RgbPixelComponentStatistics(double Mean, double Variance, double QuantizedEntropyBits,
    double MeanNeighborGradient, double EdgeDensity);

/// <summary>Whether a channel correlation has enough variation for a defined coefficient.</summary>
public enum PixelCorrelationStatus
{
    /// <summary>Both channels have positive variance and sufficient samples.</summary>
    Defined,
    /// <summary>Constant or tiny samples require separate evidence.</summary>
    Undefined
}

/// <summary>A finite channel-correlation coefficient; Undefined carries the placeholder value zero.</summary>
public sealed record RgbPixelCorrelation(PixelCorrelationStatus Status, double Value);

/// <summary>Decoded RGB pixel statistics for one spatial label.</summary>
public sealed record PortraitRegionPixelStatistics(PortraitRegionLabel Label, long PixelCount,
    RgbPixelComponentStatistics Red, RgbPixelComponentStatistics Green, RgbPixelComponentStatistics Blue,
    RgbPixelCorrelation RedGreenCorrelation, RgbPixelCorrelation RedBlueCorrelation,
    RgbPixelCorrelation GreenBlueCorrelation);

/// <summary>Owned scene-label snapshots and diagnostics for a source-preserving portrait analysis.</summary>
public sealed class PortraitBackdropAnalysis
{
    private readonly byte[] _labels;

    /// <summary>Creates an owned analysis snapshot and validates its finite summary values.</summary>
    public PortraitBackdropAnalysis(ImageDimensions dimensions, ReadOnlySpan<byte> labels,
        PortraitBackdropPresenceStatus presence, IReadOnlyList<PortraitRegionPixelStatistics> statistics,
        double candidateColorConsistency)
    {
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(statistics);
        if (dimensions.Width <= 0 || dimensions.Height <= 0 ||
            (long)dimensions.Width * dimensions.Height != labels.Length)
            throw new ArgumentException("Dimensions must match the label snapshot.", nameof(dimensions));
        if (!Enum.IsDefined(presence) || !double.IsFinite(candidateColorConsistency) ||
            candidateColorConsistency is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(candidateColorConsistency));
        foreach (var label in labels)
            if (label > (byte)PortraitRegionLabel.BackdropCandidate)
                throw new ArgumentException("The snapshot requires defined portrait labels.", nameof(labels));
        if (statistics.Count != 4 || statistics.Any(region => region is null || !Enum.IsDefined(region.Label) ||
                region.PixelCount < 0 || !Valid(region.Red) || !Valid(region.Green) || !Valid(region.Blue) ||
                !Valid(region.RedGreenCorrelation) || !Valid(region.RedBlueCorrelation) || !Valid(region.GreenBlueCorrelation)) ||
            statistics.Select(region => region.Label).Distinct().Count() != 4)
            throw new ArgumentException("Statistics require four defined labels and finite RGB measurements.", nameof(statistics));
        Dimensions = dimensions;
        _labels = labels.ToArray();
        foreach (var region in statistics)
            if (region.PixelCount != _labels.LongCount(label => label == (byte)region.Label))
                throw new ArgumentException("Statistic sample counts must match the owned labels.", nameof(statistics));
        CandidatePixelCount = _labels.LongCount(label => label == (byte)PortraitRegionLabel.BackdropCandidate);
        if (presence == PortraitBackdropPresenceStatus.EstimatedCandidate && CandidatePixelCount == 0 ||
            presence == PortraitBackdropPresenceStatus.Undetermined && CandidatePixelCount != 0)
            throw new ArgumentException("Presence evidence must match the candidate snapshot.", nameof(presence));
        Presence = presence;
        Statistics = Array.AsReadOnly(statistics.ToArray());
        CandidateColorConsistency = candidateColorConsistency;
    }

    /// <summary>Dimensions of the unchanged source used for these pixel statistics.</summary>
    public ImageDimensions Dimensions { get; }
    /// <summary>Estimated presence, or a request for additional scene evidence.</summary>
    public PortraitBackdropPresenceStatus Presence { get; }
    /// <summary>Number of accepted border-connected candidate pixels.</summary>
    public long CandidatePixelCount { get; }
    /// <summary>Accepted candidate fraction of the source image.</summary>
    public double CandidateFraction => (double)CandidatePixelCount / _labels.Length;
    /// <summary>Color consistency within configured tolerance, an engineering statistic.</summary>
    public double CandidateColorConsistency { get; }
    /// <summary>Owned RGB pixel-statistic records for all four spatial labels.</summary>
    public IReadOnlyList<PortraitRegionPixelStatistics> Statistics { get; }
    /// <summary>Explicit measurement basis separating pixel channels from codec transform bands.</summary>
    public string MeasurementBasis => "Decoded 8-bit RGB pixel channels. Codec component and subband energy requires a separate transform measurement.";
    /// <summary>Scene and measurement checks required before using these candidates for allocation.</summary>
    public IReadOnlyList<string> VerificationRequirements { get; } = Array.AsReadOnly(new[]
    {
        "Review the supplied full head, hair, forehead, ears, and shoulder boundaries against the source photograph.",
        "Validate pixel-threshold behavior for the intended capture conditions before using the candidate mask as an allocation signal.",
        "Evaluate uniform-background capture requirements separately from this estimated scene label."
    });

    /// <summary>Copies the four-label snapshot for diagnostics.</summary>
    public byte[] CopyLabels() => (byte[])_labels.Clone();
    /// <summary>Copies the candidate mask with zero outside the accepted region and one inside.</summary>
    public byte[] CopyCandidateMask() => _labels.Select(label => label == (byte)PortraitRegionLabel.BackdropCandidate ? (byte)1 : (byte)0).ToArray();
    /// <summary>Gets a label at a source-image pixel.</summary>
    public PortraitRegionLabel GetLabel(int x, int y)
    {
        if ((uint)x >= (uint)Dimensions.Width || (uint)y >= (uint)Dimensions.Height)
            throw new ArgumentOutOfRangeException(nameof(x));
        return (PortraitRegionLabel)_labels[y * Dimensions.Width + x];
    }

    private static bool Valid(RgbPixelComponentStatistics? component) => component != null &&
        double.IsFinite(component.Mean) && component.Mean is >= 0 and <= 255 &&
        double.IsFinite(component.Variance) && component.Variance is >= 0 and <= 65_025 &&
        double.IsFinite(component.QuantizedEntropyBits) && component.QuantizedEntropyBits is >= 0 and <= 4.000000001 &&
        double.IsFinite(component.MeanNeighborGradient) && component.MeanNeighborGradient is >= 0 and <= 255 &&
        double.IsFinite(component.EdgeDensity) && component.EdgeDensity is >= 0 and <= 1;

    private static bool Valid(RgbPixelCorrelation? correlation) => correlation != null &&
        Enum.IsDefined(correlation.Status) && double.IsFinite(correlation.Value) && correlation.Value is >= -1 and <= 1 &&
        (correlation.Status == PixelCorrelationStatus.Defined || correlation.Value == 0);
}

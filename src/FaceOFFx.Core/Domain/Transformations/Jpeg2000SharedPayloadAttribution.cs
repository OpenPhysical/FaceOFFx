using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>Committed payload and estimated face credit for one wavelet band.</summary>
[PublicAPI]
public readonly record struct Jpeg2000SubbandAttribution(
    int Tile,
    int Component,
    int ResolutionLevel,
    Jpeg2000WaveletSubband Subband,
    long PayloadBytes,
    double FacePayloadByteEstimate);

/// <summary>Conserved regional payload under the versioned synthesis-energy measurement convention.</summary>
[PublicAPI]
public sealed class Jpeg2000SharedPayloadAttribution
{
    /// <summary>Identifies the spatial influence and coding-pass benefit calculation.</summary>
    public const string MethodIdentifier = "synthesis-energy-decoder-effective-pass-v3";
    /// <summary>Versioned measurement convention used by this report.</summary>
    public string MethodId => MethodIdentifier;
    /// <summary>Owned wavelet-band rows in committed output order.</summary>
    public IReadOnlyList<Jpeg2000SubbandAttribution> Subbands { get; }
    /// <summary>Area of the fixed face mask used for spatial influence.</summary>
    public long RoiPixelCount { get; }
    /// <summary>Actual code-block bytes, excluding every header and container byte.</summary>
    public long PacketBodyBytes { get; }
    /// <summary>Fractional face allocation before downward rounding.</summary>
    public double FacePayloadByteEstimate { get; }
    /// <summary>Whole-byte face credit rounded down after aggregation.</summary>
    public long AttributedFacePayloadBytes { get; }
    /// <summary>Remaining payload, including the fractional rounding remainder.</summary>
    public long OutsidePayloadBytes => PacketBodyBytes - AttributedFacePayloadBytes;

    /// <summary>Copies regional rows and validates their emitted-byte conservation.</summary>
    public Jpeg2000SharedPayloadAttribution(
        IEnumerable<Jpeg2000SubbandAttribution> subbands,
        long roiPixelCount,
        long packetBodyBytes,
        double facePayloadByteEstimate,
        long attributedFacePayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(subbands);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roiPixelCount);
        ArgumentOutOfRangeException.ThrowIfNegative(packetBodyBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(packetBodyBytes, int.MaxValue);
        if (!double.IsFinite(facePayloadByteEstimate) || facePayloadByteEstimate < 0 ||
            facePayloadByteEstimate > packetBodyBytes || attributedFacePayloadBytes < 0 ||
            attributedFacePayloadBytes != (long)Math.Floor(facePayloadByteEstimate))
        {
            throw new ArgumentException("Face credit must be finite, bounded by payload and rounded down once.");
        }

        var rows = subbands.ToArray();
        var keys = new HashSet<(int, int, int, Jpeg2000WaveletSubband)>();
        long payloadSum = 0;
        double faceSum = 0;
        foreach (var row in rows)
        {
            if (row.Tile < 0 || row.Component is < 0 or > 2 || row.ResolutionLevel is < 0 or > 16 ||
                !Enum.IsDefined(row.Subband) || (row.Subband == Jpeg2000WaveletSubband.LL) != (row.ResolutionLevel == 0) ||
                row.PayloadBytes < 0 || !double.IsFinite(row.FacePayloadByteEstimate) ||
                row.FacePayloadByteEstimate < 0 || row.FacePayloadByteEstimate > row.PayloadBytes ||
                !keys.Add((row.Tile, row.Component, row.ResolutionLevel, row.Subband)))
            {
                throw new ArgumentException("Regional rows require unique valid keys and bounded face estimates.", nameof(subbands));
            }
            payloadSum = checked(payloadSum + row.PayloadBytes);
            faceSum += row.FacePayloadByteEstimate;
        }
        var tolerance = Math.Max(1, packetBodyBytes) * 1e-12;
        if (payloadSum != packetBodyBytes || Math.Abs(faceSum - facePayloadByteEstimate) > tolerance)
        {
            throw new ArgumentException("Regional rows must reconcile with the complete code-block payload.", nameof(subbands));
        }

        Subbands = Array.AsReadOnly(rows);
        RoiPixelCount = roiPixelCount;
        PacketBodyBytes = packetBodyBytes;
        FacePayloadByteEstimate = facePayloadByteEstimate;
        AttributedFacePayloadBytes = attributedFacePayloadBytes;
    }
}

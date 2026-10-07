using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>Disjoint committed code-block payload for one tile, component and subband.</summary>
[PublicAPI]
public readonly record struct Jpeg2000SubbandPayload(
    int Tile,
    int Component,
    int ResolutionLevel,
    Jpeg2000WaveletSubband Subband,
    int EmittedCodeBlockCount,
    long PayloadBytes,
    long ProtectedMaxshiftPhaseBytes,
    long WholeBandPromotedBytes,
    long CodeBlockPromotedBytes,
    long OutsideRoiCodeBlockBytes,
    long MixedOrRefinementBytes,
    long ProtectedPhaseBytesInMixedCodeBlocks);

/// <summary>Immutable emitted-byte accounting. Shared coding data retains its allocation category.</summary>
[PublicAPI]
public sealed class Jpeg2000PayloadTelemetry
{
    /// <summary>Owned, read-only payload rows keyed by tile, component and wavelet band.</summary>
    public IReadOnlyList<Jpeg2000SubbandPayload> Subbands { get; }
    /// <summary>Complete codestream length, including markers and packet headers.</summary>
    public int CodestreamBytes { get; }
    /// <summary>JP2 bytes surrounding the codestream.</summary>
    public int ContainerBytes { get; }
    /// <summary>Length of the final codestream marker.</summary>
    public int EndOfCodestreamBytes { get; }
    /// <summary>Number of committed packets, excluding allocator simulations.</summary>
    public int EmittedPacketCount { get; }
    /// <summary>Number of committed code blocks with emitted payload.</summary>
    public int EmittedCodeBlockCount { get; }
    /// <summary>Codestream headers and markers, with packet headers and EOC accounted separately.</summary>
    public long MainAndTileHeaderBytes { get; }
    /// <summary>Shared packet headers and their SOP or EPH markers.</summary>
    public long PacketHeaderBytes { get; }
    /// <summary>Emitted code-block bytes, excluding headers.</summary>
    public long PacketBodyBytes { get; }
    /// <summary>Upper Maxshift-phase payload, with promoted blocks accounted separately.</summary>
    public long ProtectedMaxshiftPhaseBytes { get; }
    /// <summary>Payload promoted across an entire wavelet band, retaining shared image information.</summary>
    public long WholeBandPromotedBytes { get; }
    /// <summary>Payload promoted by code-block alignment.</summary>
    public long CodeBlockPromotedBytes { get; }
    /// <summary>Payload from nonpromoted blocks containing zero ROI coefficients.</summary>
    public long OutsideRoiCodeBlockBytes { get; }
    /// <summary>Remaining nonpromoted payload in blocks containing ROI coefficients.</summary>
    public long MixedOrRefinementBytes { get; }
    /// <summary>Subset of protected-phase bytes in blocks with both ROI and outside coefficients.</summary>
    public long ProtectedPhaseBytesInMixedCodeBlocks { get; }
    /// <summary>Complete output length after codestream and container reconciliation.</summary>
    public long TotalOutputBytes => (long)CodestreamBytes + ContainerBytes;

    /// <summary>Snapshots disjoint payload rows and validates their complete-output accounting.</summary>
    public Jpeg2000PayloadTelemetry(
        IEnumerable<Jpeg2000SubbandPayload> subbands,
        int codestreamBytes,
        int containerBytes,
        long mainAndTileHeaderBytes,
        long packetHeaderBytes,
        int endOfCodestreamBytes,
        int emittedPacketCount)
    {
        ArgumentNullException.ThrowIfNull(subbands);
        ArgumentOutOfRangeException.ThrowIfNegative(codestreamBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(containerBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(mainAndTileHeaderBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(packetHeaderBytes);
        if (endOfCodestreamBytes != 2)
        {
            throw new ArgumentOutOfRangeException(nameof(endOfCodestreamBytes), "The JPEG 2000 EOC marker occupies two bytes.");
        }
        ArgumentOutOfRangeException.ThrowIfNegative(emittedPacketCount);
        var rows = subbands.ToArray();
        var keys = new HashSet<(int, int, int, Jpeg2000WaveletSubband)>();
        foreach (var row in rows)
        {
            if (row.Tile < 0 || row.Component is < 0 or > 2 || row.ResolutionLevel is < 0 or > 16 ||
                !Enum.IsDefined(row.Subband) || (row.Subband == Jpeg2000WaveletSubband.LL) != (row.ResolutionLevel == 0) ||
                row.EmittedCodeBlockCount < 0 || row.PayloadBytes < 0 || row.ProtectedMaxshiftPhaseBytes < 0 ||
                row.WholeBandPromotedBytes < 0 || row.CodeBlockPromotedBytes < 0 || row.OutsideRoiCodeBlockBytes < 0 ||
                row.MixedOrRefinementBytes < 0 || row.ProtectedPhaseBytesInMixedCodeBlocks < 0 ||
                row.ProtectedPhaseBytesInMixedCodeBlocks > row.ProtectedMaxshiftPhaseBytes ||
                !keys.Add((row.Tile, row.Component, row.ResolutionLevel, row.Subband)))
            {
                throw new ArgumentException("Subband accounting requires unique valid keys and nonnegative counts.", nameof(subbands));
            }
            if (row.PayloadBytes != checked(row.ProtectedMaxshiftPhaseBytes + row.WholeBandPromotedBytes +
                row.CodeBlockPromotedBytes + row.OutsideRoiCodeBlockBytes + row.MixedOrRefinementBytes))
            {
                throw new ArgumentException("Subband payload categories must reconcile exactly.", nameof(subbands));
            }
        }

        Subbands = Array.AsReadOnly(rows);
        CodestreamBytes = codestreamBytes;
        ContainerBytes = containerBytes;
        MainAndTileHeaderBytes = mainAndTileHeaderBytes;
        PacketHeaderBytes = packetHeaderBytes;
        EndOfCodestreamBytes = endOfCodestreamBytes;
        EmittedPacketCount = emittedPacketCount;
        EmittedCodeBlockCount = rows.Sum(row => row.EmittedCodeBlockCount);
        PacketBodyBytes = rows.Sum(row => row.PayloadBytes);
        ProtectedMaxshiftPhaseBytes = rows.Sum(row => row.ProtectedMaxshiftPhaseBytes);
        WholeBandPromotedBytes = rows.Sum(row => row.WholeBandPromotedBytes);
        CodeBlockPromotedBytes = rows.Sum(row => row.CodeBlockPromotedBytes);
        OutsideRoiCodeBlockBytes = rows.Sum(row => row.OutsideRoiCodeBlockBytes);
        MixedOrRefinementBytes = rows.Sum(row => row.MixedOrRefinementBytes);
        ProtectedPhaseBytesInMixedCodeBlocks = rows.Sum(row => row.ProtectedPhaseBytesInMixedCodeBlocks);
        if (codestreamBytes != checked(mainAndTileHeaderBytes + packetHeaderBytes + PacketBodyBytes + endOfCodestreamBytes))
        {
            throw new ArgumentException("Codestream headers, body and end marker must reconcile exactly.", nameof(codestreamBytes));
        }
    }
}

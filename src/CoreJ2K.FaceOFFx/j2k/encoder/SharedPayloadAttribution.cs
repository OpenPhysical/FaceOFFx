using System;
using System.Collections.Generic;

namespace CoreJ2K.FaceOFFx.j2k.encoder
{
    /// <summary>A conserved operational split of emitted packet-body bytes using positive coding-pass gains and finite 9/7 synthesis energy.</summary>
    /// <remarks>
    /// The model uses coefficient-diagonal squared synthesis influence, omits interference between coefficients,
    /// and uses positive decoder-effective Tier1 distortion lookup gains. Ordinary ROI fractional refinements
    /// clamped by the Maxshift decoder receive zero gain. The assigned bytes measure coding benefit under
    /// this operational convention. Packet, codestream and container headers receive zero credit.
    /// </remarks>
    internal sealed class SharedPayloadAttribution
    {
        public const string MethodIdentifier = "synthesis-energy-decoder-effective-pass-v3";
        public string MethodId => MethodIdentifier;
        public IReadOnlyList<SubbandPayloadAttribution> Subbands { get; }
        public long RoiPixelCount { get; }
        public long PacketBodyBytes { get; }
        public double FacePayloadByteEstimate { get; }
        /// <summary>The floor of the aggregate fractional estimate. Rounding occurs once for the complete ledger.</summary>
        public long AttributedFacePayloadBytes { get; }
        /// <summary>Remaining body credit, including zero-gain intervals and the fractional rounding remainder.</summary>
        public long OutsidePayloadBytes => PacketBodyBytes - AttributedFacePayloadBytes;

        internal SharedPayloadAttribution(SubbandPayloadAttribution[] subbands, long roiPixelCount,
            long packetBodyBytes)
        {
            if (subbands == null) throw new ArgumentNullException(nameof(subbands));
            if (roiPixelCount <= 0 || packetBodyBytes < 0) throw new ArgumentOutOfRangeException(nameof(roiPixelCount));
            var owned = (SubbandPayloadAttribution[])subbands.Clone();
            var keys = new HashSet<(int, int, int, WaveletSubband)>();
            long body = 0;
            double estimate = 0;
            foreach (var row in owned)
            {
                if (row == null || row.Tile < 0 || row.Component < 0 || row.ResolutionLevel < 0 ||
                    !Enum.IsDefined(typeof(WaveletSubband), row.Subband) ||
                    (row.Subband == WaveletSubband.LL) != (row.ResolutionLevel == 0) ||
                    row.PayloadBytes < 0 || !double.IsFinite(row.FacePayloadByteEstimate) ||
                    row.FacePayloadByteEstimate < 0 || row.FacePayloadByteEstimate > row.PayloadBytes ||
                    !keys.Add((row.Tile, row.Component, row.ResolutionLevel, row.Subband)))
                    throw new ArgumentException("Shared attribution rows require unique valid bands and finite conserved byte estimates.", nameof(subbands));
                body = checked(body + row.PayloadBytes);
                estimate += row.FacePayloadByteEstimate;
            }
            if (body != packetBodyBytes || !double.IsFinite(estimate) || estimate < 0 || estimate > body)
                throw new ArgumentException("Shared attribution must reconcile with the complete emitted packet body.", nameof(subbands));
            Subbands = Array.AsReadOnly(owned);
            RoiPixelCount = roiPixelCount;
            PacketBodyBytes = body;
            FacePayloadByteEstimate = estimate;
            AttributedFacePayloadBytes = checked((long)Math.Floor(estimate));
        }
    }
}

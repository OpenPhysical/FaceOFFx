using System;
using System.Collections.Generic;

namespace CoreJ2K.FaceOFFx.j2k.encoder
{
    /// <summary>Immutable committed-payload accounting. Category totals reconcile with the complete codec output.</summary>
    internal sealed class PayloadTelemetry
    {
        public IReadOnlyList<SubbandPayloadTelemetry> Subbands { get; }
        public int CodestreamBytes { get; }
        public int ContainerBytes { get; }
        public long TotalOutputBytes => (long)CodestreamBytes + ContainerBytes;
        /// <summary>Codestream main and tile headers and markers, with packet headers and EOC accounted separately.</summary>
        public long MainAndTileHeaderBytes { get; }
        /// <summary>Shared packet-header bytes, including configured SOP/EPH markers. These bytes have no spatial attribution.</summary>
        public long PacketHeaderBytes { get; }
        public int EndOfCodestreamBytes => 2;
        public long PacketBodyBytes { get; }
        public int EmittedPacketCount { get; }
        public int EmittedCodeBlockCount { get; }
        public long ProtectedMaxshiftPhaseBytes { get; }
        public long WholeBandPromotedBytes { get; }
        public long CodeBlockPromotedBytes { get; }
        public long OutsideRoiCodeBlockBytes { get; }
        public long MixedOrRefinementBytes { get; }
        public long ProtectedPhaseBytesInMixedCodeBlocks { get; }

        internal PayloadTelemetry(SubbandPayloadTelemetry[] subbands, int codestreamBytes, int containerBytes,
            long packetHeaderBytes, int emittedPacketCount)
        {
            var snapshot = (SubbandPayloadTelemetry[])subbands.Clone();
            Subbands = Array.AsReadOnly(snapshot);
            CodestreamBytes = codestreamBytes;
            ContainerBytes = containerBytes;
            PacketHeaderBytes = packetHeaderBytes;
            EmittedPacketCount = emittedPacketCount;
            foreach (var band in snapshot)
            {
                if (band.PayloadBytes < 0 || band.ProtectedMaxshiftPhaseBytes < 0 || band.WholeBandPromotedBytes < 0 ||
                    band.CodeBlockPromotedBytes < 0 || band.OutsideRoiCodeBlockBytes < 0 || band.MixedOrRefinementBytes < 0 ||
                    band.PayloadBytes != band.ProtectedMaxshiftPhaseBytes + band.WholeBandPromotedBytes +
                        band.CodeBlockPromotedBytes + band.OutsideRoiCodeBlockBytes + band.MixedOrRefinementBytes ||
                    band.ProtectedPhaseBytesInMixedCodeBlocks < 0 ||
                    band.ProtectedPhaseBytesInMixedCodeBlocks > band.ProtectedMaxshiftPhaseBytes)
                    throw new InvalidOperationException("Subband payload categories do not reconcile.");
                PacketBodyBytes += band.PayloadBytes;
                EmittedCodeBlockCount += band.EmittedCodeBlockCount;
                ProtectedMaxshiftPhaseBytes += band.ProtectedMaxshiftPhaseBytes;
                WholeBandPromotedBytes += band.WholeBandPromotedBytes;
                CodeBlockPromotedBytes += band.CodeBlockPromotedBytes;
                OutsideRoiCodeBlockBytes += band.OutsideRoiCodeBlockBytes;
                MixedOrRefinementBytes += band.MixedOrRefinementBytes;
                ProtectedPhaseBytesInMixedCodeBlocks += band.ProtectedPhaseBytesInMixedCodeBlocks;
            }
            MainAndTileHeaderBytes = codestreamBytes - packetHeaderBytes - PacketBodyBytes - EndOfCodestreamBytes;
            if (containerBytes < 0 || packetHeaderBytes < 0 || emittedPacketCount < 0 || MainAndTileHeaderBytes < 0)
                throw new InvalidOperationException("Payload telemetry does not reconcile with complete output lengths.");
        }
    }
}

using System;
using System.Collections.Generic;
using CoreJ2K.FaceOFFx.j2k.encoder;

namespace CoreJ2K.FaceOFFx.j2k.entropy.encoder
{
    internal sealed partial class EBCOTRateAllocator
    {
        private bool _collectPayloadTelemetry;
        private int _payloadEmittedPacketCount;
        private SubbandPayloadTelemetry[] _payloadTelemetrySubbands;

        private sealed class PayloadTotals
        {
            internal int Blocks;
            internal long Body, Protected, WholeBand, AlignedBlock, OutsideOnly, MixedOrRefinement, MixedProtected;
        }

        private void CaptureCommittedPayloadTelemetry()
        {
            // One layer emits each selected block prefix once. Reconcile with committed packet counters.
            var bands = new SortedDictionary<(int Tile, int Component, int Resolution, int Subband), PayloadTotals>();
            foreach (var entry in _budgetBlocks)
            {
                var block = entry.Data;
                int selected = _budgetSelection[block];
                if (selected < 0) continue;
                int bytes = block.truncRates[block.truncIdxs[selected]];
                if (bytes == 0) continue;
                if (bytes < 0) throw new InvalidOperationException("Committed block prefix has an invalid length.");
                var key = (entry.Tile, entry.Component, entry.Resolution, block.sb.orientation);
                if (!bands.TryGetValue(key, out var totals))
                {
                    totals = new PayloadTotals();
                    bands.Add(key, totals);
                }
                totals.Blocks++;
                totals.Body += bytes;
                if (block.RoiWholeBandPromotion)
                    totals.WholeBand += bytes;
                else if (block.RoiBlockAlignedPromotion)
                    totals.AlignedBlock += bytes;
                else
                {
                    int protectedBytes = block.ProtectedPrefixBytes(selected);
                    if (protectedBytes < 0 || protectedBytes > bytes)
                        throw new InvalidOperationException("Committed block phase lengths do not reconcile.");
                    totals.Protected += protectedBytes;
                    if (block.RoiMixedSupport) totals.MixedProtected += protectedBytes;
                    if (block.nROIcoeff == 0)
                        totals.OutsideOnly += bytes;
                    else
                        totals.MixedOrRefinement += bytes - protectedBytes;
                }
            }
            var rows = new SubbandPayloadTelemetry[bands.Count];
            int index = 0;
            foreach (var band in bands)
            {
                var key = band.Key;
                var totals = band.Value;
                rows[index++] = new SubbandPayloadTelemetry(key.Tile, key.Component, key.Resolution,
                    (WaveletSubband)key.Subband, totals.Blocks, totals.Body, totals.Protected,
                    totals.WholeBand, totals.AlignedBlock, totals.OutsideOnly, totals.MixedOrRefinement, totals.MixedProtected);
            }
            _payloadTelemetrySubbands = rows;
        }

        internal override PayloadTelemetry CreatePayloadTelemetry(int codestreamBytes, int containerBytes)
        {
            if (!_collectPayloadTelemetry) return null;
            if (_payloadTelemetrySubbands == null)
                throw new InvalidOperationException("Payload telemetry requires completed, validated packet emission.");
            var report = new PayloadTelemetry(_payloadTelemetrySubbands, codestreamBytes, containerBytes,
                _budgetDiagnostics.PacketHeaderBytes, _payloadEmittedPacketCount);
            if (report.PacketBodyBytes != _budgetDiagnostics.PacketBodyBytes ||
                report.ProtectedMaxshiftPhaseBytes != _budgetDiagnostics.ProtectedMaxshiftPayloadBytes ||
                report.WholeBandPromotedBytes + report.CodeBlockPromotedBytes != _budgetDiagnostics.PromotedPayloadBytes ||
                report.OutsideRoiCodeBlockBytes + report.MixedOrRefinementBytes != _budgetDiagnostics.UnprotectedPayloadBytes ||
                report.ProtectedPhaseBytesInMixedCodeBlocks != _budgetDiagnostics.ProtectedPhaseBytesInMixedBlocks)
                throw new InvalidOperationException("Subband telemetry does not reconcile with committed packet counters.");
            return report;
        }
    }
}

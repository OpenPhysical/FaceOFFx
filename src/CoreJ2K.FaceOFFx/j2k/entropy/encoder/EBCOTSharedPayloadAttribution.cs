using System;
using System.Collections.Generic;
using CoreJ2K.FaceOFFx.j2k.encoder;

namespace CoreJ2K.FaceOFFx.j2k.entropy.encoder
{
    internal sealed partial class EBCOTRateAllocator
    {
        internal override SharedPayloadAttribution CreateSharedPayloadAttribution()
        {
            var influence = (src as StdEntropyCoder)?.SharedPayloadInfluence;
            if (influence == null) return null;
            if (!_collectPayloadTelemetry || _payloadTelemetrySubbands == null || _budgetSelection == null)
                throw new InvalidOperationException("Shared attribution requires completed, reconciled packet-body emission.");
            var estimates = new Dictionary<(int, int, int, int), double>();
            foreach (var entry in _budgetBlocks)
            {
                int selected = _budgetSelection[entry.Data];
                if (selected < 0) continue;
                var key = (entry.Tile, entry.Component, entry.Resolution, entry.Data.sb.orientation);
                double estimate = AttributeSelectedPrefix(entry.Data, selected);
                estimates.TryGetValue(key, out double previous);
                estimates[key] = previous + estimate;
            }
            var rows = new SubbandPayloadAttribution[_payloadTelemetrySubbands.Length];
            for (int i = 0; i < rows.Length; i++)
            {
                var band = _payloadTelemetrySubbands[i];
                estimates.TryGetValue((band.Tile, band.Component, band.ResolutionLevel, (int)band.Subband), out double estimate);
                if (!double.IsFinite(estimate) || estimate < 0 || estimate > band.PayloadBytes * (1 + 1e-12))
                    throw new InvalidOperationException("Shared band attribution exceeds its committed payload.");
                rows[i] = new SubbandPayloadAttribution(band.Tile, band.Component, band.ResolutionLevel,
                    band.Subband, band.PayloadBytes, Math.Min(estimate, band.PayloadBytes));
            }
            var report = new SharedPayloadAttribution(rows, influence.RoiPixelCount, _budgetDiagnostics.PacketBodyBytes);
            return report;
        }

        internal static double AttributeSelectedPrefix(CBlkRateDistStats block, int selected)
        {
            if (selected < 0) return 0;
            if (selected >= block.nVldTrunc)
                throw new InvalidOperationException("Selected shared prefix lies outside its candidate map.");
            return AttributePassPrefix(block, block.truncIdxs[selected]);
        }

        internal static double AttributePassPrefix(CBlkRateDistStats block, int terminalPass)
        {
            if (terminalPass < 0 || terminalPass >= block.nTotTrunc || block.SynthesisTotalGainsByPass == null ||
                block.SynthesisFaceGainsByPass == null || block.SynthesisTotalGainsByPass.Length != block.nTotTrunc ||
                block.SynthesisFaceGainsByPass.Length != block.nTotTrunc)
                throw new InvalidOperationException("Committed block requires complete synthesis pass-gain statistics.");
            Span<int> endpoints = stackalloc int[32 * StdEntropyCoderOptions.NUM_PASSES];
            int count = 0, nextBytes = int.MaxValue;
            // Canonical partitions depend only on the emitted terminal pass and actual byte endpoints.
            // Walking backward folds equal or nonmonotonic rate estimates into increasing intervals.
            for (int pass = terminalPass; pass >= 0; pass--)
            {
                int bytes = block.truncRates[pass];
                if (bytes <= 0 || bytes >= nextBytes) continue;
                endpoints[count++] = pass;
                nextBytes = bytes;
            }
            if (count == 0 || endpoints[0] != terminalPass)
                throw new InvalidOperationException("Shared attribution requires a positive emitted terminal byte endpoint.");
            double estimate = 0, previousTotal = 0, previousFace = 0;
            int previousBytes = 0;
            for (int endpoint = count - 1; endpoint >= 0; endpoint--)
            {
                int pass = endpoints[endpoint];
                int bytes = block.truncRates[pass];
                double total = block.SynthesisTotalGainsByPass[pass];
                double face = block.SynthesisFaceGainsByPass[pass];
                double totalGain = total - previousTotal, faceGain = face - previousFace;
                int byteGain = bytes - previousBytes;
                if (byteGain <= 0 || !double.IsFinite(totalGain) || !double.IsFinite(faceGain) ||
                    totalGain < 0 || faceGain < 0 || faceGain > totalGain * (1 + 1e-12))
                    throw new InvalidOperationException("Shared attribution requires increasing valid prefixes and conserved finite positive pass gains.");
                // Zero-gain coding overhead remains outside the credited face payload.
                if (totalGain > 0) estimate += byteGain * Math.Min(1, faceGain / totalGain);
                previousBytes = bytes; previousTotal = total; previousFace = face;
            }
            if (!double.IsFinite(estimate) || estimate < 0 || estimate > previousBytes * (1 + 1e-12))
                throw new InvalidOperationException("Shared block attribution exceeds its emitted prefix.");
            return Math.Min(estimate, previousBytes);
        }
    }
}

using System;
using System.Collections.Generic;
using CoreJ2K.FaceOFFx.j2k.codestream.writer;
using CoreJ2K.FaceOFFx.j2k.encoder;
using CoreJ2K.FaceOFFx.j2k.roi.encoder;

namespace CoreJ2K.FaceOFFx.j2k.entropy.encoder
{
    internal sealed partial class EBCOTRateAllocator
    {
        private bool _budgetEnabled;
        private bool _budgetInitialized;
        private int _maximumCodestreamBytes;
        private int _budgetHeaderBytes;
        private readonly List<BudgetBlock> _budgetBlocks = new List<BudgetBlock>();
        private Dictionary<CBlkRateDistStats, int> _budgetSelection;
        private BudgetCandidate _budgetExpected;
        private BudgetCandidate _budgetFullCandidate;
        private readonly BudgetPrefixCache _budgetPrefixCache = new BudgetPrefixCache();
        private int[] _budgetCandidateSelection;
        private BudgetAllocationDiagnostics _budgetDiagnostics;
        private BitOutputBuffer _budgetPacketHeader;
        private byte[] _budgetPacketBody;

        public override BudgetAllocationDiagnostics BudgetDiagnostics => _budgetDiagnostics;

        public override void ConfigureBudget(int maximumCodestreamBytes, bool collectPayloadTelemetry = true)
        {
            if (_budgetInitialized) throw new InvalidOperationException("Configure the byte budget before initializing the allocator.");
            if (maximumCodestreamBytes <= 2) throw new ArgumentOutOfRangeException(nameof(maximumCodestreamBytes));
            if (num_Layers != 1) throw new NotSupportedException("Balanced encoding uses one quality layer.");
            _budgetEnabled = true;
            _maximumCodestreamBytes = maximumCodestreamBytes;
            _collectPayloadTelemetry = collectPayloadTelemetry;
            _budgetDiagnostics = new BudgetAllocationDiagnostics { MaximumCodestreamBytes = maximumCodestreamBytes,
                UsesSubbandWeights = (src as StdEntropyCoder)?.SubbandWeights?.HasNonNeutralWeights ?? false };
        }

        private sealed class BudgetBlock
        {
            internal readonly int Tile, Component, Resolution, Subband, Index;
            internal readonly CBlkRateDistStats Data;
            internal int[] PrefixRates, PrefixProtectedBytes;
            internal double[] PrefixDistortions;

            internal BudgetBlock(int tile, int component, int resolution, int subband, int index, CBlkRateDistStats data)
            {
                Tile = tile; Component = component; Resolution = resolution;
                Subband = subband; Index = index; Data = data;
            }
        }

        internal sealed class BudgetCandidate
        {
            internal int[] Selection;
            internal long PacketBytes, ProtectedBytes;
            internal double DistortionDecrease;
            internal BudgetCandidate NextWithHash;
        }

        internal sealed class BudgetPrefixCache
        {
            private readonly Dictionary<ulong, BudgetCandidate> _candidates = new Dictionary<ulong, BudgetCandidate>();
            private readonly long _maximumSelectionBytes;
            private readonly int _maximumEntries;
            internal long SelectionBytes { get; private set; }
            internal int Count { get; private set; }

            internal BudgetPrefixCache(long maximumSelectionBytes = 8L * 1024 * 1024, int maximumEntries = 2048)
            {
                if (maximumSelectionBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumSelectionBytes));
                if (maximumEntries < 0) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
                _maximumSelectionBytes = maximumSelectionBytes;
                _maximumEntries = maximumEntries;
            }

            internal bool TryGet(ulong hash, int[] selection, out BudgetCandidate candidate)
            {
                _candidates.TryGetValue(hash, out candidate);
                while (candidate != null)
                {
                    bool equal = candidate.Selection.Length == selection.Length;
                    for (int i = 0; equal && i < selection.Length; i++)
                        equal = candidate.Selection[i] == selection[i];
                    if (equal) return true;
                    candidate = candidate.NextWithHash;
                }
                return false;
            }

            internal BudgetCandidate Add(ulong hash, int[] selection, long packetBytes, long protectedBytes, double distortionDecrease)
            {
                var candidate = new BudgetCandidate
                {
                    Selection = (int[])selection.Clone(), PacketBytes = packetBytes,
                    ProtectedBytes = protectedBytes, DistortionDecrease = distortionDecrease
                };
                long selectionBytes = (long)selection.Length * sizeof(int);
                // Bound retained maps for large inputs. An uncached candidate still owns its prefix snapshot.
                if (Count < _maximumEntries && selectionBytes <= _maximumSelectionBytes - SelectionBytes)
                {
                    _candidates.TryGetValue(hash, out candidate.NextWithHash);
                    _candidates[hash] = candidate;
                    SelectionBytes += selectionBytes;
                    Count++;
                }
                return candidate;
            }

            internal void Clear()
            {
                _candidates.Clear();
                SelectionBytes = 0;
                Count = 0;
            }
        }

        private void AllocateBudgetLayer(int maximumPacketBytes)
        {
            _budgetDiagnostics.EncodedCodeBlockCount = _budgetBlocks.Count;
            _budgetCandidateSelection = new int[_budgetBlocks.Count];
            double utilityScale = 1;
            foreach (var entry in _budgetBlocks)
            {
                var block = entry.Data;
                entry.PrefixRates = new int[block.nVldTrunc];
                entry.PrefixProtectedBytes = new int[block.nVldTrunc];
                entry.PrefixDistortions = new double[block.nVldTrunc];
                if (block.nVldTrunc == 0) continue;
                int last = block.nVldTrunc - 1;
                for (int k = 0; k < block.nVldTrunc; k++)
                {
                    int bytes = block.truncRates[block.truncIdxs[k]];
                    double distortion = block.truncDists[block.truncIdxs[k]];
                    if (bytes <= 0 || double.IsNaN(distortion) || double.IsInfinity(distortion) || distortion < 0)
                        throw new InvalidOperationException("Invalid rate-distortion statistics for byte allocation.");
                    entry.PrefixRates[k] = bytes;
                    entry.PrefixProtectedBytes[k] = block.ProtectedPrefixBytes(k);
                    entry.PrefixDistortions[k] = distortion;
                    utilityScale = Math.Max(utilityScale, distortion / bytes);
                }
            }
            BudgetCandidate best = null;
            SearchBudgetThreshold(0, utilityScale, maximumPacketBytes, ref best);
            if (best == null) throw new InvalidOperationException("The balanced allocator did not find a valid prefix allocation within the byte cap.");
            _budgetExpected = best;
            _budgetSelection = new Dictionary<CBlkRateDistStats, int>(_budgetBlocks.Count);
            for (int i = 0; i < _budgetBlocks.Count; i++)
                _budgetSelection.Add(_budgetBlocks[i].Data, best.Selection[i]);
            _budgetPrefixCache.Clear();
            _budgetFullCandidate = null;
            pktEnc.reset();
        }

        private BudgetCandidate SearchBudgetThreshold(double reward, double utilityScale, int maximumPacketBytes, ref BudgetCandidate best)
        {
            double lower = 0, upper = utilityScale + reward + 1;
            BudgetCandidate bestAtReward = null;
            // Coefficient scoring can change which prefix maximizes the reward-specific objective.
            var full = _budgetFullCandidate ?? (_budgetFullCandidate = SelectAndSimulateBudget(0, 0));
            KeepBudgetCandidate(full, maximumPacketBytes, ref bestAtReward, ref best);
            if (full.PacketBytes <= maximumPacketBytes) return bestAtReward;
            for (int step = 0; step < 48; step++)
            {
                double threshold = (lower + upper) / 2;
                var candidate = SelectAndSimulateBudget(threshold, reward);
                KeepBudgetCandidate(candidate, maximumPacketBytes, ref bestAtReward, ref best);
                if (candidate.PacketBytes > maximumPacketBytes)
                    lower = threshold;
                else
                    upper = threshold;
            }
            return bestAtReward;
        }

        private void KeepBudgetCandidate(BudgetCandidate candidate, int maximumPacketBytes, ref BudgetCandidate bestAtReward, ref BudgetCandidate best)
        {
            if (candidate.PacketBytes > maximumPacketBytes) return;
            if (bestAtReward == null || candidate.PacketBytes > bestAtReward.PacketBytes ||
                candidate.PacketBytes == bestAtReward.PacketBytes && candidate.DistortionDecrease > bestAtReward.DistortionDecrease)
                bestAtReward = candidate;
            if ((best == null || candidate.DistortionDecrease > best.DistortionDecrease ||
                 candidate.DistortionDecrease == best.DistortionDecrease && candidate.PacketBytes > best.PacketBytes))
                best = candidate;
        }

        private BudgetCandidate SelectAndSimulateBudget(double threshold, double reward)
        {
            _budgetDiagnostics.AllocationCandidateCount++;
            long protectedBytes = 0;
            double distortionDecrease = 0;
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < _budgetBlocks.Count; i++)
            {
                var entry = _budgetBlocks[i];
                int selected = -1;
                double bestUtility = 0;
                for (int k = 0; k < entry.PrefixRates.Length; k++)
                {
                    double utility = entry.PrefixDistortions[k] - threshold * entry.PrefixRates[k];
                    if (utility > bestUtility)
                    {
                        bestUtility = utility;
                        selected = k;
                    }
                }
                _budgetCandidateSelection[i] = selected;
                hash = unchecked((hash ^ (uint)selected) * 1099511628211UL);
                if (selected >= 0)
                {
                    protectedBytes += entry.PrefixProtectedBytes[selected];
                    distortionDecrease += entry.PrefixDistortions[selected];
                }
            }
            // A hash is only a lookup aid. Compare every prefix before reusing packet measurements.
            if (_budgetPrefixCache.TryGet(hash, _budgetCandidateSelection, out var cached)) return cached;
            for (int i = 0; i < _budgetBlocks.Count; i++)
            {
                var entry = _budgetBlocks[i];
                truncIdxs[entry.Tile][0][entry.Component][entry.Resolution][entry.Subband][entry.Index] = _budgetCandidateSelection[i];
            }
            pktEnc.reset();
            BitOutputBuffer header = _budgetPacketHeader;
            byte[] body = _budgetPacketBody;
            long measuredProtected = 0;
            long packetBytes = 0;
            for (int tile = 0; tile < src.getNumTiles(); tile++)
            {
                bool sop = string.Equals((string)encSpec.sops.getTileDef(tile), "on", StringComparison.OrdinalIgnoreCase);
                bool eph = string.Equals((string)encSpec.ephs.getTileDef(tile), "on", StringComparison.OrdinalIgnoreCase);
                for (int component = 0; component < src.NumComps; component++)
                {
                    int levels = src.getAnSubbandTree(tile, component).resLvl + 1;
                    for (int resolution = 0; resolution < levels; resolution++)
                    {
                        int precincts = numPrec[tile][component][resolution].x * numPrec[tile][component][resolution].y;
                        for (int precinct = 0; precinct < precincts; precinct++)
                        {
                            header = pktEnc.encodePacket(1, component, resolution, tile, cblks[tile][component][resolution],
                                truncIdxs[tile][0][component][resolution], header, body, precinct);
                            if (!pktEnc.PacketWritable) continue;
                            body = pktEnc.LastBodyBuf;
                            packetBytes += bsWriter.writePacketHead(header.Buffer, header.Length, true, sop, eph);
                            packetBytes += bsWriter.writePacketBody(body, pktEnc.LastBodyLen, true, pktEnc.ROIinPkt, pktEnc.ROILen);
                            measuredProtected += pktEnc.LastProtectedPayloadBytes;
                        }
                    }
                }
            }
            if (protectedBytes != measuredProtected)
                throw new InvalidOperationException("Selected protected prefixes do not reconcile with packet simulation.");
            _budgetPacketHeader = header;
            _budgetPacketBody = body;
            _budgetDiagnostics.AllocationSimulationCount++;
            return _budgetPrefixCache.Add(hash, _budgetCandidateSelection, packetBytes, protectedBytes, distortionDecrease);
        }

        private void WriteBudgetPacket(BitOutputBuffer header, bool sop, bool eph)
        {
            int expectedHeader = bsWriter.writePacketHead(header.Buffer, header.Length, true, sop, eph);
            int expectedBody = pktEnc.LastBodyLen;
            if (_budgetEnabled && bsWriter.MaxAvailableBytes < (long)expectedHeader + expectedBody + 2)
                throw new InvalidOperationException("The byte cap would truncate a packet or its end marker.");
            int writtenHeader = bsWriter.writePacketHead(header.Buffer, header.Length, false, sop, eph);
            int writtenBody = bsWriter.writePacketBody(pktEnc.LastBodyBuf, expectedBody, false, pktEnc.ROIinPkt, pktEnc.ROILen);
            if (!_budgetEnabled) return;
            if (writtenHeader != expectedHeader || writtenBody != expectedBody)
                throw new InvalidOperationException("Actual packet emission differs from exact byte simulation.");
            _budgetDiagnostics.PacketHeaderBytes += writtenHeader;
            _budgetDiagnostics.PacketBodyBytes += writtenBody;
            _budgetDiagnostics.ProtectedMaxshiftPayloadBytes += pktEnc.LastProtectedPayloadBytes;
            _budgetDiagnostics.PromotedPayloadBytes += pktEnc.LastPromotedPayloadBytes;
            _budgetDiagnostics.ProtectedPhaseBytesInMixedBlocks += pktEnc.LastProtectedPhaseBytesInMixedBlocks;
            _budgetDiagnostics.UnprotectedPayloadBytes += writtenBody - pktEnc.LastProtectedPayloadBytes - pktEnc.LastPromotedPayloadBytes;
            if (_collectPayloadTelemetry) _payloadEmittedPacketCount++;
        }

        private void ValidateBudgetEmission()
        {
            if (_budgetDiagnostics.PacketHeaderBytes + _budgetDiagnostics.PacketBodyBytes != _budgetExpected.PacketBytes ||
                _budgetDiagnostics.ProtectedMaxshiftPayloadBytes != _budgetExpected.ProtectedBytes ||
                _budgetDiagnostics.PacketBodyBytes != _budgetDiagnostics.ProtectedMaxshiftPayloadBytes +
                    _budgetDiagnostics.PromotedPayloadBytes + _budgetDiagnostics.UnprotectedPayloadBytes ||
                _budgetHeaderBytes + _budgetExpected.PacketBytes > _maximumCodestreamBytes ||
                (long)bsWriter.Length + 2 != _budgetHeaderBytes + _budgetExpected.PacketBytes)
                throw new InvalidOperationException("Final codestream emission does not satisfy its measured byte constraints.");
            if (_collectPayloadTelemetry) CaptureCommittedPayloadTelemetry();
        }
    }
}

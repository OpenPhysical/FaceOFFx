namespace CoreJ2K.FaceOFFx.j2k.entropy.encoder
{
    /// <summary>Measured encoder payload and allocation counts. Spatial regional-compression attribution requires separate validation.</summary>
    internal sealed class BudgetAllocationDiagnostics
    {
        public int MaximumCodestreamBytes { get; internal set; }
        public bool UsesSubbandWeights { get; internal set; }
        public long ProtectedMaxshiftPayloadBytes { get; internal set; }
        public long PacketHeaderBytes { get; internal set; }
        public long PacketBodyBytes { get; internal set; }
        public long PromotedPayloadBytes { get; internal set; }
        public long UnprotectedPayloadBytes { get; internal set; }
        public long ProtectedPhaseBytesInMixedBlocks { get; internal set; }
        public int EncodedCodeBlockCount { get; internal set; }
        public int AllocationCandidateCount { get; internal set; }
        public int AllocationSimulationCount { get; internal set; }
    }
}

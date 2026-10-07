namespace CoreJ2K.FaceOFFx.j2k.encoder
{
    /// <summary>Committed one-layer code-block payload by wavelet band. Spatial regional attribution requires separate validation.</summary>
    internal sealed class SubbandPayloadTelemetry
    {
        public int Tile { get; }
        public int Component { get; }
        /// <summary>Zero is the coarsest LL band; higher values approach native resolution.</summary>
        public int ResolutionLevel { get; }
        public WaveletSubband Subband { get; }
        public int EmittedCodeBlockCount { get; }
        public long PayloadBytes { get; }
        /// <summary>Upper Maxshift coding-phase bytes, including entropy-coding overhead, with promoted blocks accounted separately.</summary>
        public long ProtectedMaxshiftPhaseBytes { get; }
        /// <summary>Payload from whole-band promotion. These bytes retain image-wide information.</summary>
        public long WholeBandPromotedBytes { get; }
        /// <summary>Payload from block-aligned promotion, containing the promoted block's full coefficient support.</summary>
        public long CodeBlockPromotedBytes { get; }
        /// <summary>Nonpromoted blocks containing zero ROI coefficients. This is a coefficient-support category.</summary>
        public long OutsideRoiCodeBlockBytes { get; }
        /// <summary>Remaining nonpromoted payload in blocks containing ROI coefficients, including lower coding phases.</summary>
        public long MixedOrRefinementBytes { get; }
        /// <summary>A subset of protected-phase bytes in blocks that mix ROI and outside coefficients.</summary>
        public long ProtectedPhaseBytesInMixedCodeBlocks { get; }

        internal SubbandPayloadTelemetry(int tile, int component, int resolutionLevel, WaveletSubband subband,
            int emittedCodeBlockCount, long payloadBytes, long protectedMaxshiftPhaseBytes,
            long wholeBandPromotedBytes, long codeBlockPromotedBytes, long outsideRoiCodeBlockBytes,
            long mixedOrRefinementBytes, long protectedPhaseBytesInMixedCodeBlocks)
        {
            Tile = tile;
            Component = component;
            ResolutionLevel = resolutionLevel;
            Subband = subband;
            EmittedCodeBlockCount = emittedCodeBlockCount;
            PayloadBytes = payloadBytes;
            ProtectedMaxshiftPhaseBytes = protectedMaxshiftPhaseBytes;
            WholeBandPromotedBytes = wholeBandPromotedBytes;
            CodeBlockPromotedBytes = codeBlockPromotedBytes;
            OutsideRoiCodeBlockBytes = outsideRoiCodeBlockBytes;
            MixedOrRefinementBytes = mixedOrRefinementBytes;
            ProtectedPhaseBytesInMixedCodeBlocks = protectedPhaseBytesInMixedCodeBlocks;
        }
    }
}

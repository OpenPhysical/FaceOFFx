namespace CoreJ2K.FaceOFFx.j2k.encoder
{
    /// <summary>Committed band payload and its fractional ROI credit under the versioned synthesis-energy model.</summary>
    internal sealed class SubbandPayloadAttribution
    {
        public int Tile { get; }
        public int Component { get; }
        public int ResolutionLevel { get; }
        public WaveletSubband Subband { get; }
        public long PayloadBytes { get; }
        public double FacePayloadByteEstimate { get; }

        internal SubbandPayloadAttribution(int tile, int component, int resolutionLevel, WaveletSubband subband,
            long payloadBytes, double facePayloadByteEstimate)
        {
            Tile = tile;
            Component = component;
            ResolutionLevel = resolutionLevel;
            Subband = subband;
            PayloadBytes = payloadBytes;
            FacePayloadByteEstimate = facePayloadByteEstimate;
        }
    }
}

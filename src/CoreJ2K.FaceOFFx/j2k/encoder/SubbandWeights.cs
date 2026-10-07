#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreJ2K.FaceOFFx.j2k.encoder
{
    /// <summary>Wavelet subband orientation, matching JPEG 2000's LL, HL, LH and HH bands.</summary>
    internal enum WaveletSubband
    {
        LL = 0,
        HL = 1,
        LH = 2,
        HH = 3
    }

    /// <summary>An SSE distortion-utility multiplier for one encoded component and subband.</summary>
    internal readonly record struct SubbandWeight(
        int Component, int ResolutionLevel, WaveletSubband Subband, double Weight);

    /// <summary>Immutable explicit allocation weights. Omitted subbands retain weight one.</summary>
    /// <remarks>
    /// Components refer to encoded components. With the RGB irreversible component transform enabled,
    /// component zero is luma and components one and two are chroma. Resolution zero is the coarsest LL;
    /// higher resolutions approach the native grid and contain HL, LH and HH. The first letter selects
    /// horizontal filtering; the second selects vertical filtering. H means high-pass and L means low-pass.
    /// A weight below one deprioritizes a band and a weight above one prioritizes it. Numeric weights
    /// multiply SSE distortion utility directly; square amplitude-domain sensitivities before supplying them.
    /// Coding-pass allocation uses these utilities while retaining the quantizer and decoder.
    /// The fixed FaceOFFx recipe applies these utilities to every coefficient.
    /// Viewing-condition, visual-quality and recognition validation are application-level requirements.
    /// </remarks>
    internal sealed class SubbandWeights
    {
        public const double MinimumWeight = 1d / 16;
        public const double MaximumWeight = 16;

        private readonly Dictionary<(int Component, int Resolution, WaveletSubband Subband), double> _weights;

        public IReadOnlyList<SubbandWeight> Entries { get; }

        internal bool HasNonNeutralWeights { get; }

        public SubbandWeights(IEnumerable<SubbandWeight> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var copied = entries.ToArray();
            _weights = new Dictionary<(int, int, WaveletSubband), double>(copied.Length);
            foreach (var entry in copied)
            {
                if (entry.Component < 0 || entry.ResolutionLevel < 0 || entry.ResolutionLevel > 16 ||
                    !Enum.IsDefined(typeof(WaveletSubband), entry.Subband) ||
                    (entry.Subband == WaveletSubband.LL) != (entry.ResolutionLevel == 0))
                    throw new ArgumentException("Specify a nonnegative component and a valid resolution/subband pair.", nameof(entries));
                if (!double.IsFinite(entry.Weight) || entry.Weight < MinimumWeight || entry.Weight > MaximumWeight)
                    throw new ArgumentOutOfRangeException(nameof(entries), "Subband weights must be finite and between 1/16 and 16.");
                if (!_weights.TryAdd((entry.Component, entry.ResolutionLevel, entry.Subband), entry.Weight))
                    throw new ArgumentException("Specify each component/resolution/subband once.", nameof(entries));
                HasNonNeutralWeights |= entry.Weight != 1;
            }
            Entries = Array.AsReadOnly(copied);
        }

        internal double GetWeight(int component, int resolution, int orientation) =>
            _weights.TryGetValue((component, resolution, (WaveletSubband)orientation), out var weight) ? weight : 1;

        internal void ValidateForImage(int components, int decompositionLevels)
        {
            foreach (var entry in Entries)
                if (entry.Component >= components || entry.ResolutionLevel > decompositionLevels)
                    throw new ArgumentException("A subband weight references a component or subband absent from this image.");
        }
    }
}

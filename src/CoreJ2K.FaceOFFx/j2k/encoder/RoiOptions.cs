#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreJ2K.FaceOFFx.j2k.encoder
{
    /// <summary>ROI priority and optional measured spatial-attribution or Maxshift-phase payload floor.</summary>
    internal sealed record RoiOptions
    {
        private IReadOnlyList<RoiRegion> _regions = Array.Empty<RoiRegion>();

        public IReadOnlyList<RoiRegion> Regions
        {
            get => _regions;
            init
            {
                if (value == null) throw new ArgumentNullException(nameof(Regions));
                var regions = value.ToArray();
                if (regions.Any(region => region == null))
                    throw new ArgumentException("ROI regions cannot contain null entries.", nameof(Regions));
                _regions = Array.AsReadOnly(regions);
            }
        }

        internal int StartResolutionLevel => 4;
        internal bool AlignToCodeBlocks => false;

        internal void ValidateForImage(int width, int height, int decompositionLevels)
        {
            if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue)
                throw new ArgumentException("The source dimensions are unsupported.");
            if (Regions.Count == 0) throw new ArgumentException("Specify at least one ROI region.", nameof(Regions));
            if (StartResolutionLevel < -1 || StartResolutionLevel > decompositionLevels)
                throw new ArgumentOutOfRangeException(nameof(StartResolutionLevel));
            foreach (var region in Regions) region.ValidateForImage(width, height);
            if (GetPixelCount(width, height) == 0)
                throw new ArgumentException("The ROI must contain at least one source pixel.", nameof(Regions));
        }

        internal long GetPixelCount(int width, int height)
        {
            if (Regions.Count == 1 && Regions[0].SimplePixelCount is { } count) return count;
            long pixelCount = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    for (var regionIndex = 0; regionIndex < Regions.Count; regionIndex++)
                    {
                        if (!Regions[regionIndex].Contains(x, y)) continue;
                        pixelCount++;
                        break;
                    }
                }
            }
            return pixelCount;
        }
    }
}

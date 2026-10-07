using System;
using System.Collections.Generic;
using CoreJ2K.FaceOFFx.j2k.encoder;
using CoreJ2K.FaceOFFx.j2k.wavelet;
using CoreJ2K.FaceOFFx.j2k.wavelet.synthesis;

namespace CoreJ2K.FaceOFFx.j2k.entropy.encoder
{
    internal sealed class SynthesisEnergyInfluence
    {
        internal const int MaximumPixels = 4 * 1024 * 1024;
        internal const int MaximumCachedBands = 8192;
        private const long MaximumWork = 250_000_000;
        private readonly bool[] _roi;
        private readonly int _width, _height, _originX, _originY;
        private readonly int _maximumCachedBands;
        private long _work;
        private readonly Dictionary<(int, int, int, int, int, int, int, int, int, int), BandEnergy> _bands =
            new Dictionary<(int, int, int, int, int, int, int, int, int, int), BandEnergy>();
        internal long RoiPixelCount { get; }

        internal sealed class BandEnergy
        {
            internal readonly int Width, Height;
            internal readonly double[] Total, Face;
            internal BandEnergy(int width, int height)
            {
                Width = width; Height = height;
                Total = new double[checked(width * height)];
                Face = new double[Total.Length];
            }
        }

        private sealed class AxisEnergy
        {
            internal int Start;
            internal double[] Squared;
            internal double Total;
        }

        internal SynthesisEnergyInfluence(RoiOptions roi, int width, int height, int originX, int originY,
            int maximumCachedBands = MaximumCachedBands)
        {
            if (roi == null) throw new ArgumentNullException(nameof(roi));
            if (width <= 0 || height <= 0 || (long)width * height > MaximumPixels || Math.Max(width, height) > 8192)
                throw new NotSupportedException("Synthesis-energy attribution supports at most 4,194,304 pixels and 8,192 samples per axis.");
            if (maximumCachedBands < 1 || maximumCachedBands > MaximumCachedBands)
                throw new ArgumentOutOfRangeException(nameof(maximumCachedBands));
            _width = width; _height = height; _originX = originX; _originY = originY;
            _maximumCachedBands = maximumCachedBands;
            _roi = new bool[checked(width * height)];
            long pixels = 0;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    foreach (var region in roi.Regions)
                        if (region.Contains(x, y)) { _roi[y * width + x] = true; pixels++; break; }
            RoiPixelCount = pixels;
            if (pixels == 0) throw new ArgumentException("Synthesis-energy attribution requires a nonempty spatial ROI.", nameof(roi));
        }

        internal BandEnergy GetBand(Subband band)
        {
            if (band == null || band.isNode || band.w <= 0 || band.h <= 0)
                throw new ArgumentException("Synthesis energy requires a nonempty leaf band.", nameof(band));
            var root = band;
            while (root.Parent != null) root = root.Parent;
            var key = (root.ulcx, root.ulcy, root.w, root.h, band.ulcx, band.ulcy,
                band.w, band.h, band.level, band.orientation);
            if (_bands.TryGetValue(key, out var cached)) return cached;
            if (_bands.Count >= _maximumCachedBands)
                throw new NotSupportedException("Synthesis-energy attribution exceeds its 8,192 cached-band study limit.");
            int tileX = root.ulcx - _originX, tileY = root.ulcy - _originY;
            if (tileX < 0 || tileY < 0 || tileX + (long)root.w > _width || tileY + (long)root.h > _height)
                throw new InvalidOperationException("Synthesis energy tile lies outside the source pixel grid.");
            var horizontal = BuildAxis(band, true);
            var vertical = BuildAxis(band, false);
            var result = new BandEnergy(band.w, band.h);
            // Project the frozen mask onto horizontal bases, then onto vertical bases.
            // Compact impulse support keeps this separable calculation bounded.
            var projection = new double[checked(band.w * root.h)];
            for (int x = 0; x < band.w; x++)
            {
                var basis = horizontal[x];
                ReserveWork((long)root.h * basis.Squared.Length);
                for (int y = 0; y < root.h; y++)
                {
                    double energy = 0;
                    int source = (tileY + y) * _width + tileX + basis.Start;
                    for (int k = 0; k < basis.Squared.Length; k++)
                        if (_roi[source + k]) energy += basis.Squared[k];
                    projection[y * band.w + x] = energy;
                }
            }
            for (int y = 0; y < band.h; y++)
            {
                var basis = vertical[y];
                ReserveWork((long)band.w * basis.Squared.Length);
                for (int x = 0; x < band.w; x++)
                {
                    double face = 0;
                    for (int k = 0; k < basis.Squared.Length; k++)
                        face += basis.Squared[k] * projection[(basis.Start + k) * band.w + x];
                    int index = y * band.w + x;
                    double total = horizontal[x].Total * basis.Total;
                    if (!double.IsFinite(total) || total <= 0 || !double.IsFinite(face) || face < 0 || face > total * (1 + 1e-12))
                        throw new InvalidOperationException("Synthesis energy exceeds finite conserved model arithmetic.");
                    result.Total[index] = total;
                    result.Face[index] = Math.Min(face, total);
                }
            }
            _bands.Add(key, result);
            return result;
        }

        private AxisEnergy[] BuildAxis(Subband band, bool horizontal)
        {
            int length = horizontal ? band.w : band.h;
            var result = new AxisEnergy[length];
            for (int coefficient = 0; coefficient < length; coefficient++)
            {
                float[] impulse = BuildAxisImpulse(band, horizontal, coefficient, ReserveWork);
                int first = 0, last = impulse.Length - 1;
                while (first < impulse.Length && impulse[first] == 0) first++;
                while (last >= first && impulse[last] == 0) last--;
                var basis = new AxisEnergy { Start = first, Squared = new double[last - first + 1] };
                for (int k = first; k <= last; k++)
                {
                    double value = impulse[k];
                    double squared = value * value;
                    basis.Squared[k - first] = squared;
                    basis.Total += squared;
                }
                if (!double.IsFinite(basis.Total) || basis.Total <= 0)
                    throw new InvalidOperationException("Synthesis impulse must have finite positive energy.");
                result[coefficient] = basis;
            }
            return result;
        }

        internal static float[] BuildAxisImpulse(Subband band, bool horizontal, int coefficient, Action<long> reserveWork = null)
        {
            int length = horizontal ? band.w : band.h;
            if (coefficient < 0 || coefficient >= length) throw new ArgumentOutOfRangeException(nameof(coefficient));
            var signal = new float[length];
            signal[coefficient] = 1;
            var filter = new SynWTFilterFloatLift9x7();
            for (var current = band; current.Parent != null; current = current.Parent)
            {
                var parent = current.Parent;
                int parentLength = horizontal ? parent.w : parent.h;
                int origin = horizontal ? parent.ulcx : parent.ulcy;
                int lowLength = (parentLength + ((origin & 1) == 0 ? 1 : 0)) / 2;
                int highLength = parentLength - lowLength;
                bool high = (current.orientation & (horizontal ? 1 : 2)) != 0;
                if (signal.Length != (high ? highLength : lowLength))
                    throw new InvalidOperationException("Synthesis impulse geometry does not match its parent band.");
                var low = high ? new float[lowLength] : signal;
                var upper = high ? signal : new float[highLength];
                var output = new float[parentLength];
                reserveWork?.Invoke(parentLength);
                if ((origin & 1) == 0)
                    filter.synthetize_lpf(low, 0, lowLength, 1, upper, 0, highLength, 1, output, 0, 1);
                else
                    filter.synthetize_hpf(low, 0, lowLength, 1, upper, 0, highLength, 1, output, 0, 1);
                signal = output;
            }
            return signal;
        }

        private void ReserveWork(long operations)
        {
            if (operations < 0 || operations > MaximumWork - _work)
                throw new NotSupportedException("Synthesis-energy attribution exceeds its 250,000,000-operation study limit.");
            _work += operations;
        }
    }
}

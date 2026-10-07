using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Transformations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>Measures decoded RGB scene evidence while preserving every source pixel.</summary>
public static class PortraitBackdropAnalyzer
{
    /// <summary>Estimates border-connected backdrop candidates around explicit foreground protection.</summary>
    public static Result<PortraitBackdropAnalysis, PipelineError> Analyze(Image<Rgba32> source,
        FacialRoiMask faceMask, PortraitForegroundProtection protection, PortraitBackdropOptions? options = null)
    {
        options ??= new PortraitBackdropOptions();
        if (source is null || faceMask is null || protection is null ||
            !ValidOptions(options) || (long)source.Width * source.Height > options.MaximumPixelCount)
            return Failure("A source, explicit foreground protection, and finite supported analysis controls are required.");
        var width = source.Width;
        var height = source.Height;
        bool Matching(FacialRoiMask? mask) => mask is null || mask.Width == width && mask.Height == height;
        if (!Matching(faceMask) || !Matching(protection.RetainedHeadMask) || !Matching(protection.ShoulderMask) ||
            (protection.RetainedHeadMask is null) == (protection.RetainedHeadEnvelope is null) ||
            !float.IsFinite(protection.ChinY) || protection.ChinY < 0 || protection.ChinY > height ||
            protection.RetainedHeadEnvelope is { } envelope &&
            (envelope.Width <= 0 || envelope.Height <= 0 || envelope.X < 0 || envelope.Y < 0 ||
             (long)envelope.X + envelope.Width > width || (long)envelope.Y + envelope.Height > height))
            return Failure("Matching masks, one complete retained-head boundary, and a source-supported chin position are required.");

        var labels = new byte[checked(width * height)];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            labels[y * width + x] = faceMask.Contains(x, y) ? (byte)PortraitRegionLabel.Face :
                protection.RetainedHeadMask?.Contains(x, y) == true ||
                protection.RetainedHeadEnvelope?.Contains(x, y) == true ||
                protection.ShoulderMask?.Contains(x, y) == true || y + 0.5f >= protection.ChinY
                    ? (byte)PortraitRegionLabel.RetainedForeground : (byte)PortraitRegionLabel.Unknown;

        // Pixel differences are measured from the native image before any scene-label filtering.
        var eligible = new byte[labels.Length];
        var border = new List<int>();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var index = y * width + x;
            if (labels[index] != (byte)PortraitRegionLabel.Unknown || source[x, y].A != 255 ||
                !LowTexture(source, x, y, options)) continue;
            eligible[index] = 1;
            if (x == 0 || y == 0 || x == width - 1 || y == height - 1) border.Add(index);
        }
        var candidateCount = 0;
        var consistency = 0d;
        if (border.Count > 0)
        {
            byte Median(int component) => border.Select(index => Channel(source[index % width, index / width], component))
                .OrderBy(value => value).ElementAt(border.Count / 2);
            var reference = new[] { Median(0), Median(1), Median(2) };
            bool Matches(int index)
            {
                var pixel = source[index % width, index / width];
                for (var component = 0; component < 3; component++)
                    if (Math.Abs(Channel(pixel, component) - reference[component]) > options.MaximumColorDistance) return false;
                return true;
            }
            var queue = new Queue<int>();
            void Enqueue(int index)
            {
                if (eligible[index] == 0 || labels[index] != (byte)PortraitRegionLabel.Unknown || !Matches(index)) return;
                labels[index] = (byte)PortraitRegionLabel.BackdropCandidate;
                queue.Enqueue(index);
            }
            foreach (var seed in border) Enqueue(seed);
            double deviation = 0;
            while (queue.TryDequeue(out var index))
            {
                candidateCount++;
                var x = index % width;
                var y = index / width;
                var pixel = source[x, y];
                for (var component = 0; component < 3; component++)
                    deviation += Math.Abs(Channel(pixel, component) - reference[component]);
                if (x > 0) Enqueue(index - 1);
                if (x + 1 < width) Enqueue(index + 1);
                if (y > 0) Enqueue(index - width);
                if (y + 1 < height) Enqueue(index + width);
            }
            consistency = candidateCount == 0 ? 0 : Math.Clamp(
                1 - deviation / (3 * candidateCount * Math.Max(1, options.MaximumColorDistance)), 0, 1);
        }
        if (candidateCount < options.MinimumCandidatePixels ||
            (double)candidateCount / labels.Length < options.MinimumCandidateFraction)
        {
            for (var index = 0; index < labels.Length; index++)
                if (labels[index] == (byte)PortraitRegionLabel.BackdropCandidate) labels[index] = (byte)PortraitRegionLabel.Unknown;
            candidateCount = 0;
            consistency = 0;
        }

        var accumulators = Enumerable.Range(0, 4).Select(_ => new PixelAccumulator()).ToArray();
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            accumulators[labels[y * width + x]].Add(source, x, y, options.EdgeDifferenceThreshold);
        var statistics = accumulators.Select((accumulator, label) => accumulator.Finish((PortraitRegionLabel)label)).ToArray();
        return Result.Success<PortraitBackdropAnalysis, PipelineError>(new PortraitBackdropAnalysis(
            new ImageDimensions(width, height), labels,
            candidateCount > 0 ? PortraitBackdropPresenceStatus.EstimatedCandidate : PortraitBackdropPresenceStatus.Undetermined,
            statistics, consistency));
    }

    private static Result<PortraitBackdropAnalysis, PipelineError> Failure(string message) =>
        Result.Failure<PortraitBackdropAnalysis, PipelineError>(new ValidationError(message, "portrait-backdrop"));

    private static bool ValidOptions(PortraitBackdropOptions options) =>
        double.IsFinite(options.MaximumNeighborGradient) && options.MaximumNeighborGradient is >= 0 and <= 255 &&
        double.IsFinite(options.MaximumLocalVariance) && options.MaximumLocalVariance is >= 0 and <= 65_025 &&
        double.IsFinite(options.MaximumColorDistance) && options.MaximumColorDistance is >= 0 and <= 255 &&
        double.IsFinite(options.EdgeDifferenceThreshold) && options.EdgeDifferenceThreshold is > 0 and <= 255 &&
        options.MinimumCandidatePixels > 0 && options.MinimumCandidatePixels <= options.MaximumPixelCount &&
        double.IsFinite(options.MinimumCandidateFraction) && options.MinimumCandidateFraction is >= 0 and <= 1 &&
        options.MaximumPixelCount is > 0 and <= 16_777_216;

    private static byte Channel(Rgba32 pixel, int component) => component == 0 ? pixel.R : component == 1 ? pixel.G : pixel.B;

    private static bool LowTexture(Image<Rgba32> source, int x, int y, PortraitBackdropOptions options)
    {
        for (var component = 0; component < 3; component++)
        {
            var gradient = NeighborDifferences(source, x, y, component).Mean;
            if (gradient > options.MaximumNeighborGradient) return false;
            double sum = 0, squareSum = 0;
            var count = 0;
            for (var sy = Math.Max(0, y - 1); sy <= Math.Min(source.Height - 1, y + 1); sy++)
            for (var sx = Math.Max(0, x - 1); sx <= Math.Min(source.Width - 1, x + 1); sx++)
            {
                var value = Channel(source[sx, sy], component);
                sum += value;
                squareSum += value * value;
                count++;
            }
            if (Math.Max(0, squareSum / count - Math.Pow(sum / count, 2)) > options.MaximumLocalVariance) return false;
        }
        return true;
    }

    private static (double Mean, int Maximum) NeighborDifferences(Image<Rgba32> source, int x, int y, int component)
    {
        var value = Channel(source[x, y], component);
        var sum = 0;
        var maximum = 0;
        var count = 0;
        void Add(int sx, int sy)
        {
            var difference = Math.Abs(value - Channel(source[sx, sy], component));
            sum += difference;
            maximum = Math.Max(maximum, difference);
            count++;
        }
        if (x > 0) Add(x - 1, y);
        if (x + 1 < source.Width) Add(x + 1, y);
        if (y > 0) Add(x, y - 1);
        if (y + 1 < source.Height) Add(x, y + 1);
        return (count == 0 ? 0 : (double)sum / count, maximum);
    }

    private sealed class PixelAccumulator
    {
        private long _count;
        private readonly double[] _sum = new double[3];
        private readonly double[] _squareSum = new double[3];
        private readonly double[] _gradient = new double[3];
        private readonly long[] _edges = new long[3];
        private readonly long[,] _bins = new long[3, 16];
        private readonly double[] _products = new double[3];

        internal void Add(Image<Rgba32> source, int x, int y, double edgeThreshold)
        {
            _count++;
            var pixel = source[x, y];
            for (var component = 0; component < 3; component++)
            {
                var value = Channel(pixel, component);
                _sum[component] += value;
                _squareSum[component] += value * value;
                _bins[component, value >> 4]++;
                var gradient = NeighborDifferences(source, x, y, component);
                _gradient[component] += gradient.Mean;
                if (gradient.Maximum >= edgeThreshold) _edges[component]++;
            }
            _products[0] += pixel.R * pixel.G;
            _products[1] += pixel.R * pixel.B;
            _products[2] += pixel.G * pixel.B;
        }

        internal PortraitRegionPixelStatistics Finish(PortraitRegionLabel label)
        {
            RgbPixelComponentStatistics Component(int component)
            {
                if (_count == 0) return new RgbPixelComponentStatistics(0, 0, 0, 0, 0);
                var mean = _sum[component] / _count;
                double entropy = 0;
                for (var bin = 0; bin < 16; bin++)
                {
                    var probability = (double)_bins[component, bin] / _count;
                    if (probability > 0) entropy -= probability * Math.Log2(probability);
                }
                return new RgbPixelComponentStatistics(mean, Math.Max(0, _squareSum[component] / _count - mean * mean),
                    entropy, _gradient[component] / _count, (double)_edges[component] / _count);
            }
            var red = Component(0);
            var green = Component(1);
            var blue = Component(2);
            RgbPixelCorrelation Correlation(int product, RgbPixelComponentStatistics first, RgbPixelComponentStatistics second)
            {
                if (_count < 2 || first.Variance <= 1e-12 || second.Variance <= 1e-12)
                    return new RgbPixelCorrelation(PixelCorrelationStatus.Undefined, 0);
                var value = (_products[product] / _count - first.Mean * second.Mean) /
                    Math.Sqrt(first.Variance * second.Variance);
                return new RgbPixelCorrelation(PixelCorrelationStatus.Defined, Math.Clamp(value, -1, 1));
            }
            return new PortraitRegionPixelStatistics(label, _count, red, green, blue,
                Correlation(0, red, green), Correlation(1, red, blue), Correlation(2, green, blue));
        }
    }
}

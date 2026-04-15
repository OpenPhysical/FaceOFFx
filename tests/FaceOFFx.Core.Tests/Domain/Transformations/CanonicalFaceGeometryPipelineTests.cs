using AwesomeAssertions;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public sealed class CanonicalFaceGeometryPipelineTests
{
    private const float SafeMargin = 112f * 0.045f;
    private const float SafeBound = 112f * (1f - 0.045f);

    [Test]
    public async Task ExtractAsync_MapsDetectorBoxIntoJawSafeChipBounds()
    {
        using var image = new Image<Rgba32>(320, 480, Color.Gray);
        var faceBox = FaceBox.Create(92, 96, 108, 144).Value;
        var coarseLandmarks = new FaceLandmarks5(
            new Point2D(122, 150),
            new Point2D(171, 147),
            new Point2D(148, 179),
            new Point2D(129, 205),
            new Point2D(168, 202));
        var detectedFace = new DetectedFace(faceBox, 0.99f, Maybe<FaceLandmarks5>.From(coarseLandmarks));

        var result = await CanonicalFaceGeometryPipeline.ExtractAsync(
            image,
            detectedFace,
            new StubLandmarkExtractor());

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var geometry = result.Value;
        var corners = new[]
        {
            new Point2D(faceBox.Left, faceBox.Top),
            new Point2D(faceBox.Right, faceBox.Top),
            new Point2D(faceBox.Right, faceBox.Bottom),
            new Point2D(faceBox.Left, faceBox.Bottom)
        };

        var mappedCorners = corners
            .Select(geometry.SourceToChip.TransformPoint)
            .ToArray();

        mappedCorners.Should().OnlyContain(point =>
            point.X >= SafeMargin - 0.01f &&
            point.X <= SafeBound + 0.01f &&
            point.Y >= SafeMargin - 0.01f &&
            point.Y <= SafeBound + 0.01f);
    }

    [Test]
    public async Task ExtractAsync_RoundTripsChipCornersThroughAdaptiveTransform()
    {
        using var image = new Image<Rgba32>(300, 420, Color.Gray);
        var faceBox = FaceBox.Create(84, 72, 126, 170).Value;
        var coarseLandmarks = new FaceLandmarks5(
            new Point2D(117, 134),
            new Point2D(177, 130),
            new Point2D(150, 167),
            new Point2D(126, 208),
            new Point2D(175, 203));
        var detectedFace = new DetectedFace(faceBox, 0.98f, Maybe<FaceLandmarks5>.From(coarseLandmarks));

        var result = await CanonicalFaceGeometryPipeline.ExtractAsync(
            image,
            detectedFace,
            new StubLandmarkExtractor());

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var geometry = result.Value;
        var corners = new[]
        {
            new Point2D(0, 0),
            new Point2D(112, 0),
            new Point2D(112, 112),
            new Point2D(0, 112)
        };

        foreach (var corner in corners)
        {
            var sourcePoint = geometry.ChipToSource.TransformPoint(corner);
            var roundTripped = geometry.SourceToChip.TransformPoint(sourcePoint);
            roundTripped.X.Should().BeApproximately(corner.X, 0.02f);
            roundTripped.Y.Should().BeApproximately(corner.Y, 0.02f);
        }
    }

    private sealed class StubLandmarkExtractor : ILandmarkExtractor
    {
        public Task<Result<FaceLandmarks68, PipelineError>> ExtractLandmarksAsync(
            Image<Rgba32> image,
            FaceBox faceBox,
            CancellationToken cancellationToken = default)
        {
            var points = Enumerable.Range(0, 68)
                .Select(index => new Point2D(
                    20f + ((index % 8) * 9f),
                    20f + ((index / 8) * 9f)))
                .ToArray();

            return Task.FromResult(Result.Success<FaceLandmarks68, PipelineError>(new FaceLandmarks68(points)));
        }
    }
}

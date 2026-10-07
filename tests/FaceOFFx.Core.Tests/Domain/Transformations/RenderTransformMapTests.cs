using AwesomeAssertions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;
using SixLabors.ImageSharp;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public class RenderTransformMapTests
{
    [Test]
    public void ForwardMapAppliesCropBeforeScaleInImageCoordinateOrder()
    {
        var source = new ImageDimensions(1000, 1200);
        var map = RenderTransformMapBuilder.CreateRotateCropResize(source, 0, source,
            new Rectangle(100, 200, 600, 800), new ImageDimensions(480, 640)).Value;
        var corner = map.MapSourceToOutput(new Point2D(100, 200));
        corner.X.Should().BeApproximately(0, 0.001f);
        corner.Y.Should().BeApproximately(0, 0.001f);
        var point = map.MapSourceToOutput(new Point2D(400, 600));
        point.X.Should().BeApproximately(240, 0.001f);
        point.Y.Should().BeApproximately(320, 0.001f);
    }

    [TestCase(768, 1376, 7f, 611, 387, 111, 149, 420, 540)]
    [TestCase(1024, 1536, 0f, 212, 283, 600, 780, 600, 600)]
    [TestCase(1536, 1024, -5.5f, 420, 180, 540, 720, 600, 750)]
    public void CreateRotateCropResize_RoundTripsOutputCornersWithinTolerance(
        int sourceWidth,
        int sourceHeight,
        float rotationDegrees,
        int cropX,
        int cropY,
        int cropWidth,
        int cropHeight,
        int outputWidth,
        int outputHeight)
    {
        var source = new ImageDimensions(sourceWidth, sourceHeight);
        var rotated = RenderTransformMapBuilder.ComputeExpandedRotationDimensions(source, rotationDegrees);
        var crop = new Rectangle(cropX, cropY, cropWidth, cropHeight);
        var output = new ImageDimensions(outputWidth, outputHeight);

        var mapResult = RenderTransformMapBuilder.CreateRotateCropResize(
            source,
            rotationDegrees,
            rotated,
            crop,
            output);

        mapResult.IsSuccess.Should().BeTrue();
        var map = mapResult.Value;

        var corners = new[]
        {
            new Point2D(0, 0),
            new Point2D(outputWidth, 0),
            new Point2D(outputWidth, outputHeight),
            new Point2D(0, outputHeight)
        };

        foreach (var corner in corners)
        {
            var sourcePoint = map.MapOutputToSource(corner);
            var roundTripped = map.MapSourceToOutput(sourcePoint);
            roundTripped.X.Should().BeApproximately(corner.X, 0.01f);
            roundTripped.Y.Should().BeApproximately(corner.Y, 0.01f);
        }
    }
}

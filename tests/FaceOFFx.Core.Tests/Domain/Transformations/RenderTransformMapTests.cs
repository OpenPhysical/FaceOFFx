using AwesomeAssertions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;
using SixLabors.ImageSharp;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public class RenderTransformMapTests
{
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

        var map = RenderTransformMapBuilder.CreateRotateCropResize(source, rotationDegrees, rotated, crop, output);

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

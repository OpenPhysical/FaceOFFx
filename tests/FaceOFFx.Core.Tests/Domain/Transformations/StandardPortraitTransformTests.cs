using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using FluentAssertions;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public class StandardPortraitTransformTests
{
    [Test]
    public void Transform_WithIcaoDimensions_ProjectsLandmarksIntoOutputSpace()
    {
        var sourceDimensions = new ImageDimensions(1024, 1536);
        var landmarks = CreateFaceLandmarks(sourceDimensions, eyeDistance: 100f);

        var result = StandardPortraitTransform.Transform(
            sourceDimensions,
            landmarks,
            new ImageDimensions(413, 531));

        result.IsSuccess.Should().BeTrue();
        result.Value.OutputDimensions.Should().Be(new ImageDimensions(413, 531));
        result.Value.Landmarks.LeftEyeCenter.X.Should().BeGreaterThan(0f);
        result.Value.Landmarks.RightEyeCenter.X.Should().BeLessThan(413f);
        result.Value.Landmarks.Points.Should().OnlyContain(point =>
            point.X >= 0f && point.X <= 413f && point.Y >= 0f && point.Y <= 531f);
    }

    private static FaceLandmarks68 CreateFaceLandmarks(ImageDimensions dims, float eyeDistance)
    {
        var centerX = dims.Width / 2f;
        var centerY = dims.Height / 2f;
        var points = new List<Point2D>();

        for (var i = 0; i < 68; i++)
        {
            var x = centerX;
            var y = centerY;

            if (i <= 16)
            {
                x = centerX + (i - 8) * 18;
                y = centerY + 180;
            }
            else if (i >= 17 && i <= 21)
            {
                x = centerX - 90 + (i - 17) * 15;
                y = centerY - 40;
            }
            else if (i >= 22 && i <= 26)
            {
                x = centerX + 30 + (i - 22) * 15;
                y = centerY - 40;
            }
            else if (i >= 36 && i <= 41)
            {
                x = centerX - eyeDistance / 2f + (i - 36) * 7;
                y = centerY - 10;
            }
            else if (i >= 42 && i <= 47)
            {
                x = centerX + eyeDistance / 2f + (i - 42) * 7;
                y = centerY - 10;
            }
            else if (i >= 48 && i <= 67)
            {
                x = centerX - 50 + (i - 48) * 5;
                y = centerY + 90;
            }

            points.Add(new Point2D(x, y));
        }

        return new FaceLandmarks68(points);
    }
}

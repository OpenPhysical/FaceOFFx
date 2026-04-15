using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using AwesomeAssertions;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests.Services;

[TestFixture]
public class StandardPortraitProcessorTests
{
    [Test]
    public void ForStandard_IcaoUsesDedicatedProfile()
    {
        var profile = StandardPortraitProfile.ForStandard("icao");

        profile.StandardName.Should().Be("ICAO");
        profile.EnableRoi.Should().BeFalse();
        profile.RequireSingleFace.Should().BeFalse();
        profile.BaseRate.Should().Be(2.0f);
    }

    [Test]
    public void CalculateCropRectangle_UsesRequestedAspectRatio()
    {
        var faceBox = FaceBox.Create(100, 120, 220, 260).Value;
        var landmarks = CreateLandmarks();

        var crop = StandardPortraitProcessorService.CalculateCropRectangle(
            faceBox,
            landmarks,
            800,
            1000,
            new ImageDimensions(413, 531));

        ((double)crop.Width / crop.Height).Should().BeApproximately(413d / 531d, 0.02d);
    }

    private static FaceLandmarks68 CreateLandmarks()
    {
        var points = Enumerable.Repeat(new Point2D(200, 240), 68).ToList();
        points[36] = new Point2D(180, 220);
        points[39] = new Point2D(210, 220);
        points[42] = new Point2D(250, 220);
        points[45] = new Point2D(280, 220);
        return new FaceLandmarks68(points);
    }
}

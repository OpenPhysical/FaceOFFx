using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Transformations;
using AwesomeAssertions;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Tests.Domain.Quality;

[TestFixture]
public class ComplianceAssessmentPipelineTests
{
    [Test]
    public async Task AssessComplianceAsync_OutputValidation_UsesTransformedPortraitGeometry()
    {
        using var image = new Image<Rgba32>(1024, 1536, Color.White);
        using var outputPortraitImage = new Image<Rgba32>(420, 560, Color.White);

        var faceBox = FaceBox.Create(350, 420, 320, 420).Value;
        var landmarks = CreateLandmarks(new ImageDimensions(image.Width, image.Height), 130f);
        var outputLandmarks = CreateLandmarks(new ImageDimensions(outputPortraitImage.Width, outputPortraitImage.Height), 100f);
        var detectedFace = new DetectedFace(faceBox, 0.99f, Maybe<FaceLandmarks5>.None);

        var inputAssessment = await ComplianceAssessmentPipeline.AssessInputComplianceAsync(
            image,
            detectedFace,
            landmarks,
            "PIV");

        var outputAssessment = await ComplianceAssessmentPipeline.AssessOutputComplianceAsync(
            image,
            detectedFace,
            landmarks,
            new OutputPortraitAssessmentInput(outputPortraitImage, outputLandmarks),
            "PIV");

        inputAssessment.IsSuccess.Should().BeTrue();
        outputAssessment.IsSuccess.Should().BeTrue();
        inputAssessment.Value.Symmetry.CountsTowardsCompliance.Should().BeFalse();
        outputAssessment.Value.Symmetry.CountsTowardsCompliance.Should().BeTrue();
        outputAssessment.Value.Geometry.InterPupillaryDistance.DistancePixels.Should().NotBeApproximately(
            inputAssessment.Value.Geometry.InterPupillaryDistance.DistancePixels,
            0.01f);
        outputAssessment.Value.Geometry.InterPupillaryDistance.DistancePixels.Should().BeApproximately(100f, 1.5f);
    }

    [Test]
    public async Task AssessComplianceAsync_InputValidation_DoesNotGateOnRawIpd()
    {
        using var image = new Image<Rgba32>(1024, 1536, Color.White);

        var faceBox = FaceBox.Create(350, 420, 320, 420).Value;
        var landmarks = CreateLandmarks(new ImageDimensions(image.Width, image.Height), 210f);
        var detectedFace = new DetectedFace(faceBox, 0.99f, Maybe<FaceLandmarks5>.None);

        var assessment = await ComplianceAssessmentPipeline.AssessInputComplianceAsync(
            image,
            detectedFace,
            landmarks,
            "PIV");

        assessment.IsSuccess.Should().BeTrue();
        assessment.Value.Geometry.InterPupillaryDistance.CountsTowardsCompliance.Should().BeFalse();
        assessment.Value.Geometry.InterPupillaryDistance.Passed.Should().BeTrue();
        assessment.Value.Geometry.InterPupillaryDistance.DistancePixels.Should().BeApproximately(210f, 1.5f);
    }

    private static FaceLandmarks68 CreateLandmarks(ImageDimensions dims, float eyeDistance)
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
                y = centerY + 220;
            }
            else if (i >= 36 && i <= 41)
            {
                x = centerX - eyeDistance / 2f + (i - 36) * 6;
                y = centerY - 15;
            }
            else if (i >= 42 && i <= 47)
            {
                x = centerX + eyeDistance / 2f + (i - 42) * 6;
                y = centerY - 15;
            }
            else if (i >= 48 && i <= 67)
            {
                x = centerX - 45 + (i - 48) * 5;
                y = centerY + 120;
            }

            points.Add(new Point2D(x, y));
        }

        return new FaceLandmarks68(points);
    }
}

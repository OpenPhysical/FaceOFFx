using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Standards;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public class PivGeometryValidationTests
{
    private static PivComplianceLines Lines(float width, GeometryEvidenceStatus status = GeometryEvidenceStatus.Measured) =>
        new(240, 256, width, new Point2D(240, 280), new Point2D(240, 400),
            new Point2D(180, 256), new Point2D(300, 256), new Point2D(240 - width / 2, 300), new Point2D(240 + width / 2, 300))
        { HeadWidthStatus = status, HeadWidthMeasurementSource = status == GeometryEvidenceStatus.Measured ? "Reviewed ear attachment fixture" : null };

    [Test]
    public void ResolutionMinimumAndFrameMarginHaveIndependentDirections()
    {
        var valid = PivComplianceValidation.Validate(Lines(240), 480, 640);
        Assert.That(valid.IsCCRatioValid, Is.True);
        Assert.That(valid.IsHeadResolutionValid, Is.True);
        Assert.That(valid.MinRequiredHeadWidth, Is.EqualTo(240));
        Assert.That(valid.IsFullyCompliant, Is.False);
        Assert.That(valid.VerificationRequirements, Is.Not.Empty);

        var oversized = PivComplianceValidation.Validate(Lines(280), 480, 640);
        Assert.That(oversized.IsCCRatioValid, Is.False);
        Assert.That(oversized.Recommendations, Has.Some.Contains("wider"));
        var undersized = PivComplianceValidation.Validate(Lines(230), 480, 640);
        Assert.That(undersized.IsCCRatioValid, Is.True);
        Assert.That(undersized.IsHeadResolutionValid, Is.False);
        Assert.That(undersized.WidthGeometryScreenPassed, Is.False);
    }

    [Test]
    public void JawContourEstimateKeepsMeasuredCcVerificationPending()
    {
        var result = PivComplianceValidation.Validate(Lines(250, GeometryEvidenceStatus.Estimated), 480, 640);
        Assert.That(result.WidthGeometryScreenPassed, Is.True);
        Assert.That(result.IsCCRatioValid, Is.False);
        Assert.That(result.IsHeadResolutionValid, Is.False);
        Assert.That(result.HeadWidthStatus, Is.EqualTo(GeometryEvidenceStatus.Estimated));
        Assert.That(result.VerificationRequirements, Has.Some.Contains("ear-to-head"));
        Assert.That(result.Summary, Does.Contain("PIV candidate"));
    }

    [Test]
    public void Width420RequiresLargerNativeCanvas()
    {
        var result = PivComplianceValidation.Validate(Lines(240), 420, 560);
        Assert.That(result.IsFrameWidthValid, Is.False);
        Assert.That(result.WidthGeometryScreenPassed, Is.False);
    }
}

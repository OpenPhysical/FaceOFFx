using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Quality;

[TestFixture]
public class PivOnlyAssessmentProfileTests
{
    [Test]
    public void CaptureAndQualityProfilesRetainPivControls()
    {
        Assert.That(DocumentCatalog.PivCapture.MaxRollDegrees, Is.EqualTo(5));
        Assert.That(DocumentCatalog.GetInputProfile("PIV-CAPTURE").Value, Is.EqualTo(DocumentCatalog.PivCapture));
        Assert.That(DocumentCatalog.PivCapture.RequireSingleFace, Is.True);
        Assert.That(QualityAssessmentOptions.ForStandard("piv").Standard, Is.SameAs(Iso19794Standard.Piv));
        Assert.That(Iso19794Standard.Piv.ExpectedDimensions, Is.EqualTo(new ImageDimensions(480, 640)));
        Assert.That(ComplianceStandards.GetSupportedStandards(), Is.EqualTo(new[] { "PIV" }));
    }

    [TestCase("twic")]
    [TestCase("icao")]
    [TestCase("cac")]
    [TestCase("passport")]
    public void UnsupportedProfilesRequireExplicitPivSelection(string profile)
    {
        Assert.That(DocumentCatalog.GetInputProfile(profile).IsFailure, Is.True);
        Assert.Throws<ArgumentException>(() => QualityAssessmentOptions.ForStandard(profile));
        Assert.That(Standard.FromName(profile), Is.Null);
        Assert.That(ComplianceStandards.IsSupported(profile), Is.False);
    }
}

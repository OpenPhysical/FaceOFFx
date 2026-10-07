using AwesomeAssertions;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public class ProfileSpecificationsTests
{
    [Test]
    public void PivProfile_UsesMinimumWholeObjectBudget()
    {
        ProfileSpecifications.Piv.Encoding.Goal.Should().BeOfType<NamedFileSizeGoal>();
        var pivGoal = (NamedFileSizeGoal)ProfileSpecifications.Piv.Encoding.Goal;
        pivGoal.Target.Id.Should().Be("minimum");
        pivGoal.Target.MaxBytes.Should().Be(11820);

    }

    [Test]
    public void PivPortraitSpec_UsesOrderedHeadWidthCandidateLadder()
    {
        var ratios = ProfileSpecifications.Piv.Portrait.HeadWidthCandidateRatios;
        ratios.Should().Equal(new[] { 225f / 420f, 215f / 420f, 235f / 420f, 210f / 420f });
    }

    [Test]
    public void PivEncoding_ContainsOnlyMediaTypeAndByteGoal()
    {
        var encoding = ProfileSpecifications.Piv.Encoding;

        encoding.MimeType.Should().Be("image/jp2");
        typeof(EncodingSpecification).GetProperties().Select(property => property.Name)
            .Should().Equal(nameof(EncodingSpecification.MimeType), nameof(EncodingSpecification.Goal));
    }

    [Test]
    public void PivBalancedBaseline_PreservesNativeGeometryAndWholeObjectBudget()
    {
        var profile = ProfileSpecifications.Piv;
        profile.Portrait.EnforcePivGeometry.Should().BeTrue();
        profile.Portrait.FaceRegion.Should().Be(PivFaceRegion.LandmarkFace);
        profile.Portrait.OutputDimensions.Width.Should().Be(480);
        profile.Portrait.OutputDimensions.Height.Should().Be(640);
        profile.Portrait.MaximumRotationDegrees.Should().Be(5);
        profile.Portrait.MinimumTopMargin.Should().Be(24);
        profile.Portrait.MinimumSideMargin.Should().Be(12);

        var target = ((NamedFileSizeGoal)profile.Encoding.Goal).Target;
        target.MaxBytes.Should().Be(11_820);
        target.CardBudget!.MaximumBiometricValueBytes.Should().Be(12_704);
        target.CardBudget.MaximumObjectBytes.Should().Be(12_710);
    }

    [Test]
    public void ByteTargetOverride_PreservesTheFixedPortraitRecipe()
    {
        var baseline = ProfileSpecifications.Piv.Encoding;
        var diagnostic = baseline with
        {
            Goal = new MaxFileSizeGoal(((NamedFileSizeGoal)baseline.Goal).Target.MaxBytes)
        };

        diagnostic.Goal.Should().BeOfType<MaxFileSizeGoal>();
        baseline.Goal.Should().BeOfType<NamedFileSizeGoal>();
        diagnostic.MimeType.Should().Be(baseline.MimeType);
    }

    [Test]
    public void ResolveForProfile_WithMinimumPivTarget_ReservesWholeRecordOverhead()
    {
        var result = ProfileFileSizeTargets.ResolveForProfile("piv", "minimum");

        result.IsSuccess.Should().BeTrue();
        result.Value.MaxBytes.Should().Be(11820);
    }

    [TestCase("")]
    [TestCase("unknown")]
    public void ResolveForProfile_WithInvalidTarget_ReturnsConfigurationFailure(string targetId)
    {
        var result = ProfileFileSizeTargets.ResolveForProfile("piv", targetId);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<FaceOFFx.Core.Domain.Common.ConfigurationError>();
    }

    [Test]
    public void WithFileSizeTarget_WithMismatchedProfile_ReturnsConfigurationFailure()
    {
        var result = ProfileSpecifications.Piv.WithFileSizeTarget(
            new ProfileFileSizeTarget("different-profile", "minimum", "Different profile", 11820, "Fixture"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<FaceOFFx.Core.Domain.Common.ConfigurationError>();
    }

    [Test]
    public void WithFileSizeTarget_WithPivTarget_ReplacesEncodingGoal()
    {
        var result = ProfileSpecifications.Piv.WithFileSizeTarget(ProfileFileSizeTargets.PivMinimum);

        result.IsSuccess.Should().BeTrue();
        result.Value.Encoding.Goal.Should().BeOfType<NamedFileSizeGoal>();
        ((NamedFileSizeGoal)result.Value.Encoding.Goal).Target.Id.Should().Be("minimum");
        result.Value.Portrait.Should().Be(ProfileSpecifications.Piv.Portrait);
    }
}

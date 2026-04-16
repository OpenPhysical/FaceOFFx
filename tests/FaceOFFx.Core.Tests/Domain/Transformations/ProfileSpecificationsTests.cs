using AwesomeAssertions;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public class ProfileSpecificationsTests
{
    [Test]
    public void BuiltInProfiles_ExposeExpectedEncodingGoals()
    {
        ProfileSpecifications.Piv.Encoding.Goal.Should().BeOfType<NamedFileSizeGoal>();
        var pivGoal = (NamedFileSizeGoal)ProfileSpecifications.Piv.Encoding.Goal;
        pivGoal.Target.Id.Should().Be("preferred");
        pivGoal.Target.MaxBytes.Should().Be(22000);

        ProfileSpecifications.Twic.Encoding.Goal.Should().BeOfType<MaxFileSizeGoal>();
        ((MaxFileSizeGoal)ProfileSpecifications.Twic.Encoding.Goal).MaxBytes.Should().Be(14000);

        ProfileSpecifications.Cac.Encoding.Goal.Should().BeOfType<ExplicitRateGoal>();
        ((ExplicitRateGoal)ProfileSpecifications.Cac.Encoding.Goal).BitsPerPixel.Should().Be(0.7f);

        ProfileSpecifications.Icao.Encoding.Goal.Should().BeOfType<ExplicitRateGoal>();
        ((ExplicitRateGoal)ProfileSpecifications.Icao.Encoding.Goal).BitsPerPixel.Should().Be(2.0f);
    }

    [Test]
    public void PivPortraitSpec_UsesOrderedHeadWidthCandidateLadder()
    {
        var ratios = ProfileSpecifications.Piv.Portrait.HeadWidthCandidateRatios;
        ratios.Should().Equal(new[] { 235f / 420f, 225f / 420f, 215f / 420f, 210f / 420f });
    }

    [Test]
    public void ResolveForProfile_WithMinimumPivTarget_ReturnsTwelveKilobyteTarget()
    {
        var result = ProfileFileSizeTargets.ResolveForProfile("piv", "minimum");

        result.IsSuccess.Should().BeTrue();
        result.Value.MaxBytes.Should().Be(12000);
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
        var result = ProfileSpecifications.Twic.WithFileSizeTarget(ProfileFileSizeTargets.PivMinimum);

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
    }
}

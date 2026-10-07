using AwesomeAssertions;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public class RegionalCompressionVerificationTests
{
    [TestCase(1, 3, 1)]
    [TestCase(7, 21, 1)]
    [TestCase(8, 24, 1)]
    [TestCase(9, 27, 2)]
    [TestCase(80_694, 242_082, 10_087)]
    public void PendingPiv_RecordsRawRgbArithmeticAndRequiredAttribution(long pixels, long rawBytes, long minimumBytes)
    {
        var evidence = RegionalCompressionVerification.PendingPiv(pixels);

        evidence.RoiPixelCount.Should().Be(pixels);
        evidence.UncompressedRegionBytes.Should().Be(rawBytes);
        evidence.MaximumCompressionRatio.Should().Be(24);
        evidence.MinimumAttributedPayloadBytes.Should().Be(minimumBytes);
        AssertPending(evidence);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void PendingPiv_RequiresPositiveRegionArea(long pixels)
    {
        Action action = () => RegionalCompressionVerification.PendingPiv(pixels);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void PendingPiv_RejectsOverflowInRawRgbLength()
    {
        Action action = () => RegionalCompressionVerification.PendingPiv(long.MaxValue / 3 + 1);
        action.Should().Throw<OverflowException>();
    }

    [Test]
    public void PendingPiv_LargeRepresentableAreaKeepsCeilingArithmeticSafe()
    {
        var pixels = long.MaxValue / 3;
        var evidence = RegionalCompressionVerification.PendingPiv(pixels);

        evidence.UncompressedRegionBytes.Should().Be(checked(3 * pixels));
        evidence.MinimumAttributedPayloadBytes.Should().Be(pixels / 8 + (pixels % 8 == 0 ? 0 : 1));
        AssertPending(evidence);
    }

    [TestCase(null, null)]
    [TestCase(null, 0L)]
    [TestCase(0, 0L)]
    [TestCase(40, 40L)]
    [TestCase(40, 200L)]
    public void CodecPayloadCounters_KeepRegionalAcceptancePending(int? requestedBytes, long? protectedBytes)
    {
        var decision = new EncodingDecision(0.3f, 200, Maybe<int>.From(250), Array.Empty<float>())
        {
            CodecEvidence = new Jpeg2000EncodingResult(new byte[200], 180, 20, 320,
                protectedBytes, "Measured protected-phase payload", 1, 1),
            RegionalCompressionVerification = RegionalCompressionVerification.PendingPiv(320)
        };

        AssertPending(decision.RegionalCompressionVerification);
        decision.RegionalCompressionVerification.MinimumAttributedPayloadBytes.Should().Be(40);
    }

    [Test]
    public void Decision_RegionalRequirementIsExplicitlyScoped()
    {
        var decision = new EncodingDecision(2, 200, Maybe<int>.None, Array.Empty<float>());

        decision.RegionalCompressionVerification.Should().BeNull();
    }

    [Test]
    public void Evidence_ExposesImmutablePendingMeasurements()
    {
        typeof(RegionalCompressionVerification).GetProperties()
            .Where(property => property.Name != "EqualityContract")
            .Should().OnlyContain(property => property.SetMethod == null || property.SetMethod.IsPrivate);
    }

    private static void AssertPending(RegionalCompressionVerification evidence)
    {
        evidence.Status.Should().Be(RegionalCompressionVerificationStatus.PendingSharedBandAttribution);
        evidence.AttributedPayloadBytes.Should().BeNull();
        evidence.MeasuredCompressionRatio.Should().BeNull();
        evidence.Statement.Should().Contain("Regional 24:1 verification pending");
        evidence.Statement.Should().Contain("shared wavelet and packet bytes");
        evidence.ProtectedPayloadInterpretation.Should().Be("Protected-phase payload is a codec allocation diagnostic.");
    }
}

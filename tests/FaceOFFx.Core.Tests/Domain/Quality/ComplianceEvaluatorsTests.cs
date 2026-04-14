using FaceOFFx.Core.Domain.Quality;
using FluentAssertions;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Quality;

[TestFixture]
public class ComplianceEvaluatorsTests
{
    [Test]
    public void EvaluateSymmetryCompliance_TreatsHigherSymmetryPercentagesAsPassing()
    {
        var measurement = new SymmetryMeasurement(
            IlluminationAsymmetryPercent: 90f,
            PoseAsymmetryPercent: 82f,
            GaborResponses: Array.Empty<float>());

        var compliance = ComplianceEvaluators.EvaluateSymmetryCompliance(
            measurement,
            ComplianceStandards.GetRulesOrDefault("PIV"));

        compliance.Passed.Should().BeTrue();
        compliance.Rejection.Should().BeNull();
    }

    [Test]
    public void EvaluateSymmetryCompliance_RejectsLowSymmetryPercentages()
    {
        var measurement = new SymmetryMeasurement(
            IlluminationAsymmetryPercent: 20f,
            PoseAsymmetryPercent: 30f,
            GaborResponses: Array.Empty<float>());

        var compliance = ComplianceEvaluators.EvaluateSymmetryCompliance(
            measurement,
            ComplianceStandards.GetRulesOrDefault("PIV"));

        compliance.Passed.Should().BeFalse();
        compliance.Rejection.Should().NotBeNull();
        compliance.Rejection!.Type.Should().Be(ComparisonType.MustBeGreaterThan);
    }

    [Test]
    public void EvaluateSymmetryCompliance_WithAdvisoryMode_DoesNotProduceRejection()
    {
        var measurement = new SymmetryMeasurement(
            IlluminationAsymmetryPercent: 10f,
            PoseAsymmetryPercent: 15f,
            GaborResponses: Array.Empty<float>());

        var compliance = ComplianceEvaluators.EvaluateSymmetryCompliance(
            measurement,
            ComplianceStandards.GetRulesOrDefault("PIV"),
            countsTowardsCompliance: false);

        compliance.Passed.Should().BeFalse();
        compliance.CountsTowardsCompliance.Should().BeFalse();
        compliance.Rejection.Should().BeNull();
    }
}

using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Transformations;
using AwesomeAssertions;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Quality;

[TestFixture]
public class QualityAcceptanceEvaluatorTests
{
    [Test]
    public void Evaluate_AcceptsAssessmentWhenItMeetsRequestedGate()
    {
        var assessment = CreateAssessment(overallValue: 0.82f, criticalViolation: false);
        var options = QualityAssessmentOptions.ForStandard("piv") with
        {
            EnforceCompliance = false,
            MinQualityThreshold = 0.8f
        };

        var result = QualityAcceptanceEvaluator.Evaluate(assessment, options);

        result.Accepted.Should().BeTrue();
        result.FailureReason.Should().BeNull();
    }

    [Test]
    public void Evaluate_RejectsAssessmentBelowRequestedThresholdEvenWhenNativeCompliancePasses()
    {
        var assessment = CreateAssessment(overallValue: 0.75f, criticalViolation: false);
        var options = QualityAssessmentOptions.ForStandard("piv") with
        {
            EnforceCompliance = false,
            MinQualityThreshold = 0.8f
        };

        assessment.IsCompliant.Should().BeTrue();

        var result = QualityAcceptanceEvaluator.Evaluate(assessment, options);

        result.Accepted.Should().BeFalse();
        result.FailureReason.Should().Be("Requested threshold not met.");
    }

    [Test]
    public void Evaluate_RejectsNonCompliantAssessmentWhenComplianceIsEnforced()
    {
        var assessment = CreateAssessment(overallValue: 0.92f, criticalViolation: true);
        var options = QualityAssessmentOptions.StrictForStandard("icao");

        var result = QualityAcceptanceEvaluator.Evaluate(assessment, options);

        result.Accepted.Should().BeFalse();
        result.FailureReason.Should().Be("Compliance requirements not met.");
    }

    [Test]
    public void StrictForStandard_PreservesRequestedStandardAndOverrideThreshold()
    {
        var options = QualityAssessmentOptions.StrictForStandard("icao", 0.9f);

        options.Standard.Should().Be(Iso19794Standard.Icao);
        options.EnforceCompliance.Should().BeTrue();
        options.MinQualityThreshold.Should().Be(0.9f);
    }

    private static Iso19794Assessment CreateAssessment(float overallValue, bool criticalViolation)
    {
        var overall = QualityScore.Create(overallValue).Value;
        var symmetry = new FacialSymmetryScore(
            QualityScore.Create(0.8f).Value,
            QualityScore.Create(0.82f).Value,
            QualityScore.Create(0.78f).Value,
            Array.Empty<float>());
        var sharpness = new SharpnessScore(
            QualityScore.Create(0.81f).Value,
            0.81f,
            new Dictionary<string, float>());
        var dimensions = new ImageDimensions(420, 560);
        var geometry = new GeometricCompliance(
            QualityScore.Create(0.83f).Value,
            QualityScore.Create(0.84f).Value,
            QualityScore.Create(0.82f).Value,
            dimensions,
            dimensions);
        var violations = criticalViolation
            ? new[] { new ComplianceViolation("Geometry", "Critical geometry issue", ViolationSeverity.Critical) }
            : Array.Empty<ComplianceViolation>();

        return new Iso19794Assessment(
            overall,
            symmetry,
            sharpness,
            geometry,
            violations,
            DateTime.UtcNow);
    }
}

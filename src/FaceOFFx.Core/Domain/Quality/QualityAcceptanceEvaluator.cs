using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality;

/// <summary>
/// Evaluates whether an assessment satisfies the caller's requested quality gate.
/// </summary>
[PublicAPI]
public static class QualityAcceptanceEvaluator
{
    /// <summary>
    /// Evaluates an assessment against the supplied options.
    /// </summary>
    public static QualityAcceptanceResult Evaluate(
        Iso19794Assessment assessment,
        QualityAssessmentOptions options)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(options);

        if (options.EnforceCompliance && !assessment.IsCompliant)
        {
            return new QualityAcceptanceResult(
                false,
                "Compliance requirements not met.");
        }

        if (assessment.Overall.Value < options.MinQualityThreshold)
        {
            return new QualityAcceptanceResult(
                false,
                "Requested threshold not met.");
        }

        return new QualityAcceptanceResult(true, null);
    }
}

/// <summary>
/// Result of evaluating an assessment against requested quality requirements.
/// </summary>
[PublicAPI]
public sealed record QualityAcceptanceResult(bool Accepted, string? FailureReason);

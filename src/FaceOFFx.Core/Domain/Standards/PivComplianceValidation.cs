using JetBrains.Annotations;
using FaceOFFx.Core.Domain.Detection;

namespace FaceOFFx.Core.Domain.Standards;

/// <summary>
/// Records selected PIV candidate geometry checks and the remaining anatomical and capture verification.
/// </summary>
/// <param name="IsAAAligned">True if nose and mouth centers align with the vertical center line</param>
/// <param name="IsBBPositioned">True if eye line is positioned 50-70% from bottom edge</param>
/// <param name="IsCCRatioValid">True if head width meets minimum 7:4 ratio (image width : head width)</param>
/// <param name="IsFullyCompliant">External complete-conformance assertion; automatic line validation records remaining verification.</param>
/// <param name="AADeviation">Deviation of face center line from image center in pixels</param>
/// <param name="BBFromBottom">Actual percentage of eye line from bottom edge (0.5-0.7 is compliant)</param>
/// <param name="CCRatio">Current image width : head width ratio (must be ≥ 1.75 for 7:4)</param>
/// <param name="HeadWidthPixels">Actual head width in pixels</param>
/// <param name="MinRequiredHeadWidth">NIST minimum ear-attachment width, 240 pixels.</param>
/// <param name="Issues">List of specific compliance issues found</param>
/// <param name="Recommendations">List of recommended corrections</param>
[PublicAPI]
public record PivComplianceValidation(
    bool IsAAAligned,
    bool IsBBPositioned,
    bool IsCCRatioValid,
    bool IsFullyCompliant,
    float AADeviation,
    float BBFromBottom,
    float CCRatio,
    float HeadWidthPixels,
    float MinRequiredHeadWidth,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Recommendations
)
{
    /// <summary>Measurement basis for the displayed CC width.</summary>
    public GeometryEvidenceStatus HeadWidthStatus { get; init; }
    /// <summary>Reports whether the frame width exceeds 420 pixels.</summary>
    public bool IsFrameWidthValid { get; init; }
    /// <summary>Reports a measured anatomical width of at least 240 pixels.</summary>
    public bool IsHeadResolutionValid { get; init; }
    /// <summary>Screening result using the available width value and its recorded basis.</summary>
    public bool WidthGeometryScreenPassed { get; init; }
    /// <summary>Acquisition and anatomical checks required beyond the measured lines.</summary>
    public IReadOnlyList<string> VerificationRequirements { get; init; } = [];
    /// <summary>
    /// PIV compliance thresholds and constants.
    /// </summary>
    public static class Thresholds
    {
        /// <summary>Maximum allowed deviation from center line in pixels.</summary>
        public const float MaxCenterDeviation = 10.0f;

        /// <summary>Minimum percentage from bottom for eye line.</summary>
        public const float MinEyeFromBottom = 0.50f;

        /// <summary>Maximum percentage from bottom for eye line.</summary>
        public const float MaxEyeFromBottom = 0.70f;

        /// <summary>Optimal percentage from bottom for eye line.</summary>
        public const float OptimalEyeFromBottom = 0.60f;

        /// <summary>Minimum image width to head width ratio (7:4 = 1.75).</summary>
        public const float MinWidthRatio = 7.0f / 4.0f; // 1.75

        /// <summary>Optimal image width to head width ratio.</summary>
        public const float OptimalWidthRatio = 1.85f;

        /// <summary>Maximum allowed alignment deviation for nose/mouth centers.</summary>
        public const float MaxAlignmentDeviation = 8.0f;
    }

    /// <summary>
    /// Creates a PIV compliance validation from PIV lines and image dimensions.
    /// </summary>
    /// <param name="lines">The PIV compliance lines</param>
    /// <param name="imageWidth">Width of the image</param>
    /// <param name="imageHeight">Height of the image</param>
    /// <returns>Complete validation results</returns>
    public static PivComplianceValidation Validate(
        PivComplianceLines lines,
        int imageWidth,
        int imageHeight
    )
    {
        var issues = new List<string>();
        var recommendations = new List<string>();

        // Validate Line AA (Vertical Center Line)
        var aaDeviation = lines.GetCenterLineDeviation(imageWidth);
        var isAAAligned =
            Math.Abs(aaDeviation) <= Thresholds.MaxCenterDeviation
            && lines.AreNoseAndMouthAligned(Thresholds.MaxAlignmentDeviation);

        if (!isAAAligned)
        {
            if (Math.Abs(aaDeviation) > Thresholds.MaxCenterDeviation)
            {
                issues.Add(
                    $"Face center line deviates {aaDeviation:F1}px from image center (max: {Thresholds.MaxCenterDeviation}px)"
                );
                recommendations.Add(
                    $"Adjust horizontal crop to center face (shift {-aaDeviation:F1}px)"
                );
            }

            if (!lines.AreNoseAndMouthAligned(Thresholds.MaxAlignmentDeviation))
            {
                var noseMouthDev = Math.Abs(lines.NoseCenter.X - lines.MouthCenter.X);
                issues.Add(
                    $"Nose and mouth not aligned: {noseMouthDev:F1}px deviation (max: {Thresholds.MaxAlignmentDeviation}px)"
                );
                recommendations.Add("Check facial landmark detection quality or face pose");
            }
        }

        // Validate Line BB (Horizontal Eye Line)
        var bbFromBottom = lines.GetEyeLinePercentageFromBottom(imageHeight);
        var isBBPositioned =
            bbFromBottom >= Thresholds.MinEyeFromBottom
            && bbFromBottom <= Thresholds.MaxEyeFromBottom;

        if (!isBBPositioned)
        {
            if (bbFromBottom < Thresholds.MinEyeFromBottom)
            {
                issues.Add(
                    $"Eyes too low: {bbFromBottom:P1} from bottom (min: {Thresholds.MinEyeFromBottom:P1})"
                );
                recommendations.Add(
                    $"Adjust vertical crop to raise eye position by {(Thresholds.OptimalEyeFromBottom - bbFromBottom) * imageHeight:F0}px"
                );
            }
            else
            {
                issues.Add(
                    $"Eyes too high: {bbFromBottom:P1} from bottom (max: {Thresholds.MaxEyeFromBottom:P1})"
                );
                recommendations.Add(
                    $"Adjust vertical crop to lower eye position by {(bbFromBottom - Thresholds.OptimalEyeFromBottom) * imageHeight:F0}px"
                );
            }
        }

        // Validate Line CC (Head Width Ratio)
        var ccRatio = lines.GetImageToHeadWidthRatio(imageWidth);
        const float minRequiredHeadWidth = 240;
        var maxAllowedHeadWidth = imageWidth / Thresholds.MinWidthRatio;
        // Use proper floating point comparison with epsilon tolerance
        const float epsilon = 0.001f; // Small tolerance for floating point precision
        var ratioWithinBounds = float.IsFinite(ccRatio) && ccRatio >= Thresholds.MinWidthRatio - epsilon;
        var headWidthWithinBounds = float.IsFinite(lines.LineCC_Width) && lines.LineCC_Width >= minRequiredHeadWidth;
        var measuredWidth = lines.HeadWidthStatus == GeometryEvidenceStatus.Measured &&
            !string.IsNullOrWhiteSpace(lines.HeadWidthMeasurementSource);
        var isCCRatioValid = measuredWidth && ratioWithinBounds;
        var frameWidthValid = imageWidth > 420;

        if (!ratioWithinBounds)
        {
            issues.Add(
                $"Head width ratio {ccRatio:F2} below minimum {Thresholds.MinWidthRatio:F2} (7:4)"
            );
            issues.Add(
                $"Width value {lines.LineCC_Width:F0}px exceeds the maximum {maxAllowedHeadWidth:F0}px for this frame"
            );
            recommendations.Add(
                "Retain a wider source-supported crop to provide the required head-width margin"
            );
        }
        if (!headWidthWithinBounds)
        {
            issues.Add($"Width value {lines.LineCC_Width:F0}px is below the 240-pixel anatomical-resolution threshold");
            recommendations.Add("Use a native-resolution source and verify at least 240 pixels across the ear-to-head attachments");
        }
        if (!frameWidthValid)
        {
            issues.Add($"Frame width {imageWidth}px requires a native-resolution canvas wider than 420 pixels");
            recommendations.Add("Select a source-supported canvas with width greater than 420 pixels");
        }
        var requirements = new List<string>
        {
            "Verify the true crown-to-chin height, complete head and shoulder coverage, and optical acquisition requirements.",
            "Verify frontal pose, neutral expression, scene quality, source history, color encoding, and final signed-record size."
        };
        if (!measuredWidth) requirements.Add("Measure CC at the ear-to-head attachments and record the measurement source.");

        return new PivComplianceValidation(
            isAAAligned,
            isBBPositioned,
            isCCRatioValid,
            false,
            aaDeviation,
            bbFromBottom,
            ccRatio,
            lines.LineCC_Width,
            minRequiredHeadWidth,
            issues,
            recommendations
        )
        {
            HeadWidthStatus = measuredWidth ? GeometryEvidenceStatus.Measured : GeometryEvidenceStatus.Estimated,
            IsFrameWidthValid = frameWidthValid,
            IsHeadResolutionValid = measuredWidth && headWidthWithinBounds,
            WidthGeometryScreenPassed = ratioWithinBounds && headWidthWithinBounds && frameWidthValid,
            VerificationRequirements = requirements.AsReadOnly()
        };
    }

    /// <summary>
    /// Gets a severity level for the compliance issues.
    /// </summary>
    public ComplianceSeverity Severity
    {
        get
        {
            if (IsFullyCompliant)
                return ComplianceSeverity.Compliant;
            if (Issues.Count >= 3)
                return ComplianceSeverity.Critical;
            if (!WidthGeometryScreenPassed)
                return ComplianceSeverity.High;
            if (!IsBBPositioned)
                return ComplianceSeverity.Medium;
            return ComplianceSeverity.Low;
        }
    }

    /// <summary>
    /// Gets a summary of compliance status for logging.
    /// </summary>
    public string Summary =>
        IsFullyCompliant
            ? "Externally verified PIV conformance"
            : $"PIV candidate: {Issues.Count} geometry issues; {VerificationRequirements.Count} verification requirements ({HeadWidthStatus} CC width)";

    /// <summary>
    /// Gets detailed compliance report for debugging.
    /// </summary>
    public string DetailedReport
    {
        get
        {
            var report = new List<string>
            {
                $"PIV Candidate Geometry Report:",
                $"  Line AA (Center): {(IsAAAligned ? "✓" : "✗")} Deviation: {AADeviation:F1}px",
                $"  Line BB (Eyes): {(IsBBPositioned ? "✓" : "✗")} Position: {BBFromBottom:P1} from bottom",
                $"  Line CC (Width): {HeadWidthStatus}; ratio {CCRatio:F2}, width {HeadWidthPixels:F0}px",
                $"  Overall: {Summary}",
            };

            if (Issues.Any())
            {
                report.Add("  Issues:");
                report.AddRange(Issues.Select(issue => $"    - {issue}"));
            }

            if (Recommendations.Any())
            {
                report.Add("  Recommendations:");
                report.AddRange(Recommendations.Select(rec => $"    - {rec}"));
            }
            if (VerificationRequirements.Any())
            {
                report.Add("  Required verification:");
                report.AddRange(VerificationRequirements.Select(requirement => $"    - {requirement}"));
            }

            return string.Join(Environment.NewLine, report);
        }
    }
}

/// <summary>
/// Severity levels for PIV compliance issues.
/// </summary>
[PublicAPI]
public enum ComplianceSeverity
{
    /// <summary>Fully compliant with all requirements.</summary>
    Compliant,

    /// <summary>Minor deviations that don't affect core requirements.</summary>
    Low,

    /// <summary>Moderate issues that may affect recognition quality.</summary>
    Medium,

    /// <summary>Significant deviations from requirements.</summary>
    High,

    /// <summary>Critical failures that prevent PIV compliance.</summary>
    Critical,
}

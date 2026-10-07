// MIT License
// 
// Copyright (c) 2025 FaceOFFx Contributors
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using CSharpFunctionalExtensions;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality;

/// <summary>
/// Compliance standard for facial image assessment
/// </summary>
[PublicAPI]
public record Standard(string Name, string DisplayName, string Description)
{
    /// <summary>
    /// Personal Identity Verification standard
    /// </summary>
    public static readonly Standard Piv = new("PIV", "PIV", "Personal Identity Verification cards");
    
    /// <summary>
    /// Get a standard by name (case insensitive)
    /// </summary>
    /// <param name="name">The standard name</param>
    /// <returns>The matching standard or null if not found</returns>
    public static Standard? FromName(string name) => name.ToUpperInvariant() switch
    {
        "PIV" => Piv,
        _ => null
    };
}

/// <summary>
/// Comparison type for compliance evaluation
/// </summary>
[PublicAPI]
public enum ComparisonType
{
    /// <summary>
    /// Value must be greater than the threshold
    /// </summary>
    MustBeGreaterThan,
    /// <summary>
    /// Value must be less than the threshold
    /// </summary>
    MustBeLessThan,
    /// <summary>
    /// Value must be within a specified range
    /// </summary>
    MustBeInRange,
    /// <summary>
    /// Value must equal the threshold exactly
    /// </summary>
    MustEqual
}

/// <summary>
/// Unit type for rejection reason formatting
/// </summary>
[PublicAPI]
public enum MetricUnit
{
    /// <summary>
    /// Value is measured as a percentage (0-100)
    /// </summary>
    Percentage,
    /// <summary>
    /// Value is measured in pixels
    /// </summary>
    Pixels,
    /// <summary>
    /// Value has no specific unit
    /// </summary>
    None
}

/// <summary>
/// Specific rejection reason with actionable information
/// </summary>
[PublicAPI]
public record RejectionReason(
    string Metric,
    float ActualValue,
    float RequiredValue,
    Standard Standard,
    ComparisonType Type,
    MetricUnit Unit,
    string Message)
{
    /// <summary>
    /// Creates a rejection reason with appropriate formatting based on the metric unit
    /// </summary>
    /// <param name="metric">The metric that failed</param>
    /// <param name="actual">The actual measured value</param>
    /// <param name="required">The required threshold value</param>
    /// <param name="standard">The compliance standard</param>
    /// <param name="type">The type of comparison</param>
    /// <param name="unit">The unit of measurement</param>
    /// <returns>A formatted rejection reason</returns>
    public static RejectionReason Create(
        string metric,
        float actual,
        float required,
        Standard standard,
        ComparisonType type,
        MetricUnit unit = MetricUnit.Percentage)
    {
        var (actualFormatted, requiredFormatted, unitSuffix) = unit switch
        {
            MetricUnit.Percentage => ($"{actual:F1}%", $"{required:F0}%", "%"),
            MetricUnit.Pixels => ($"{actual:F1}px", $"{required:F0}px", "px"),
            MetricUnit.None => ($"{actual:F1}", $"{required:F1}", ""),
            _ => ($"{actual:F1}", $"{required:F1}", "")
        };

        var message = type switch
        {
            ComparisonType.MustBeGreaterThan => 
                $"REJECTED: {actualFormatted} {metric.ToLower()} < {requiredFormatted} {standard.Name} minimum",
            ComparisonType.MustBeLessThan => 
                $"REJECTED: {actualFormatted} {metric.ToLower()} > {requiredFormatted} {standard.Name} maximum",
            ComparisonType.MustBeInRange => 
                $"REJECTED: {actualFormatted} {metric.ToLower()} outside {standard.Name} range",
            ComparisonType.MustEqual => 
                $"REJECTED: {metric} {actualFormatted} does not match {standard.Name} requirements",
            _ => $"REJECTED: {metric} {actualFormatted} violates {standard.Name} requirements"
        };
        
        return new RejectionReason(metric, actual, required, standard, type, unit, message);
    }
}

/// <summary>
/// Symmetry compliance assessment with specific pass/fail criteria
/// </summary>
[PublicAPI]
public record SymmetryCompliance(
    bool Passed,
    float IlluminationAsymmetryPercent,
    float PoseAsymmetryPercent,
    float RequiredThreshold,
    Maybe<RejectionReason> Rejection = default,
    bool CountsTowardsCompliance = true);

/// <summary>
/// Sharpness compliance assessment with specific pass/fail criteria
/// </summary>
[PublicAPI]
public record SharpnessCompliance(
    bool Passed,
    float OverallSharpnessPercent,
    float RequiredThreshold,
    IReadOnlyDictionary<string, float> RegionalScores,
    Maybe<RejectionReason> Rejection = default);

/// <summary>
/// Head size compliance assessment
/// </summary>
[PublicAPI]
public record HeadSizeCompliance(
    bool Passed,
    float SizePercent,
    float MinPercent,
    float MaxPercent,
    Maybe<RejectionReason> Rejection = default);

/// <summary>
/// Centering compliance assessment
/// </summary>
[PublicAPI]
public record CenteringCompliance(
    bool Passed,
    float CenteringPercent,
    float RequiredThreshold,
    Maybe<RejectionReason> Rejection = default);

/// <summary>
/// Inter-pupillary distance compliance assessment
/// </summary>
[PublicAPI]
public record IpdCompliance(
    bool Passed,
    float DistancePixels,
    float MinPixels,
    float MaxPixels,
    Maybe<RejectionReason> Rejection = default,
    bool CountsTowardsCompliance = true);

/// <summary>
/// Geometry compliance assessment combining all geometric metrics
/// </summary>
[PublicAPI]
public record GeometryCompliance(
    bool Passed,
    HeadSizeCompliance HeadSize,
    CenteringCompliance Centering,
    IpdCompliance InterPupillaryDistance,
    Maybe<RejectionReason> Rejection = default);

/// <summary>
/// Complete compliance assessment with clear pass/fail per metric
/// </summary>
[PublicAPI]
public record ComplianceAssessment(
    SymmetryCompliance Symmetry,
    SharpnessCompliance Sharpness,
    GeometryCompliance Geometry,
    Standard AppliedStandard,
    IReadOnlyList<RejectionReason> Rejections,
    DateTime AssessmentTime = default)
{
    /// <summary>
    /// True if all metrics pass compliance requirements
    /// </summary>
    public bool IsCompliant =>
        (!Symmetry.CountsTowardsCompliance || Symmetry.Passed)
        && Sharpness.Passed
        && Geometry.Passed;
    
    /// <summary>
    /// Summary of compliance status
    /// </summary>
    public string Summary => IsCompliant 
        ? $"✅ COMPLIANT for {AppliedStandard.Name} use"
        : $"❌ REJECTED for {AppliedStandard.Name} use ({Rejections.Count} violations)";
        
    /// <summary>
    /// Creates assessment with default timestamp
    /// </summary>
    public ComplianceAssessment(
        SymmetryCompliance symmetry,
        SharpnessCompliance sharpness,
        GeometryCompliance geometry,
        Standard appliedStandard,
        IReadOnlyList<RejectionReason> rejections) 
        : this(symmetry, sharpness, geometry, appliedStandard, rejections, DateTime.UtcNow)
    {
    }
}

/// <summary>
/// Raw measurement data before compliance evaluation
/// </summary>
[PublicAPI]
public record SymmetryMeasurement(
    float IlluminationAsymmetryPercent,
    float PoseAsymmetryPercent,
    IReadOnlyList<float> GaborResponses);

/// <summary>
/// Raw sharpness measurement data before compliance evaluation
/// </summary>
[PublicAPI]
public record SharpnessMeasurement(
    float OverallSharpnessPercent,
    float HighFrequencyRatio,
    IReadOnlyDictionary<string, float> RegionalScores);

/// <summary>
/// Raw geometry measurement data before compliance evaluation
/// </summary>
[PublicAPI]
public record GeometryMeasurement(
    float HeadSizePercent,
    float CenteringPercent,
    float IpdPixels,
    int ImageWidth,
    int ImageHeight);

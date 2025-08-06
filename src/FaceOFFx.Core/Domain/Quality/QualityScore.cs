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
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality;

/// <summary>
/// Represents a normalized quality score between 0 and 1
/// </summary>
[PublicAPI]
public record QualityScore
{
    /// <summary>
    /// The quality score value between 0 (worst) and 1 (best)
    /// </summary>
    public float Value { get; }

    private QualityScore(float value)
    {
        Value = value;
    }

    /// <summary>
    /// Creates a new quality score with validation
    /// </summary>
    public static Result<QualityScore> Create(float value) =>
        value is >= 0f and <= 1f
            ? Result.Success(new QualityScore(value))
            : Result.Failure<QualityScore>($"Quality score must be between 0 and 1, got {value}");

    /// <summary>
    /// Creates a quality score from a percentage (0-100)
    /// </summary>
    public static Result<QualityScore> FromPercentage(float percentage) =>
        Create(percentage / 100f);

    /// <summary>
    /// Gets the score as a percentage (0-100)
    /// </summary>
    public float AsPercentage() => Value * 100f;

    /// <summary>
    /// Implicit conversion to float for easier calculations
    /// </summary>
    public static implicit operator float(QualityScore score) => score.Value;

    /// <summary>
    /// Returns the quality score formatted as a percentage
    /// </summary>
    public override string ToString() => $"{Value:P1}";
}

/// <summary>
/// Represents facial symmetry assessment results
/// </summary>
[PublicAPI]
public record FacialSymmetryScore(
    QualityScore Overall,
    QualityScore Illumination,
    QualityScore Pose,
    IReadOnlyList<float> GaborResponses
)
{
    /// <summary>
    /// Creates a facial symmetry score from raw asymmetry values
    /// </summary>
    public static Result<FacialSymmetryScore> FromAsymmetryValues(
        float illuminationAsymmetry,
        float poseAsymmetry,
        IReadOnlyList<float> gaborResponses)
    {
        // Convert asymmetry to quality scores (lower asymmetry = higher quality)
        var illuminationResult = QualityScore.Create(1f - MathF.Min(illuminationAsymmetry, 1f));
        var poseResult = QualityScore.Create(1f - MathF.Min(poseAsymmetry, 1f));
        
        return Result.Combine(illuminationResult, poseResult)
            .Bind(() =>
            {
                var overall = (illuminationResult.Value.Value + poseResult.Value.Value) / 2f;
                return QualityScore.Create(overall)
                    .Map(overallScore => new FacialSymmetryScore(
                        overallScore,
                        illuminationResult.Value,
                        poseResult.Value,
                        gaborResponses));
            });
    }
}

/// <summary>
/// Represents image sharpness assessment results
/// </summary>
[PublicAPI]
public record SharpnessScore(
    QualityScore Overall,
    float HighFrequencyRatio,
    IReadOnlyDictionary<string, float> RegionalScores
)
{
    /// <summary>
    /// Creates a sharpness score from DCT analysis results
    /// </summary>
    public static Result<SharpnessScore> FromDctAnalysis(
        float highFrequencyRatio,
        IReadOnlyDictionary<string, float> regionalScores)
    {
        // Higher frequency content = sharper image
        return QualityScore.Create(highFrequencyRatio)
            .Map(overall => new SharpnessScore(overall, highFrequencyRatio, regionalScores));
    }
}

/// <summary>
/// Represents geometric compliance with ISO/IEC 19794-5
/// </summary>
[PublicAPI]
public record GeometricCompliance(
    QualityScore HeadSize,
    QualityScore Centering,
    QualityScore InterPupillaryDistance,
    ImageDimensions ActualDimensions,
    ImageDimensions ExpectedDimensions
)
{
    /// <summary>
    /// Gets the overall geometric compliance score
    /// </summary>
    public QualityScore Overall => QualityScore.Create(
        (HeadSize.Value + Centering.Value + InterPupillaryDistance.Value) / 3f
    ).Value;
}

/// <summary>
/// Represents a compliance violation
/// </summary>
[PublicAPI]
public record ComplianceViolation(
    string Category,
    string Description,
    ViolationSeverity Severity,
    RoiBoundingBox? AffectedRegion = null
);

/// <summary>
/// Violation severity levels
/// </summary>
[PublicAPI]
public enum ViolationSeverity
{
    /// <summary>
    /// Minor issue that doesn't prevent usage
    /// </summary>
    Minor,
    
    /// <summary>
    /// Moderate issue that may affect quality
    /// </summary>
    Moderate,
    
    /// <summary>
    /// Critical issue that prevents compliance
    /// </summary>
    Critical
}

/// <summary>
/// Complete ISO/IEC 19794-5 quality assessment
/// </summary>
[PublicAPI]
public record Iso19794Assessment(
    QualityScore Overall,
    FacialSymmetryScore Symmetry,
    SharpnessScore Sharpness,
    GeometricCompliance Geometry,
    IReadOnlyList<ComplianceViolation> Violations,
    DateTime AssessmentTime
)
{
    /// <summary>
    /// Determines if the image is compliant with ISO/IEC 19794-5
    /// </summary>
    public bool IsCompliant => Overall.Value >= 0.7f && 
        !Violations.Any(v => v.Severity == ViolationSeverity.Critical);

    /// <summary>
    /// Gets a summary of the assessment
    /// </summary>
    public string Summary => IsCompliant
        ? $"Compliant (Quality: {Overall})"
        : $"Non-compliant (Quality: {Overall}, Critical violations: {Violations.Count(v => v.Severity == ViolationSeverity.Critical)})";
}
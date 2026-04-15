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
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Quality.Assessors;
using FaceOFFx.Core.Domain.Transformations;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Domain.Quality;

/// <summary>
/// Pipeline for performing comprehensive ISO/IEC 19794-5 quality assessment
/// </summary>
[PublicAPI]
public static class QualityAssessmentPipeline
{
    private const float CenterRegionCriticalThreshold = 0.4f;

    /// <summary>
    /// Performs a complete quality assessment on a facial image
    /// </summary>
    public static async Task<Result<Iso19794Assessment, PipelineError>> AssessAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        QualityAssessmentOptions options)
    {
        var optionsValidation = options.Validate();
        if (optionsValidation.IsFailure)
        {
            return Result.Failure<Iso19794Assessment, PipelineError>(
                new ValidationError($"Invalid assessment options: {optionsValidation.Error}", options.Standard.Name));
        }

        return options.EnableParallelAssessment
            ? await AssessParallelAsync(image, face, landmarks, options).ConfigureAwait(false)
            : await AssessSequentialAsync(image, face, landmarks, options).ConfigureAwait(false);
    }

    private static async Task<Result<Iso19794Assessment, PipelineError>> AssessParallelAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        QualityAssessmentOptions options)
    {
        var faceRoi = GetFaceRoi(image, landmarks);
        var symmetryTask = Task.Run(() => SymmetryAssessor.Assess(image, landmarks));
        var sharpnessTask = Task.Run(() => AssessSharpness(image, faceRoi));
        var geometryTask = Task.Run(() =>
            GeometryAssessor.Assess(
                new ImageDimensions(image.Width, image.Height),
                landmarks,
                options.Standard));

        await Task.WhenAll(symmetryTask, sharpnessTask, geometryTask).ConfigureAwait(false);

        return CombineResults(
            await symmetryTask.ConfigureAwait(false),
            await sharpnessTask.ConfigureAwait(false),
            await geometryTask.ConfigureAwait(false),
            options);
    }

    private static Task<Result<Iso19794Assessment, PipelineError>> AssessSequentialAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        QualityAssessmentOptions options)
    {
        var faceRoi = GetFaceRoi(image, landmarks);
        var symmetryResult = SymmetryAssessor.Assess(image, landmarks);
        var sharpnessResult = AssessSharpness(image, faceRoi);
        var geometryResult = GeometryAssessor.Assess(
            new ImageDimensions(image.Width, image.Height),
            landmarks,
            options.Standard);

        return Task.FromResult(CombineResults(
            symmetryResult,
            sharpnessResult,
            geometryResult,
            options));
    }

    private static Result<Iso19794Assessment, PipelineError> CombineResults(
        Result<FacialSymmetryScore> symmetryResult,
        Result<SharpnessScore> sharpnessResult,
        Result<GeometricCompliance> geometryResult,
        QualityAssessmentOptions options)
    {
        var symmetry = symmetryResult.ToPipelineResult(
            error => new ValidationError($"Symmetry assessment failed: {error}", options.Standard.Name));
        if (symmetry.IsFailure)
        {
            return Result.Failure<Iso19794Assessment, PipelineError>(symmetry.Error);
        }

        var sharpness = sharpnessResult.ToPipelineResult(
            error => new ValidationError($"Sharpness assessment failed: {error}", options.Standard.Name));
        if (sharpness.IsFailure)
        {
            return Result.Failure<Iso19794Assessment, PipelineError>(sharpness.Error);
        }

        var geometry = geometryResult.ToPipelineResult(
            error => new ValidationError($"Geometry assessment failed: {error}", options.Standard.Name));
        if (geometry.IsFailure)
        {
            return Result.Failure<Iso19794Assessment, PipelineError>(geometry.Error);
        }

        var overallScore = CalculateOverallScore(symmetry.Value, sharpness.Value, geometry.Value)
            .ToPipelineResult(error => new ValidationError($"Overall quality score calculation failed: {error}", options.Standard.Name));
        if (overallScore.IsFailure)
        {
            return Result.Failure<Iso19794Assessment, PipelineError>(overallScore.Error);
        }

        var violations = CollectViolations(symmetry.Value, sharpness.Value, geometry.Value, options);

        return Result.Success<Iso19794Assessment, PipelineError>(new Iso19794Assessment(
            overallScore.Value,
            symmetry.Value,
            sharpness.Value,
            geometry.Value,
            violations,
            DateTime.UtcNow));
    }

    private static Maybe<Rectangle> GetFaceRoi(Image<Rgba32> image, FaceLandmarks68 landmarks)
    {
        var faceRoiResult = landmarks.CalculateFaceRoi();
        if (faceRoiResult.IsFailure)
        {
            return Maybe<Rectangle>.None;
        }

        var faceRoi = Rectangle.Intersect(
            faceRoiResult.Value,
            new Rectangle(0, 0, image.Width, image.Height));

        return faceRoi.Width > 0 && faceRoi.Height > 0
            ? Maybe<Rectangle>.From(faceRoi)
            : Maybe<Rectangle>.None;
    }

    private static Result<SharpnessScore> AssessSharpness(Image<Rgba32> image, Maybe<Rectangle> faceRoi) =>
        faceRoi.HasValue
            ? SharpnessAssessor.Assess(image, faceRoi.Value)
            : SharpnessAssessor.Assess(image);

    private static List<ComplianceViolation> CollectViolations(
        FacialSymmetryScore symmetry,
        SharpnessScore sharpness,
        GeometricCompliance geometry,
        QualityAssessmentOptions options)
    {
        var violations = new List<ComplianceViolation>();

        if (symmetry.Overall.Value < 0.6f)
        {
            violations.Add(new ComplianceViolation(
                "Symmetry",
                $"Facial symmetry score {symmetry.Overall} is below acceptable threshold",
                symmetry.Overall.Value < 0.4f ? ViolationSeverity.Critical : ViolationSeverity.Moderate));
        }

        if (symmetry.Illumination.Value < 0.5f)
        {
            violations.Add(new ComplianceViolation(
                "Illumination",
                "Uneven illumination detected",
                ViolationSeverity.Moderate));
        }

        if (symmetry.Pose.Value < 0.5f)
        {
            violations.Add(new ComplianceViolation(
                "Pose",
                "Non-frontal pose detected",
                ViolationSeverity.Moderate));
        }

        if (sharpness.Overall.Value < 0.5f)
        {
            violations.Add(new ComplianceViolation(
                "Sharpness",
                $"Image sharpness {sharpness.Overall} is below acceptable threshold",
                sharpness.Overall.Value < 0.3f ? ViolationSeverity.Critical : ViolationSeverity.Moderate));
        }

        foreach (var (region, score) in sharpness.RegionalScores)
        {
            var normalizedRegionScore = NormalizeRegionalSharpness(score);
            if (region == "Center" && normalizedRegionScore < CenterRegionCriticalThreshold)
            {
                violations.Add(new ComplianceViolation(
                    "Sharpness",
                    $"Face region has poor sharpness: {normalizedRegionScore:F2}",
                    ViolationSeverity.Critical));
            }
        }

        if (geometry.HeadSize.Value < 0.7f)
        {
            violations.Add(new ComplianceViolation(
                "Geometry",
                $"Head size ratio {geometry.HeadSize} is outside acceptable range",
                geometry.HeadSize.Value < 0.5f ? ViolationSeverity.Critical : ViolationSeverity.Moderate));
        }

        if (geometry.Centering.Value < 0.7f)
        {
            violations.Add(new ComplianceViolation(
                "Geometry",
                "Face is not properly centered in the image",
                ViolationSeverity.Moderate));
        }

        if (geometry.InterPupillaryDistance.Value < 0.8f)
        {
            violations.Add(new ComplianceViolation(
                "Geometry",
                "Inter-pupillary distance is outside acceptable range",
                ViolationSeverity.Minor));
        }

        var actualDims = geometry.ActualDimensions;
        var expectedDims = geometry.ExpectedDimensions;
        var widthRatio = (float)actualDims.Width / expectedDims.Width;
        var heightRatio = (float)actualDims.Height / expectedDims.Height;

        if (MathF.Abs(widthRatio - 1f) > 0.1f || MathF.Abs(heightRatio - 1f) > 0.1f)
        {
            violations.Add(new ComplianceViolation(
                "Dimensions",
                $"Image dimensions {actualDims.Width}x{actualDims.Height} differ from expected {expectedDims.Width}x{expectedDims.Height}",
                ViolationSeverity.Minor));
        }

        return violations;
    }

    private static Result<QualityScore> CalculateOverallScore(
        FacialSymmetryScore symmetry,
        SharpnessScore sharpness,
        GeometricCompliance geometry)
    {
        const float symmetryWeight = 0.3f;
        const float sharpnessWeight = 0.3f;
        const float geometryWeight = 0.4f;

        var weightedScore =
            symmetry.Overall.Value * symmetryWeight +
            sharpness.Overall.Value * sharpnessWeight +
            geometry.Overall.Value * geometryWeight;

        return QualityScore.Create(weightedScore);
    }

    private static float NormalizeRegionalSharpness(float rawRegionalScore)
    {
        const float sharpnessNormalizationFactor = 294f;
        return MathF.Min(rawRegionalScore * sharpnessNormalizationFactor, 1f);
    }
}

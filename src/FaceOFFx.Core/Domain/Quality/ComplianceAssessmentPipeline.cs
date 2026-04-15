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
/// Selects whether compliance is evaluated on the raw input image or the rendered output portrait.
/// </summary>
[PublicAPI]
public enum AssessmentMode
{
    /// <summary>
    /// Assess the source image before any portrait rendering.
    /// </summary>
    InputValidation,

    /// <summary>
    /// Assess the final aligned output portrait.
    /// </summary>
    OutputValidation
}

/// <summary>
/// Performs stage-appropriate compliance analysis and returns typed pipeline failures.
/// </summary>
[PublicAPI]
public static class ComplianceAssessmentPipeline
{
    /// <summary>
    /// Assesses compliance for a raw input image without requiring an aligned portrait.
    /// </summary>
    public static Task<Result<ComplianceAssessment, PipelineError>> AssessInputComplianceAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        string standardName = "PIV") =>
        AssessComplianceAsync(
            image,
            face,
            landmarks,
            standardName,
            AssessmentMode.InputValidation,
            Maybe<OutputPortraitAssessmentInput>.None);

    /// <summary>
    /// Assesses compliance for a rendered output portrait using output-space geometry and symmetry.
    /// </summary>
    public static Task<Result<ComplianceAssessment, PipelineError>> AssessOutputComplianceAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        OutputPortraitAssessmentInput outputPortrait,
        string standardName = "PIV") =>
        AssessComplianceAsync(
            image,
            face,
            landmarks,
            standardName,
            AssessmentMode.OutputValidation,
            Maybe<OutputPortraitAssessmentInput>.From(outputPortrait));

    /// <summary>
    /// Returns only the pass/fail outcome for raw-input compliance.
    /// </summary>
    public static Task<Result<bool, PipelineError>> QuickInputComplianceCheckAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        string standardName = "PIV") =>
        AssessInputComplianceAsync(image, face, landmarks, standardName)
            .MapAsync(assessment => assessment.IsCompliant);

    /// <summary>
    /// Returns only the pass/fail outcome for rendered-output compliance.
    /// </summary>
    public static Task<Result<bool, PipelineError>> QuickOutputComplianceCheckAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        OutputPortraitAssessmentInput outputPortrait,
        string standardName = "PIV") =>
        AssessOutputComplianceAsync(image, face, landmarks, outputPortrait, standardName)
            .MapAsync(assessment => assessment.IsCompliant);

    /// <summary>
    /// Produces the raw measurements used by the compliance evaluators.
    /// </summary>
    public static async Task<Result<RawMeasurements, PipelineError>> PerformRawMeasurementsAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks)
    {
        var stage1 = await PerformStage1AssessmentsAsync(image, face, landmarks).ConfigureAwait(false);
        if (stage1.IsFailure)
        {
            return Result.Failure<RawMeasurements, PipelineError>(stage1.Error);
        }

        var symmetry = await PerformStage2AssessmentsAsync(image, landmarks).ConfigureAwait(false);
        if (symmetry.IsFailure)
        {
            return Result.Failure<RawMeasurements, PipelineError>(symmetry.Error);
        }

        return Result.Success<RawMeasurements, PipelineError>(new RawMeasurements(
            symmetry.Value,
            stage1.Value.Sharpness,
            stage1.Value.Geometry));
    }

    private static async Task<Result<ComplianceAssessment, PipelineError>> AssessComplianceAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        string standardName,
        AssessmentMode mode,
        Maybe<OutputPortraitAssessmentInput> outputPortrait)
    {
        var rules = GetRulesForMode(standardName, mode);

        var stage1Result = await PerformStage1AssessmentsAsync(image, face, landmarks).ConfigureAwait(false);
        if (stage1Result.IsFailure)
        {
            return Result.Failure<ComplianceAssessment, PipelineError>(stage1Result.Error);
        }

        var stage2Result = mode == AssessmentMode.OutputValidation
            ? await AssessOutputSymmetryAsync(outputPortrait, standardName).ConfigureAwait(false)
            : await PerformStage2AssessmentsAsync(image, landmarks).ConfigureAwait(false);
        if (stage2Result.IsFailure)
        {
            return Result.Failure<ComplianceAssessment, PipelineError>(stage2Result.Error);
        }

        var geometryResult = mode == AssessmentMode.OutputValidation
            ? MeasureOutputGeometry(outputPortrait, standardName)
            : Result.Success<GeometryMeasurement, PipelineError>(stage1Result.Value.Geometry);
        if (geometryResult.IsFailure)
        {
            return Result.Failure<ComplianceAssessment, PipelineError>(geometryResult.Error);
        }

        return Result.Success<ComplianceAssessment, PipelineError>(ComplianceEvaluators.AssessCompliance(
            stage2Result.Value,
            stage1Result.Value.Sharpness,
            geometryResult.Value,
            rules,
            symmetryCountsTowardsCompliance: mode == AssessmentMode.OutputValidation,
            ipdCountsTowardsCompliance: mode == AssessmentMode.OutputValidation));
    }

    private static async Task<Result<SymmetryMeasurement, PipelineError>> AssessOutputSymmetryAsync(
        Maybe<OutputPortraitAssessmentInput> outputPortrait,
        string standardName)
    {
        var portrait = outputPortrait.ToPipelineResult(
            new ValidationError("Output validation requires aligned portrait data", standardName));
        if (portrait.IsFailure)
        {
            return Result.Failure<SymmetryMeasurement, PipelineError>(portrait.Error);
        }

        return await PerformStage2AssessmentsAsync(
                portrait.Value.Image,
                portrait.Value.Landmarks)
            .ConfigureAwait(false);
    }

    private static Result<GeometryMeasurement, PipelineError> MeasureOutputGeometry(
        Maybe<OutputPortraitAssessmentInput> outputPortrait,
        string standardName)
    {
        var portrait = outputPortrait.ToPipelineResult(
            new ValidationError("Output validation requires aligned portrait data", standardName));
        if (portrait.IsFailure)
        {
            return Result.Failure<GeometryMeasurement, PipelineError>(portrait.Error);
        }

        return GeometryAssessor.MeasureGeometry(
                new ImageDimensions(portrait.Value.Image.Width, portrait.Value.Image.Height),
                portrait.Value.Landmarks)
            .ToPipelineResult(error => new ValidationError($"Output geometry assessment failed: {error}", standardName));
    }

    private static async Task<Result<Stage1Measurements, PipelineError>> PerformStage1AssessmentsAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks)
    {
        var faceRectangle = new Rectangle(
            (int)face.BoundingBox.X,
            (int)face.BoundingBox.Y,
            (int)face.BoundingBox.Width,
            (int)face.BoundingBox.Height);

        var sharpnessTask = Task.Run(() => SharpnessAssessor.MeasureSharpness(image, faceRectangle));
        var geometryTask = Task.Run(() => GeometryAssessor.MeasureGeometry(
            new ImageDimensions(image.Width, image.Height), landmarks));

        await Task.WhenAll(sharpnessTask, geometryTask).ConfigureAwait(false);

        var sharpnessResult = (await sharpnessTask.ConfigureAwait(false))
            .ToPipelineResult(error => new ValidationError($"Stage 1 sharpness assessment failed: {error}"));
        if (sharpnessResult.IsFailure)
        {
            return Result.Failure<Stage1Measurements, PipelineError>(sharpnessResult.Error);
        }

        var geometryResult = (await geometryTask.ConfigureAwait(false))
            .ToPipelineResult(error => new ValidationError($"Stage 1 geometry assessment failed: {error}"));
        if (geometryResult.IsFailure)
        {
            return Result.Failure<Stage1Measurements, PipelineError>(geometryResult.Error);
        }

        return Result.Success<Stage1Measurements, PipelineError>(new Stage1Measurements(
            sharpnessResult.Value,
            geometryResult.Value));
    }

    private static async Task<Result<SymmetryMeasurement, PipelineError>> PerformStage2AssessmentsAsync(
        Image<Rgba32> image,
        FaceLandmarks68 landmarks)
    {
        return (await Task.Run(() => SymmetryAssessor.MeasureAsymmetry(image, landmarks)).ConfigureAwait(false))
            .ToPipelineResult(error => new ValidationError($"Stage 2 symmetry assessment failed: {error}"));
    }

    private static ComplianceRules GetRulesForMode(string standardName, AssessmentMode mode)
    {
        var isInputValidation = mode == AssessmentMode.InputValidation;
        var rules = ComplianceStandards.GetRulesForMode(standardName, isInputValidation);
        if (rules != null)
        {
            return rules;
        }

        var baseRules = ComplianceStandards.GetRulesOrDefault(standardName);
        if (mode == AssessmentMode.InputValidation)
        {
            return baseRules with
            {
                MinHeadSizePercent = 25f,
                MaxHeadSizePercent = 85f,
                MinSymmetryPercent = 40f,
                MinSharpnessPercent = 15f,
                MinIpdPixels = 60f,
                MaxIpdPixels = 200f
            };
        }

        return baseRules;
    }
}

/// <summary>
/// Carries an already-rendered portrait plus its output-space landmarks for output validation.
/// </summary>
/// <param name="Image">The rendered portrait image in final output dimensions.</param>
/// <param name="Landmarks">The facial landmarks measured in output image coordinates.</param>
[PublicAPI]
public sealed record OutputPortraitAssessmentInput(
    Image<Rgba32> Image,
    FaceLandmarks68 Landmarks);

/// <summary>
/// Holds the stage-1 measurements produced from the raw image.
/// </summary>
/// <param name="Sharpness">The raw-image sharpness measurement.</param>
/// <param name="Geometry">The raw-image geometry measurement.</param>
[PublicAPI]
public record Stage1Measurements(
    SharpnessMeasurement Sharpness,
    GeometryMeasurement Geometry);

/// <summary>
/// Holds the complete set of raw measurements gathered before compliance evaluation.
/// </summary>
/// <param name="Symmetry">The symmetry measurement.</param>
/// <param name="Sharpness">The sharpness measurement.</param>
/// <param name="Geometry">The geometry measurement.</param>
[PublicAPI]
public record RawMeasurements(
    SymmetryMeasurement Symmetry,
    SharpnessMeasurement Sharpness,
    GeometryMeasurement Geometry);

/// <summary>
/// Controls how compliance findings are interpreted by callers.
/// </summary>
[PublicAPI]
public enum ComplianceRequirement
{
    /// <summary>
    /// Treat every violation as a hard failure.
    /// </summary>
    Strict,
    /// <summary>
    /// Collect violations while still returning the full assessment.
    /// </summary>
    Lenient,
    /// <summary>
    /// Report findings for informational use only.
    /// </summary>
    Advisory
}

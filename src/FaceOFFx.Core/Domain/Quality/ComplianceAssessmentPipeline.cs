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
/// Assessment mode determines which image processing stages are used
/// </summary>
[PublicAPI]
public enum AssessmentMode
{
    /// <summary>
    /// Input validation - assess raw photos for processing suitability
    /// </summary>
    InputValidation,
    
    /// <summary>
    /// Output validation - assess final credentials for compliance
    /// </summary>
    OutputValidation
}

/// <summary>
/// Pipeline for performing compliance assessment with stage-appropriate testing
/// </summary>
[PublicAPI]
public static class ComplianceAssessmentPipeline
{
    /// <summary>
    /// Performs multi-stage compliance assessment optimized for accuracy
    /// </summary>
    public static async Task<Result<ComplianceAssessment>> AssessComplianceAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        string standardName = "PIV",
        AssessmentMode mode = AssessmentMode.InputValidation,
        OutputPortraitAssessmentInput? outputPortrait = null)
    {
        // Get compliance rules for standard and mode
        var rules = GetRulesForMode(standardName, mode);
        
        // Step 1: Stage 1 - Raw image assessments
        var stage1Task = Task.Run(() => PerformStage1AssessmentsAsync(image, face, landmarks));
        var stage1Result = await stage1Task;
        
        if (stage1Result.IsFailure)
        {
            return Result.Failure<ComplianceAssessment>($"Stage 1 assessments failed: {stage1Result.Error}");
        }
        
        // Step 2: Stage 2 - Pre-aligned assessments (if needed for symmetry)
        SymmetryMeasurement? symmetryMeasurement = null;
        if (mode == AssessmentMode.OutputValidation)
        {
            if (outputPortrait == null)
            {
                return Result.Failure<ComplianceAssessment>(
                    "Output validation requires aligned portrait data");
            }

            var stage2Task = Task.Run(() => PerformStage2AssessmentsAsync(
                outputPortrait.Image,
                outputPortrait.Landmarks));
            var stage2Result = await stage2Task;

            if (stage2Result.IsFailure)
            {
                return Result.Failure<ComplianceAssessment>($"Stage 2 assessments failed: {stage2Result.Error}");
            }

            symmetryMeasurement = stage2Result.Value;
        }
        else
        {
            var stage2Task = Task.Run(() => PerformStage2AssessmentsAsync(image, landmarks));
            var stage2Result = await stage2Task;
            if (stage2Result.IsFailure)
            {
                return Result.Failure<ComplianceAssessment>($"Stage 2 assessments failed: {stage2Result.Error}");
            }

            symmetryMeasurement = stage2Result.Value;
        }
        
        // Step 3: Stage 3 - output-mode geometry uses the final transformed portrait geometry.
        GeometryMeasurement geometryMeasurement;
        if (mode == AssessmentMode.OutputValidation)
        {
            geometryMeasurement = MeasureOutputGeometry(outputPortrait!);
        }
        else
        {
            geometryMeasurement = stage1Result.Value.Geometry;
        }
        
        // Step 4: Evaluate compliance against rules
        var assessment = ComplianceEvaluators.AssessCompliance(
            symmetryMeasurement,
            stage1Result.Value.Sharpness,
            geometryMeasurement,
            rules,
            symmetryCountsTowardsCompliance: mode == AssessmentMode.OutputValidation,
            ipdCountsTowardsCompliance: mode == AssessmentMode.OutputValidation
        );
        
        return Result.Success(assessment);
    }

    private static GeometryMeasurement MeasureOutputGeometry(OutputPortraitAssessmentInput outputPortrait)
    {
        return GeometryAssessor.MeasureGeometry(
            new ImageDimensions(outputPortrait.Image.Width, outputPortrait.Image.Height),
            outputPortrait.Landmarks).Value;
    }

    /// <summary>
    /// Stage 1: Raw image assessments (sharpness, head size, centering)
    /// </summary>
    private static async Task<Result<Stage1Measurements>> PerformStage1AssessmentsAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks)
    {
        // Run Stage 1 measurements in parallel for better performance
        // For sharpness, analyze only the face region
        var faceRectangle = new Rectangle(
            (int)face.BoundingBox.X,
            (int)face.BoundingBox.Y,
            (int)face.BoundingBox.Width,
            (int)face.BoundingBox.Height
        );
        var sharpnessTask = Task.Run(() => SharpnessAssessor.MeasureSharpness(image, faceRectangle));
        var geometryTask = Task.Run(() => GeometryAssessor.MeasureGeometry(
            new ImageDimensions(image.Width, image.Height), landmarks));
        
        await Task.WhenAll(sharpnessTask, geometryTask);
        
        var sharpnessResult = await sharpnessTask;
        var geometryResult = await geometryTask;
        
        // Combine results
        return Result.Combine(sharpnessResult, geometryResult)
            .Map(() => new Stage1Measurements(
                sharpnessResult.Value,
                geometryResult.Value
            ));
    }

    /// <summary>
    /// Stage 2: Pre-aligned assessments (symmetry with horizontal eyes)
    /// </summary>
    private static async Task<Result<SymmetryMeasurement>> PerformStage2AssessmentsAsync(
        Image<Rgba32> image,
        FaceLandmarks68 landmarks)
    {
        return await Task.Run(() => SymmetryAssessor.MeasureAsymmetry(image, landmarks));
    }

    /// <summary>
    /// Gets compliance rules appropriate for the assessment mode
    /// </summary>
    private static ComplianceRules GetRulesForMode(string standardName, AssessmentMode mode)
    {
        var isInputValidation = mode == AssessmentMode.InputValidation;
        
        // Try to get dual rules first (preferred)
        var rules = ComplianceStandards.GetRulesForMode(standardName, isInputValidation);
        if (rules != null)
        {
            return rules;
        }
        
        // Fallback to legacy single-threshold rules
        var baseRules = ComplianceStandards.GetRulesOrDefault(standardName);
        
        if (mode == AssessmentMode.InputValidation)
        {
            // More lenient thresholds for input validation (legacy fallback)
            return baseRules with
            {
                MinHeadSizePercent = 25f, // More lenient than 35%
                MaxHeadSizePercent = 85f, // More lenient than 80%
                MinSymmetryPercent = 40f, // More lenient than 60%
                MinSharpnessPercent = 15f, // More lenient than 25%
                MinIpdPixels = 60f,       // More lenient range for input
                MaxIpdPixels = 200f
            };
        }
        
        return baseRules; // Use strict rules for output validation
    }

    /// <summary>
    /// Fast compliance check - returns pass/fail without detailed analysis
    /// </summary>
    public static async Task<Result<bool>> QuickComplianceCheckAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        string standardName = "PIV",
        AssessmentMode mode = AssessmentMode.InputValidation,
        OutputPortraitAssessmentInput? outputPortrait = null)
    {
        var assessment = await AssessComplianceAsync(image, face, landmarks, standardName, mode, outputPortrait);
        return assessment.Map(a => a.IsCompliant);
    }

    /// <summary>
    /// Legacy method for backwards compatibility
    /// </summary>
    public static async Task<Result<RawMeasurements>> PerformRawMeasurementsAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks)
    {
        var stage1 = await PerformStage1AssessmentsAsync(image, face, landmarks);
        var symmetry = await PerformStage2AssessmentsAsync(image, landmarks);
        
        return Result.Combine(stage1, symmetry)
            .Map(() => new RawMeasurements(
                symmetry.Value,
                stage1.Value.Sharpness,
                stage1.Value.Geometry
            ));
    }
}

/// <summary>
/// Aligned portrait data used for output-mode compliance assessment.
/// </summary>
[PublicAPI]
public sealed record OutputPortraitAssessmentInput(
    Image<Rgba32> Image,
    FaceLandmarks68 Landmarks);

/// <summary>
/// Container for Stage 1 assessment results (raw image assessments)
/// </summary>
[PublicAPI]
public record Stage1Measurements(
    SharpnessMeasurement Sharpness,
    GeometryMeasurement Geometry);

/// <summary>
/// Container for raw measurement results before compliance evaluation
/// </summary>
[PublicAPI]
public record RawMeasurements(
    SymmetryMeasurement Symmetry,
    SharpnessMeasurement Sharpness,
    GeometryMeasurement Geometry);

/// <summary>
/// Compliance requirement strictness levels
/// </summary>
[PublicAPI]
public enum ComplianceRequirement
{
    /// <summary>
    /// Strict compliance - fail fast on any violation
    /// </summary>
    Strict,
    
    /// <summary>
    /// Lenient compliance - collect all violations but continue processing
    /// </summary>
    Lenient,
    
    /// <summary>
    /// Advisory only - show violations but don't block processing
    /// </summary>
    Advisory
}

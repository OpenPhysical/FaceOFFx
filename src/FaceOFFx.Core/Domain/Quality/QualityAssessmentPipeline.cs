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
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Core.Domain.Quality.Assessors;
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
    /// <summary>
    /// Performs a complete quality assessment on a facial image
    /// </summary>
    public static async Task<Result<Iso19794Assessment>> AssessAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        QualityAssessmentOptions options)
    {
        // Validate options
        var optionsValidation = options.Validate();
        if (optionsValidation.IsFailure)
        {
            return Result.Failure<Iso19794Assessment>($"Invalid assessment options: {optionsValidation.Error}");
        }
        
        if (options.EnableParallelAssessment)
        {
            return await AssessParallelAsync(image, face, landmarks, options);
        }
        
        return await AssessSequentialAsync(image, face, landmarks, options);
    }
    
    private static async Task<Result<Iso19794Assessment>> AssessParallelAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        QualityAssessmentOptions options)
    {
        // Calculate face ROI for focused sharpness assessment
        Rectangle? faceRoi = null;
        try
        {
            faceRoi = landmarks.CalculateFaceRoi();
            
            // Ensure ROI doesn't exceed image boundaries
            faceRoi = Rectangle.Intersect(faceRoi.Value, new Rectangle(0, 0, image.Width, image.Height));
            
            // If intersection results in empty rectangle, use null (full image)
            if (faceRoi.Value.Width <= 0 || faceRoi.Value.Height <= 0)
            {
                faceRoi = null;
            }
        }
        catch (Exception)
        {
            // If face ROI calculation fails, fall back to full image analysis
            faceRoi = null;
        }
        
        // Run assessments in parallel for better performance
        var symmetryTask = Task.Run(() => SymmetryAssessor.Assess(image, landmarks));
        var sharpnessTask = Task.Run(() => SharpnessAssessor.Assess(image, faceRoi));
        var geometryTask = Task.Run(() => 
            GeometryAssessor.Assess(
                new ImageDimensions(image.Width, image.Height),
                landmarks,
                options.Standard));
        
        await Task.WhenAll(symmetryTask, sharpnessTask, geometryTask);
        
        return CombineResults(
            await symmetryTask,
            await sharpnessTask,
            await geometryTask,
            options);
    }
    
    private static async Task<Result<Iso19794Assessment>> AssessSequentialAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        QualityAssessmentOptions options)
    {
        // Calculate face ROI for focused sharpness assessment
        Rectangle? faceRoi = null;
        try
        {
            faceRoi = landmarks.CalculateFaceRoi();
            
            // Ensure ROI doesn't exceed image boundaries
            faceRoi = Rectangle.Intersect(faceRoi.Value, new Rectangle(0, 0, image.Width, image.Height));
            
            // If intersection results in empty rectangle, use null (full image)
            if (faceRoi.Value.Width <= 0 || faceRoi.Value.Height <= 0)
            {
                faceRoi = null;
            }
        }
        catch (Exception)
        {
            // If face ROI calculation fails, fall back to full image analysis
            faceRoi = null;
        }
        
        var symmetryResult = SymmetryAssessor.Assess(image, landmarks);
        if (symmetryResult.IsFailure)
            return Result.Failure<Iso19794Assessment>($"Symmetry assessment failed: {symmetryResult.Error}");
        
        var sharpnessResult = SharpnessAssessor.Assess(image, faceRoi);
        if (sharpnessResult.IsFailure)
            return Result.Failure<Iso19794Assessment>($"Sharpness assessment failed: {sharpnessResult.Error}");
        
        var geometryResult = GeometryAssessor.Assess(
            new ImageDimensions(image.Width, image.Height),
            landmarks,
            options.Standard);
        if (geometryResult.IsFailure)
            return Result.Failure<Iso19794Assessment>($"Geometry assessment failed: {geometryResult.Error}");
        
        return await Task.FromResult(CombineResults(
            symmetryResult,
            sharpnessResult,
            geometryResult,
            options));
    }
    
    private static Result<Iso19794Assessment> CombineResults(
        Result<FacialSymmetryScore> symmetryResult,
        Result<SharpnessScore> sharpnessResult,
        Result<GeometricCompliance> geometryResult,
        QualityAssessmentOptions options)
    {
        // Check if all results are successful
        var combinedResult = Result.Combine(symmetryResult, sharpnessResult, geometryResult);
        if (combinedResult.IsFailure)
        {
            return Result.Failure<Iso19794Assessment>(combinedResult.Error);
        }
        
        var symmetry = symmetryResult.Value;
        var sharpness = sharpnessResult.Value;
        var geometry = geometryResult.Value;
        
        // Collect violations
        var violations = CollectViolations(symmetry, sharpness, geometry, options);
        
        // Calculate overall score with weighted components
        var overallScore = CalculateOverallScore(symmetry, sharpness, geometry);
        
        return overallScore.Map(score => new Iso19794Assessment(
            score,
            symmetry,
            sharpness,
            geometry,
            violations,
            DateTime.UtcNow));
    }
    
    private static List<ComplianceViolation> CollectViolations(
        FacialSymmetryScore symmetry,
        SharpnessScore sharpness,
        GeometricCompliance geometry,
        QualityAssessmentOptions options)
    {
        var violations = new List<ComplianceViolation>();
        
        // Symmetry violations
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
        
        // Sharpness violations
        if (sharpness.Overall.Value < 0.5f)
        {
            violations.Add(new ComplianceViolation(
                "Sharpness",
                $"Image sharpness {sharpness.Overall} is below acceptable threshold",
                sharpness.Overall.Value < 0.3f ? ViolationSeverity.Critical : ViolationSeverity.Moderate));
        }
        
        // Check regional sharpness
        foreach (var (region, score) in sharpness.RegionalScores)
        {
            if (score < 0.4f && region == "Center")
            {
                violations.Add(new ComplianceViolation(
                    "Sharpness",
                    $"Face region has poor sharpness: {score:F2}",
                    ViolationSeverity.Critical));
            }
        }
        
        // Geometry violations
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
        
        // Dimension violations
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
        // Weight different components
        const float symmetryWeight = 0.3f;
        const float sharpnessWeight = 0.3f;
        const float geometryWeight = 0.4f;
        
        var weightedScore = 
            symmetry.Overall.Value * symmetryWeight +
            sharpness.Overall.Value * sharpnessWeight +
            geometry.Overall.Value * geometryWeight;
        
        return QualityScore.Create(weightedScore);
    }
}
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
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality.Assessors;

/// <summary>
/// Assesses geometric compliance with ISO/IEC 19794-5 standards
/// </summary>
[PublicAPI]
public static class GeometryAssessor
{
    // DPI to millimeter conversion constant
    private const float PixelsPerMm = 300f / 25.4f; // 300 DPI standard for PIV cards
    
    // Deviation thresholds for centering calculations
    private const float CenteringDeviationThreshold = 0.1f;
    private const float CenteringDeviationPenaltyFactor = 2f;
    private const float MaxCenteringPenalty = 0.8f;
    
    // Head size edge-of-range penalty
    private const float HeadSizeEdgeRangePenalty = 0.2f;
    // Standard landmark indices for eyes
    private const int LeftEyeOuterCorner = 36;
    private const int LeftEyeInnerCorner = 39;
    private const int RightEyeInnerCorner = 42;
    private const int RightEyeOuterCorner = 45;
    
    // Face outline landmark indices
    private const int ChinPoint = 8;
    private const int LeftTemple = 0;
    private const int RightTemple = 16;
    
    /// <summary>
    /// Assesses geometric compliance with the specified standard
    /// </summary>
    public static Result<GeometricCompliance> Assess(
        ImageDimensions imageDims,
        FaceLandmarks68 landmarks,
        Iso19794Standard standard)
    {
        return CalculateHeadSize(landmarks, imageDims, standard)
            .Bind(headSize => CalculateCentering(landmarks, imageDims)
                .Bind(centering => CalculateInterPupillaryDistance(landmarks, standard)
                    .Map(ipd => new GeometricCompliance(
                        headSize,
                        centering,
                        ipd,
                        imageDims,
                        standard.ExpectedDimensions))));
    }

    /// <summary>
    /// Measures geometric properties and returns raw values for compliance evaluation
    /// </summary>
    public static Result<GeometryMeasurement> MeasureGeometry(
        ImageDimensions imageDims,
        FaceLandmarks68 landmarks)
    {
        return CalculateRawHeadSizePercent(landmarks, imageDims)
            .Bind(headSizePercent => CalculateRawCenteringPercent(landmarks, imageDims)
                .Bind(centeringPercent => CalculateRawIpdPixels(landmarks)
                    .Map(ipdPixels => new GeometryMeasurement(
                        HeadSizePercent: headSizePercent,
                        CenteringPercent: centeringPercent,
                        IpdPixels: ipdPixels,
                        ImageWidth: imageDims.Width,
                        ImageHeight: imageDims.Height
                    ))));
    }
    
    private static Result<QualityScore> CalculateHeadSize(
        FaceLandmarks68 landmarks,
        ImageDimensions imageDims,
        Iso19794Standard standard)
    {
        // Validate landmarks
        var landmarkValidation = ValidateLandmarks68(landmarks);
        if (landmarkValidation.IsFailure)
        {
            return Result.Failure<QualityScore>($"Head size calculation failed: {landmarkValidation.Error}");
        }
        
        // Calculate head width from temple to temple
        if (LeftTemple >= landmarks.Points.Count || RightTemple >= landmarks.Points.Count)
        {
            return Result.Failure<QualityScore>($"Unable to calculate head size: Required landmark points (temples at indices {LeftTemple} and {RightTemple}) are missing or invalid");
        }
            
            var leftTemplePoint = landmarks.Points[LeftTemple];
            var rightTemplePoint = landmarks.Points[RightTemple];
            
            var headWidth = MathF.Abs(rightTemplePoint.X - leftTemplePoint.X);
            
            // Prevent division by zero
            if (imageDims.Width <= 0)
            {
                return Result.Failure<QualityScore>("Invalid image width for head size calculation");
            }
            
            var headWidthRatio = headWidth / imageDims.Width;
            
            // Calculate score based on how close the ratio is to the ideal range
            var idealMidpoint = (standard.MinHeadWidthRatio + standard.MaxHeadWidthRatio) / 2f;
            var deviation = MathF.Abs(headWidthRatio - idealMidpoint);
            var maxDeviation = (standard.MaxHeadWidthRatio - standard.MinHeadWidthRatio) / 2f;
            
            float score;
            if (headWidthRatio < standard.MinHeadWidthRatio)
            {
                // Too small
                score = headWidthRatio / standard.MinHeadWidthRatio;
            }
            else if (headWidthRatio > standard.MaxHeadWidthRatio)
            {
                // Too large
                score = standard.MaxHeadWidthRatio / headWidthRatio;
            }
            else
            {
                // Within range - score based on closeness to ideal
                score = 1f - (deviation / maxDeviation) * HeadSizeEdgeRangePenalty;
            }
            
            return QualityScore.Create(MathF.Max(0f, MathF.Min(1f, score)));
    }
    
    private static Result<QualityScore> CalculateCentering(
        FaceLandmarks68 landmarks,
        ImageDimensions imageDims)
    {
        // Validate landmarks
        var landmarkValidation = ValidateLandmarks68(landmarks);
        if (landmarkValidation.IsFailure)
        {
            return Result.Failure<QualityScore>($"Centering calculation failed: {landmarkValidation.Error}");
        }
        
        // Calculate face center from key landmarks
        if (30 >= landmarks.Points.Count || ChinPoint >= landmarks.Points.Count)
        {
            return Result.Failure<QualityScore>($"Unable to calculate face centering: Required landmark points (nose tip at index 30, chin at index {ChinPoint}) are missing or invalid");
        }
            
            var nose = landmarks.Points[30]; // Nose tip
            var chin = landmarks.Points[ChinPoint];
            var leftEye = landmarks.LeftEyeCenter;
            var rightEye = landmarks.RightEyeCenter;
            
            // Calculate geometric center of face
            var faceCenterX = (leftEye.X + rightEye.X + nose.X) / 3f;
            var faceCenterY = (leftEye.Y + rightEye.Y + chin.Y) / 3f;
            
            // Calculate deviation from image center
            var imageCenterX = imageDims.Width / 2f;
            var imageCenterY = imageDims.Height / 2f;
            
            // Prevent division by zero
            if (imageDims.Width <= 0 || imageDims.Height <= 0)
            {
                return Result.Failure<QualityScore>("Invalid image dimensions for centering calculation");
            }
            
            var deviationX = MathF.Abs(faceCenterX - imageCenterX) / imageDims.Width;
            var deviationY = MathF.Abs(faceCenterY - imageCenterY) / imageDims.Height;
            
            // Score based on deviation (lower deviation = higher score)
            // Allow up to threshold deviation without penalty
            var scoreX = deviationX <= CenteringDeviationThreshold ? 1f : 
                1f - MathF.Min((deviationX - CenteringDeviationThreshold) * CenteringDeviationPenaltyFactor, MaxCenteringPenalty);
            var scoreY = deviationY <= CenteringDeviationThreshold ? 1f : 
                1f - MathF.Min((deviationY - CenteringDeviationThreshold) * CenteringDeviationPenaltyFactor, MaxCenteringPenalty);
            
            var overallScore = (scoreX + scoreY) / 2f;
            
            return QualityScore.Create(MathF.Max(0f, MathF.Min(1f, overallScore)));
    }
    
    private static Result<QualityScore> CalculateInterPupillaryDistance(
        FaceLandmarks68 landmarks,
        Iso19794Standard standard)
    {
        // Validate landmarks
        var landmarkValidation = ValidateLandmarks68(landmarks);
        if (landmarkValidation.IsFailure)
        {
            return Result.Failure<QualityScore>($"IPD calculation failed: {landmarkValidation.Error}");
        }
        
        var leftEye = GetEyeCenter(landmarks, isLeft: true);
        var rightEye = GetEyeCenter(landmarks, isLeft: false);
            
            // Calculate IPD in pixels
            var deltaX = rightEye.X - leftEye.X;
            var deltaY = rightEye.Y - leftEye.Y;
            
            // Prevent overflow in Pow operations
            if (MathF.Abs(deltaX) > 10000f || MathF.Abs(deltaY) > 10000f)
            {
                return Result.Failure<QualityScore>("Eye positions too far apart for valid IPD calculation");
            }
            
            var ipdPixels = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);

            // Score based on expected IPD range in pixels.
            float score;
            if (ipdPixels < standard.MinInterPupillaryDistance)
            {
                score = ipdPixels / standard.MinInterPupillaryDistance;
            }
            else if (ipdPixels > standard.MaxInterPupillaryDistance)
            {
                score = standard.MaxInterPupillaryDistance / ipdPixels;
            }
            else
            {
                // Within range - full score
                score = 1f;
            }
            
            return QualityScore.Create(MathF.Max(0f, MathF.Min(1f, score)));
    }
    
    private static Point2D GetEyeCenter(FaceLandmarks68 landmarks, bool isLeft)
    {
        if (isLeft)
        {
            return landmarks.LeftEyeCenter;
        }
        else
        {
            return landmarks.RightEyeCenter;
        }
    }
    
    /// <summary>
    /// Validates that landmarks contain at least 68 points
    /// </summary>
    private static Result ValidateLandmarks68(FaceLandmarks68 landmarks)
    {
        if (landmarks?.Points == null)
        {
            return Result.Failure("No facial landmarks detected. Please ensure the image contains a clear, frontal view of a face");
        }
        
        if (landmarks.Points.Count == 0)
        {
            return Result.Failure("No facial feature points were detected. The face may be obscured, too small, or at an extreme angle");
        }
        
        if (landmarks.Points.Count < 68)
        {
            return Result.Failure($"Incomplete facial landmark detection: Only {landmarks.Points.Count} points found, but 68 points are required for ISO/IEC 19794-5 compliance. The face may be partially obscured or the image quality may be insufficient");
        }
        
        return Result.Success();
    }
    
    /// <summary>
    /// Calculates raw head size percentage for compliance evaluation
    /// </summary>
    private static Result<float> CalculateRawHeadSizePercent(FaceLandmarks68 landmarks, ImageDimensions imageDims)
    {
        return ValidateLandmarks68(landmarks)
            .Bind(() =>
            {
                // Get the actual face boundary points
                var browLine = landmarks.Points.Take(17).Min(p => p.Y); // Jawline points for brow approximation
                var chinPoint = landmarks.Points[ChinPoint].Y;
                
                // Estimate actual head top by extrapolating above the eyebrow line
                // Typical ratio: forehead + hair is about 30% of face height above brow line
                var faceHeight = chinPoint - browLine;
                var estimatedHeadTop = browLine - (faceHeight * 0.3f);
                var headHeight = chinPoint - estimatedHeadTop;
                var headSizePercent = (headHeight / imageDims.Height) * 100f;
                
                return Result.Success(headSizePercent);
            });
    }
    
    /// <summary>
    /// Calculates raw centering percentage for compliance evaluation
    /// </summary>
    private static Result<float> CalculateRawCenteringPercent(FaceLandmarks68 landmarks, ImageDimensions imageDims)
    {
        return ValidateLandmarks68(landmarks)
            .Bind(() =>
            {
                var faceCenter = new Point2D(
                    landmarks.Points.Average(p => p.X),
                    landmarks.Points.Average(p => p.Y)
                );
                
                var imageCenter = new Point2D(imageDims.Width / 2f, imageDims.Height / 2f);
                var deviation = MathF.Sqrt(
                    MathF.Pow(faceCenter.X - imageCenter.X, 2) +
                    MathF.Pow(faceCenter.Y - imageCenter.Y, 2)
                );
                
                var maxPossibleDeviation = MathF.Sqrt(
                    MathF.Pow(imageDims.Width / 2f, 2) +
                    MathF.Pow(imageDims.Height / 2f, 2)
                );
                
                var centeringPercent = (1f - deviation / maxPossibleDeviation) * 100f;
                return Result.Success(MathF.Max(0f, centeringPercent));
            });
    }
    
    /// <summary>
    /// Calculates raw inter-pupillary distance in pixels for compliance evaluation
    /// </summary>
    private static Result<float> CalculateRawIpdPixels(FaceLandmarks68 landmarks)
    {
        return ValidateLandmarks68(landmarks)
            .Bind(() =>
            {
                var leftEye = GetEyeCenter(landmarks, true);
                var rightEye = GetEyeCenter(landmarks, false);
                
                var ipdPixels = MathF.Sqrt(
                    MathF.Pow(rightEye.X - leftEye.X, 2) +
                    MathF.Pow(rightEye.Y - leftEye.Y, 2)
                );
                
                return Result.Success(ipdPixels);
            });
    }
}

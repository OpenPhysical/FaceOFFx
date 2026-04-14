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
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Core.Domain.Quality.Assessors;

/// <summary>
/// Assesses facial symmetry using Gabor wavelets as per ISO/IEC 19794-5.
/// 
/// Gabor filters detect texture differences between left and right face halves across multiple orientations.
/// Higher asymmetry values indicate greater differences between facial halves, suggesting:
/// - Illumination asymmetry: Uneven lighting across the face (horizontal orientations)
/// - Pose asymmetry: Non-frontal face positioning (diagonal orientations)
/// 
/// Quality scores are computed using sigmoid transformation for realistic perceptual mapping.
/// </summary>
[PublicAPI]
public static class SymmetryAssessor
{
    private const int GaborKernelSize = 21;
    private static readonly float[] Orientations = { 0f, 22.5f, 45f, 67.5f, 90f };
    private static readonly GaborFilter[] Filters = CreateGaborFilters();
    
    // Grayscale conversion coefficients (ITU-R BT.709 standard)
    private const float GrayscaleRedCoefficient = 0.299f;
    private const float GrayscaleGreenCoefficient = 0.587f;
    private const float GrayscaleBlueCoefficient = 0.114f;
    
    // Face region extraction parameters
    private const float FaceRegionPaddingRatio = 0.2f;
    
    // Statistical normalization threshold for response differences
    private const float MinResponseThreshold = 0.001f;
    
    // Gabor filter parameters
    private const float GaborSigma = 5.0f;
    private const float GaborLambda = 10.0f;
    private const float GaborGamma = 0.5f;
    private const float GaborPsi = 0f;
    
    /// <summary>
    /// Assesses facial symmetry using Gabor wavelets
    /// </summary>
    public static Result<FacialSymmetryScore> Assess(
        Image<Rgba32> image,
        FaceLandmarks68 landmarks)
    {
        var faceRegionResult = ExtractFaceRegion(image, landmarks);
        if (faceRegionResult.IsFailure)
        {
            return Result.Failure<FacialSymmetryScore>(faceRegionResult.Error);
        }

        using var faceRegion = faceRegionResult.Value;
        var halvesResult = ExtractFaceHalves(faceRegion, landmarks);
        if (halvesResult.IsFailure)
        {
            return Result.Failure<FacialSymmetryScore>(halvesResult.Error);
        }

        using var leftHalf = halvesResult.Value.left;
        using var rightHalf = halvesResult.Value.right;
        return ApplyGaborFilters(leftHalf, rightHalf)
            .Map(CalculateAsymmetry)
            .Bind(CreateSymmetryScore);
    }

    /// <summary>
    /// Measures facial symmetry and returns raw asymmetry percentages for compliance evaluation
    /// </summary>
    public static Result<SymmetryMeasurement> MeasureAsymmetry(
        Image<Rgba32> image,
        FaceLandmarks68 landmarks)
    {
        var faceRegionResult = ExtractFaceRegion(image, landmarks);
        if (faceRegionResult.IsFailure)
        {
            return Result.Failure<SymmetryMeasurement>(faceRegionResult.Error);
        }

        using var faceRegion = faceRegionResult.Value;
        var halvesResult = ExtractFaceHalves(faceRegion, landmarks);
        if (halvesResult.IsFailure)
        {
            return Result.Failure<SymmetryMeasurement>(halvesResult.Error);
        }

        using var leftHalf = halvesResult.Value.left;
        using var rightHalf = halvesResult.Value.right;
        return ApplyGaborFilters(leftHalf, rightHalf)
            .Map(CalculateAsymmetry)
            .Map(asymmetry => new SymmetryMeasurement(
                IlluminationAsymmetryPercent: ConvertAsymmetryToPercentage(asymmetry.IlluminationAsymmetry),
                PoseAsymmetryPercent: ConvertAsymmetryToPercentage(asymmetry.PoseAsymmetry),
                GaborResponses: asymmetry.GaborResponses
            ));
    }
    
    private static Result<Image<Rgba32>> ExtractFaceRegion(
        Image<Rgba32> image,
        FaceLandmarks68 landmarks)
    {
        // Validate landmarks
        if (landmarks.Points == null || landmarks.Points.Count == 0)
        {
            return Result.Failure<Image<Rgba32>>("Unable to extract face region: No facial landmark points were detected in the image");
        }
        
        // Calculate bounding box from landmarks with padding
        var points = landmarks.Points;
        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);
        
        var width = maxX - minX;
        var height = maxY - minY;
        
        // Validate dimensions
        if (width <= 0 || height <= 0)
        {
            return Result.Failure<Image<Rgba32>>($"Face dimensions too small for processing: width={width:F1}px, height={height:F1}px. The face may be too small or partially outside the image");
        }
        
        var padding = MathF.Max(width, height) * FaceRegionPaddingRatio;
        
        var x = MathF.Max(0, minX - padding);
        var y = MathF.Max(0, minY - padding);
        var w = MathF.Min(image.Width - x, width + 2 * padding);
        var h = MathF.Min(image.Height - y, height + 2 * padding);
        
        // Validate crop rectangle
        if (w <= 0 || h <= 0 || x < 0 || y < 0 || x + w > image.Width || y + h > image.Height)
        {
            return Result.Failure<Image<Rgba32>>($"Unable to extract face region: The calculated region (x={x:F0}, y={y:F0}, w={w:F0}, h={h:F0}) extends beyond the image boundaries ({image.Width}x{image.Height}). The face may be too close to the image edge");
        }
        
        var faceRegion = image.Clone(ctx => ctx.Crop(new Rectangle((int)x, (int)y, (int)w, (int)h)));
        return Result.Success(faceRegion);
    }
    
    private static Result<(Image<Rgba32> left, Image<Rgba32> right)> ExtractFaceHalves(
        Image<Rgba32> faceRegion,
        FaceLandmarks68 landmarks)
    {
        // Validate inputs
        if (faceRegion == null)
        {
            return Result.Failure<(Image<Rgba32>, Image<Rgba32>)>("Unable to process face halves: Face region extraction failed in a previous step");
        }
        
        if (faceRegion.Width <= 0 || faceRegion.Height <= 0)
        {
            return Result.Failure<(Image<Rgba32>, Image<Rgba32>)>($"Face region dimensions invalid for symmetry analysis: {faceRegion.Width}x{faceRegion.Height}px. Minimum required: 2x1px");
        }
        
        var centerX = faceRegion.Width / 2;
        
        // Ensure we have valid dimensions for both halves
        if (centerX <= 0)
        {
            return Result.Failure<(Image<Rgba32>, Image<Rgba32>)>($"Face region too narrow for symmetry analysis: width={faceRegion.Width}px. Minimum required: 2px");
        }
        
        // Extract left and right halves
        var leftHalf = faceRegion.Clone(ctx => ctx.Crop(new Rectangle(0, 0, centerX, faceRegion.Height)));
        var rightHalf = faceRegion.Clone(ctx => ctx.Crop(new Rectangle(centerX, 0, centerX, faceRegion.Height)));
        
        // Mirror the right half for comparison
        rightHalf.Mutate(ctx => ctx.Flip(FlipMode.Horizontal));
        
        return Result.Success((leftHalf, rightHalf));
    }
    
    private static Result<GaborResponses> ApplyGaborFilters(
        Image<Rgba32> leftHalf,
        Image<Rgba32> rightHalf)
    {
        // Validate inputs
        if (leftHalf == null || rightHalf == null)
        {
            return Result.Failure<GaborResponses>("Unable to apply Gabor filters: Face halves extraction failed in a previous step");
        }
        
        if (Filters == null || Filters.Length == 0)
        {
            return Result.Failure<GaborResponses>("Internal error: Gabor wavelet filters were not properly initialized. This may indicate a configuration issue");
        }
        
        var leftResponses = new float[Filters.Length][,];
        var rightResponses = new float[Filters.Length][,];
        
        // Halves are owned and disposed by the caller.
        for (int i = 0; i < Filters.Length; i++)
        {
            leftResponses[i] = ApplyGaborFilter(leftHalf, Filters[i]);
            rightResponses[i] = ApplyGaborFilter(rightHalf, Filters[i]);
        }
        
        return Result.Success(new GaborResponses(leftResponses.ToList(), rightResponses.ToList()));
    }
    
    private static float[,] ApplyGaborFilter(Image<Rgba32> image, GaborFilter filter)
    {
        var width = image.Width;
        var height = image.Height;
        var result = new float[width, height];
        
        // Convert to grayscale intensity values (0-1 range)
        var grayValues = new float[width, height];
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    var pixel = row[x];
                    // Normalize to 0-1 range
                    grayValues[x, y] = (GrayscaleRedCoefficient * pixel.R + 
                                       GrayscaleGreenCoefficient * pixel.G + 
                                       GrayscaleBlueCoefficient * pixel.B) / 255f;
                }
            }
        });
        
        // Apply Gabor filter (convolution)
        var halfKernel = GaborKernelSize / 2;
        for (var y = halfKernel; y < height - halfKernel; y++)
        {
            for (var x = halfKernel; x < width - halfKernel; x++)
            {
                var sum = 0f;
                for (var ky = -halfKernel; ky <= halfKernel; ky++)
                {
                    for (var kx = -halfKernel; kx <= halfKernel; kx++)
                    {
                        sum += grayValues[x + kx, y + ky] * filter.Kernel[kx + halfKernel, ky + halfKernel];
                    }
                }
                result[x, y] = MathF.Abs(sum); // Use magnitude
            }
        }
        
        return result;
    }
    
    private static AsymmetryResult CalculateAsymmetry(GaborResponses responses)
    {
        var asymmetryValues = new List<float>();
        var totalAsymmetry = 0f;
        
        for (var i = 0; i < responses.LeftResponses.Count; i++)
        {
            var left = responses.LeftResponses[i];
            var right = responses.RightResponses[i];
            var asymmetry = CalculateResponseDifference(left, right);
            asymmetryValues.Add(asymmetry);
            totalAsymmetry += asymmetry;
        }
        
        var averageAsymmetry = totalAsymmetry / responses.LeftResponses.Count;
        
        // Separate illumination and pose asymmetry based on orientation
        var illuminationAsymmetry = (asymmetryValues[0] + asymmetryValues[4]) / 2f; // Horizontal orientations
        var poseAsymmetry = (asymmetryValues[1] + asymmetryValues[2] + asymmetryValues[3]) / 3f; // Diagonal orientations
        
        return new AsymmetryResult(
            averageAsymmetry,
            illuminationAsymmetry,
            poseAsymmetry,
            asymmetryValues);
    }
    
    private static float CalculateResponseDifference(float[,] left, float[,] right)
    {
        var width = left.GetLength(0);
        var height = left.GetLength(1);
        var pixelCount = width * height;
        
        if (pixelCount == 0)
            return 0f;
        
        // Calculate mean absolute difference between left and right responses
        var totalAbsDiff = 0f;
        var totalMagnitude = 0f;
        
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var leftVal = left[x, y];
                var rightVal = right[x, y];
                
                totalAbsDiff += MathF.Abs(leftVal - rightVal);
                totalMagnitude += (MathF.Abs(leftVal) + MathF.Abs(rightVal)) / 2f;
            }
        }
        
        var meanAbsDiff = totalAbsDiff / pixelCount;
        var meanMagnitude = totalMagnitude / pixelCount;
        
        // Normalize by the average response magnitude to get relative asymmetry
        // This gives asymmetry as a fraction of the typical response strength
        if (meanMagnitude < MinResponseThreshold)
            return 0f; // No significant response, so no meaningful asymmetry
            
        var relativeAsymmetry = meanAbsDiff / meanMagnitude;
        
        // Clamp to reasonable range - relative asymmetry > 2.0 indicates severe asymmetry
        // Values around 1.0 are actually normal due to noise and subtle variations
        var normalizedAsymmetry = MathF.Max(0f, MathF.Min((relativeAsymmetry - 0.8f) / 1.2f, 1f));
        
        // Ensure result is valid
        if (float.IsNaN(normalizedAsymmetry) || float.IsInfinity(normalizedAsymmetry))
            return 0f;
            
        return normalizedAsymmetry;
    }
    
    private static Result<FacialSymmetryScore> CreateSymmetryScore(AsymmetryResult asymmetry)
    {
        // FacialSymmetryScore expects raw asymmetry values and applies the final inversion itself.
        return FacialSymmetryScore.FromAsymmetryValues(
            asymmetry.IlluminationAsymmetry,
            asymmetry.PoseAsymmetry,
            asymmetry.GaborResponses);
    }
    
    /// <summary>
    /// Transforms asymmetry measure to quality score using sigmoid function
    /// </summary>
    /// <param name="asymmetry">Raw asymmetry measure (0 = perfect symmetry, 1 = maximum asymmetry)</param>
    /// <param name="steepness">Controls how quickly quality degrades with asymmetry</param>
    /// <param name="midpoint">Asymmetry level that results in 50% quality score</param>
    /// <returns>Quality score between 0 and 1</returns>
    private static float SigmoidQualityTransform(float asymmetry, float steepness, float midpoint)
    {
        // Sigmoid function: 1 / (1 + exp(steepness * (asymmetry - midpoint)))
        var exponent = steepness * (asymmetry - midpoint);
        
        // Prevent overflow for very large exponents
        if (exponent > 20f)
            return 0f;
        if (exponent < -20f)
            return 1f;
            
        var quality = 1f / (1f + MathF.Exp(exponent));
        
        // Ensure valid range
        return MathF.Max(0f, MathF.Min(quality, 1f));
    }
    
    private static GaborFilter[] CreateGaborFilters()
    {
        return Orientations.Select(orientation => CreateGaborFilter(orientation)).ToArray();
    }
    
    private static GaborFilter CreateGaborFilter(float orientation)
    {
        var kernel = new float[GaborKernelSize, GaborKernelSize];
        var sigma = GaborSigma;
        var lambda = GaborLambda;
        var gamma = GaborGamma;
        var psi = GaborPsi;
        
        var theta = orientation * MathF.PI / 180f;
        var halfSize = GaborKernelSize / 2;
        
        // First pass: compute kernel values
        for (var y = -halfSize; y <= halfSize; y++)
        {
            for (var x = -halfSize; x <= halfSize; x++)
            {
                var xPrime = x * MathF.Cos(theta) + y * MathF.Sin(theta);
                var yPrime = -x * MathF.Sin(theta) + y * MathF.Cos(theta);
                
                var exponent = -(xPrime * xPrime + gamma * gamma * yPrime * yPrime) / (2 * sigma * sigma);
                
                // Prevent extreme values that could cause overflow
                if (exponent < -20f)
                {
                    kernel[x + halfSize, y + halfSize] = 0f;
                    continue;
                }
                
                var gaussian = MathF.Exp(exponent);
                var sinusoidal = MathF.Cos(2 * MathF.PI * xPrime / lambda + psi);
                
                kernel[x + halfSize, y + halfSize] = gaussian * sinusoidal;
            }
        }
        
        // Normalize kernel: zero mean and L2 norm = 1
        var sum = 0f;
        var sumSquared = 0f;
        for (var y = 0; y < GaborKernelSize; y++)
        {
            for (var x = 0; x < GaborKernelSize; x++)
            {
                sum += kernel[x, y];
                sumSquared += kernel[x, y] * kernel[x, y];
            }
        }
        
        var mean = sum / (GaborKernelSize * GaborKernelSize);
        
        // Remove DC component (zero mean)
        for (var y = 0; y < GaborKernelSize; y++)
        {
            for (var x = 0; x < GaborKernelSize; x++)
            {
                kernel[x, y] -= mean;
            }
        }
        
        // Recalculate sum of squares after mean removal
        sumSquared = 0f;
        for (var y = 0; y < GaborKernelSize; y++)
        {
            for (var x = 0; x < GaborKernelSize; x++)
            {
                sumSquared += kernel[x, y] * kernel[x, y];
            }
        }
        
        // Normalize to L2 norm = 1
        var l2Norm = MathF.Sqrt(sumSquared);
        if (l2Norm > 0f)
        {
            for (var y = 0; y < GaborKernelSize; y++)
            {
                for (var x = 0; x < GaborKernelSize; x++)
                {
                    kernel[x, y] /= l2Norm;
                }
            }
        }
        
        return new GaborFilter(orientation, kernel);
    }
    
    /// <summary>
    /// Converts raw asymmetry measure to percentage for compliance evaluation
    /// </summary>
    private static float ConvertAsymmetryToPercentage(float asymmetry)
    {
        // Convert asymmetry to a percentage where higher percentage = better symmetry
        // Using sigmoid transformation but returning percentage of symmetry quality
        const float sigmoidSteepness = 8f;
        const float sigmoidMidpoint = 0.2f;
        
        var symmetryQuality = SigmoidQualityTransform(asymmetry, sigmoidSteepness, sigmoidMidpoint);
        return symmetryQuality * 100f;
    }
    
    private record GaborFilter(float Orientation, float[,] Kernel);
    private record GaborResponses(List<float[,]> LeftResponses, List<float[,]> RightResponses);
    private record AsymmetryResult(
        float TotalAsymmetry,
        float IlluminationAsymmetry,
        float PoseAsymmetry,
        List<float> GaborResponses);
}

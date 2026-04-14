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
using CSharpFunctionalExtensions;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Domain.Quality.Assessors;

/// <summary>
/// Assesses image sharpness using DCT-based frequency analysis as per ISO/IEC 19794-5
/// </summary>
[PublicAPI]
public static class SharpnessAssessor
{
    private const int DctBlockSize = 8;
    private const float DefaultHighFrequencyRatio = 0.2f;
    
    // Grayscale conversion coefficients (ITU-R BT.709 standard)
    private const float GrayscaleRedCoefficient = 0.299f;
    private const float GrayscaleGreenCoefficient = 0.587f;
    private const float GrayscaleBlueCoefficient = 0.114f;
    
    // Sharpness score normalization factor.
    // This is tuned empirically for the current DCT implementation and should be
    // validated by monotonic blur-progression tests rather than fixed score folklore.
    private const float SharpnessNormalizationFactor = 294f;
    private const float MaxNormalizedSharpness = 1f;
    
    // Regional sharpness threshold for critical regions
    private const float CriticalRegionSharpnessThreshold = 0.4f;
    
    // Center region weight multiplier for overall score
    private const float CenterRegionWeightMultiplier = 2f;
    
    /// <summary>
    /// Assesses image sharpness using DCT frequency analysis
    /// </summary>
    public static Result<SharpnessScore> Assess(Image<Rgba32> image)
    {
        return ConvertToGrayscale(image)
            .Bind(grayscale => AnalyzeSharpness(grayscale))
            .Bind(analysis => CreateSharpnessScore(analysis));
    }

    /// <summary>
    /// Assesses image sharpness within a specific region of interest using DCT frequency analysis
    /// </summary>
    /// <param name="image">The image to analyze</param>
    /// <param name="roi">The region of interest to analyze (if null, analyzes entire image)</param>
    public static Result<SharpnessScore> Assess(Image<Rgba32> image, Rectangle? roi)
    {
        if (roi == null)
        {
            return Assess(image);
        }
        
        return ConvertToGrayscale(image, roi.Value)
            .Bind(grayscale => AnalyzeSharpness(grayscale))
            .Bind(analysis => CreateSharpnessScore(analysis));
    }

    /// <summary>
    /// Measures image sharpness and returns raw percentage for compliance evaluation
    /// </summary>
    public static Result<SharpnessMeasurement> MeasureSharpness(Image<Rgba32> image)
    {
        return ConvertToGrayscale(image)
            .Bind(grayscale => AnalyzeSharpness(grayscale))
            .Map(analysis => new SharpnessMeasurement(
                OverallSharpnessPercent: ConvertToPercentage(analysis.HighFrequencyRatio),
                HighFrequencyRatio: analysis.HighFrequencyRatio,
                RegionalScores: analysis.RegionalScores
            ));
    }
    
    /// <summary>
    /// Measures image sharpness within a specific region of interest
    /// </summary>
    /// <param name="image">The image to analyze</param>
    /// <param name="roi">The region of interest to analyze (if null, analyzes entire image)</param>
    public static Result<SharpnessMeasurement> MeasureSharpness(Image<Rgba32> image, Rectangle? roi)
    {
        if (roi == null)
        {
            return MeasureSharpness(image);
        }
        
        return ConvertToGrayscale(image, roi.Value)
            .Bind(grayscale => AnalyzeSharpness(grayscale))
            .Map(analysis => new SharpnessMeasurement(
                OverallSharpnessPercent: ConvertToPercentage(analysis.HighFrequencyRatio),
                HighFrequencyRatio: analysis.HighFrequencyRatio,
                RegionalScores: analysis.RegionalScores
            ));
    }
    
    private static Result<float[,]> ConvertToGrayscale(Image<Rgba32> image)
    {
        // Validate input
        if (image == null)
        {
            return Result.Failure<float[,]>("Unable to analyze sharpness: No image data provided");
        }
        
        var width = image.Width;
        var height = image.Height;
        
        if (width <= 0 || height <= 0)
        {
            return Result.Failure<float[,]>($"Image dimensions invalid for sharpness analysis: {width}x{height}px. Both width and height must be greater than 0");
        }
        
        var grayscale = new float[height, width];
        
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    var pixel = row[x];
                    grayscale[y, x] = GrayscaleRedCoefficient * pixel.R + 
                                     GrayscaleGreenCoefficient * pixel.G + 
                                     GrayscaleBlueCoefficient * pixel.B;
                }
            }
        });
        
        return Result.Success(grayscale);
    }
    
    private static Result<float[,]> ConvertToGrayscale(Image<Rgba32> image, Rectangle roi)
    {
        // Validate input
        if (image == null)
        {
            return Result.Failure<float[,]>("Unable to analyze sharpness: No image data provided");
        }
        
        // Validate ROI bounds
        if (roi.X < 0 || roi.Y < 0 || roi.X + roi.Width > image.Width || roi.Y + roi.Height > image.Height)
        {
            return Result.Failure<float[,]>($"ROI bounds ({roi.X},{roi.Y},{roi.Width}x{roi.Height}) exceed image dimensions ({image.Width}x{image.Height})");
        }
        
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return Result.Failure<float[,]>($"ROI dimensions invalid for sharpness analysis: {roi.Width}x{roi.Height}px. Both width and height must be greater than 0");
        }
        
        var grayscale = new float[roi.Height, roi.Width];
        
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < roi.Height; y++)
            {
                var sourceY = roi.Y + y;
                if (sourceY >= image.Height) break;
                
                var row = accessor.GetRowSpan(sourceY);
                for (var x = 0; x < roi.Width; x++)
                {
                    var sourceX = roi.X + x;
                    if (sourceX >= image.Width) break;
                    
                    var pixel = row[sourceX];
                    grayscale[y, x] = GrayscaleRedCoefficient * pixel.R + 
                                     GrayscaleGreenCoefficient * pixel.G + 
                                     GrayscaleBlueCoefficient * pixel.B;
                }
            }
        });
        
        return Result.Success(grayscale);
    }
    
    private static Result<SharpnessAnalysis> AnalyzeSharpness(float[,] grayscale)
    {
        // Validate input
        if (grayscale == null)
        {
            return Result.Failure<SharpnessAnalysis>("Unable to analyze sharpness: Grayscale conversion failed in a previous step");
        }
        
        var height = grayscale.GetLength(0);
        var width = grayscale.GetLength(1);
        
        if (height < DctBlockSize || width < DctBlockSize)
        {
            return Result.Failure<SharpnessAnalysis>($"Image too small for DCT-based sharpness analysis: {width}x{height}px. Minimum required: {DctBlockSize}x{DctBlockSize}px");
        }
        
        var blockRows = height / DctBlockSize;
        var blockCols = width / DctBlockSize;
        
        if (blockRows == 0 || blockCols == 0)
        {
            return Result.Failure<SharpnessAnalysis>($"Image too small for meaningful sharpness analysis: Only {blockRows}x{blockCols} DCT blocks available. At least 1x1 block required");
        }
            
            var totalHighFrequency = 0f;
            var totalEnergy = 0f;
            var regionalScores = new Dictionary<string, float>();
            
            // Analyze different regions
            var regions = new[]
            {
                ("Center", blockRows / 3, blockCols / 3, 2 * blockRows / 3, 2 * blockCols / 3),
                ("Top", 0, 0, blockRows / 3, blockCols),
                ("Bottom", 2 * blockRows / 3, 0, blockRows, blockCols),
                ("Left", 0, 0, blockRows, blockCols / 3),
                ("Right", 0, 2 * blockCols / 3, blockRows, blockCols)
            };
            
            foreach (var (name, r1, c1, r2, c2) in regions)
            {
                var regionHighFreq = 0f;
                var regionTotal = 0f;
                
                for (var row = r1; row < r2; row++)
                {
                    for (var col = c1; col < c2; col++)
                    {
                        var block = ExtractBlock(grayscale, row * DctBlockSize, col * DctBlockSize);
                        var dctBlock = ApplyDct2D(block);
                        
                        var (highFreq, total) = CalculateFrequencyEnergy(dctBlock);
                        regionHighFreq += highFreq;
                        regionTotal += total;
                    }
                }
                
                var regionScore = regionTotal > 0 ? regionHighFreq / regionTotal : 0f;
                
                // Ensure score is valid
                if (float.IsNaN(regionScore) || float.IsInfinity(regionScore))
                    regionScore = 0f;
                    
                regionalScores[name] = regionScore;
                
                if (name == "Center")
                {
                    // Weight center region more heavily
                    totalHighFrequency += regionHighFreq * CenterRegionWeightMultiplier;
                    totalEnergy += regionTotal * CenterRegionWeightMultiplier;
                }
                else
                {
                    totalHighFrequency += regionHighFreq;
                    totalEnergy += regionTotal;
                }
            }
            
            var overallRatio = totalEnergy > 0 ? totalHighFrequency / totalEnergy : 0f;
            
            // Ensure ratio is valid
            if (float.IsNaN(overallRatio) || float.IsInfinity(overallRatio))
                overallRatio = 0f;
                
            // Validate all regional scores
            foreach (var key in regionalScores.Keys.ToList())
            {
                var score = regionalScores[key];
                if (float.IsNaN(score) || float.IsInfinity(score))
                    regionalScores[key] = 0f;
            }
            
            return Result.Success(new SharpnessAnalysis(overallRatio, regionalScores));
    }
    
    private static float[,] ExtractBlock(float[,] image, int startRow, int startCol)
    {
        var block = new float[DctBlockSize, DctBlockSize];
        var height = image.GetLength(0);
        var width = image.GetLength(1);
        
        for (var i = 0; i < DctBlockSize; i++)
        {
            for (var j = 0; j < DctBlockSize; j++)
            {
                var row = (int)MathF.Min(startRow + i, height - 1);
                var col = (int)MathF.Min(startCol + j, width - 1);
                block[i, j] = image[row, col];
            }
        }
        
        return block;
    }
    
    private static float[,] ApplyDct2D(float[,] block)
    {
        var result = new float[DctBlockSize, DctBlockSize];
        var temp = new float[DctBlockSize, DctBlockSize];
        
        // Apply 1D DCT to rows
        for (var i = 0; i < DctBlockSize; i++)
        {
            for (var j = 0; j < DctBlockSize; j++)
            {
                var sum = 0f;
                for (var k = 0; k < DctBlockSize; k++)
                {
                    sum += block[i, k] * (float)MathF.Cos((2 * k + 1) * j * MathF.PI / (2 * DctBlockSize));
                }
                temp[i, j] = sum * (j == 0 ? 1f / MathF.Sqrt(2) : 1f);
            }
        }
        
        // Apply 1D DCT to columns
        for (var j = 0; j < DctBlockSize; j++)
        {
            for (var i = 0; i < DctBlockSize; i++)
            {
                var sum = 0f;
                for (var k = 0; k < DctBlockSize; k++)
                {
                    sum += temp[k, j] * (float)MathF.Cos((2 * k + 1) * i * MathF.PI / (2 * DctBlockSize));
                }
                result[i, j] = sum * (i == 0 ? 1f / MathF.Sqrt(2) : 1f) * 2f / DctBlockSize;
            }
        }
        
        return result;
    }
    
    private static (float highFrequency, float total) CalculateFrequencyEnergy(float[,] dctBlock)
    {
        var total = 0f;
        var highFrequency = 0f;
        
        for (var i = 0; i < DctBlockSize; i++)
        {
            for (var j = 0; j < DctBlockSize; j++)
            {
                var energy = dctBlock[i, j] * dctBlock[i, j];
                total += energy;
                
                // Consider high frequency if outside the low-frequency corner
                // Use a more reasonable threshold: exclude only the DC and very low freq components
                if (i + j > 2)  // More sensitive to mid and high frequencies
                {
                    highFrequency += energy;
                }
            }
        }
        
        return (highFrequency, total);
    }
    
    private static Result<SharpnessScore> CreateSharpnessScore(SharpnessAnalysis analysis)
    {
        // Map high frequency ratio to quality score
        // Higher frequency content indicates better sharpness
        var normalizedScore = MathF.Min(analysis.HighFrequencyRatio * SharpnessNormalizationFactor, MaxNormalizedSharpness);
        
        return SharpnessScore.FromDctAnalysis(normalizedScore, analysis.RegionalScores);
    }
    
    /// <summary>
    /// Converts high frequency ratio to percentage for compliance evaluation
    /// </summary>
    private static float ConvertToPercentage(float highFrequencyRatio)
    {
        // Convert ratio to percentage - higher frequency = better sharpness
        var normalizedScore = MathF.Min(highFrequencyRatio * SharpnessNormalizationFactor, MaxNormalizedSharpness);
        return normalizedScore * 100f;
    }
    
    private record SharpnessAnalysis(
        float HighFrequencyRatio,
        IReadOnlyDictionary<string, float> RegionalScores);
}

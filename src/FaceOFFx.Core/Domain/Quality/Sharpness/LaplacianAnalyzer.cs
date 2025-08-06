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
using System.Collections.Immutable;
using System.Linq;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality.Sharpness;

/// <summary>
/// Analyzes image sharpness using Laplacian of Gaussian (LoG) operator
/// </summary>
[PublicAPI]
public static class LaplacianAnalyzer
{
    /// <summary>
    /// Standard Laplacian kernel for edge detection
    /// </summary>
    private static readonly ConvolutionKernel LaplacianKernel = new(
        ImmutableArray.Create(
            ImmutableArray.Create( 0f, -1f,  0f),
            ImmutableArray.Create(-1f,  4f, -1f),
            ImmutableArray.Create( 0f, -1f,  0f)),
        Size: 3);
    
    /// <summary>
    /// Laplacian of Gaussian kernel with σ = 1.4 (optimal for edge detection)
    /// </summary>
    private static readonly ConvolutionKernel LaplacianOfGaussian = BuildLoGKernel(1.4f);
    
    /// <summary>
    /// Analyzes sharpness using Laplacian variance method
    /// </summary>
    public static Result<LaplacianMetrics> Analyze(
        GrayscaleImage image,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        logger.LogDebug("Starting Laplacian sharpness analysis for {Width}x{Height} image", 
            image.Width, image.Height);
        
        return from normalized in NormalizeImage(image)
                   .Tap(_ => logger.LogDebug("Image normalized: mean={Mean:F4}, std={Std:F4}", 
                       _.Mean, _.StandardDeviation))
               from response in ApplyLaplacianOfGaussian(normalized)
                   .Tap(_ => logger.LogDebug("Laplacian response computed"))
               from metrics in ComputeMetrics(response)
                   .Tap(m => logger.LogDebug(
                       "Laplacian metrics: variance={Variance:F4}, kurtosis={Kurtosis:F2}, max={Max:F4}",
                       m.Variance, m.Kurtosis, m.MaxResponse))
               select metrics;
    }
    
    /// <summary>
    /// Analyzes sharpness and returns both the LoG response and metrics
    /// This allows for custom masking of the response before calculating statistics
    /// </summary>
    public static Result<(GrayscaleImage Response, LaplacianMetrics Metrics)> AnalyzeWithResponse(
        GrayscaleImage image,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        logger.LogDebug("Starting Laplacian sharpness analysis with response for {Width}x{Height} image", 
            image.Width, image.Height);
        
        return from normalized in NormalizeImage(image)
                   .Tap(_ => logger.LogDebug("Image normalized: mean={Mean:F4}, std={Std:F4}", 
                       _.Mean, _.StandardDeviation))
               from response in ApplyLaplacianOfGaussian(normalized)
                   .Tap(_ => logger.LogDebug("Laplacian response computed"))
               from metrics in ComputeMetrics(response)
                   .Tap(m => logger.LogDebug(
                       "Laplacian metrics: variance={Variance:F4}, kurtosis={Kurtosis:F2}, max={Max:F4}",
                       m.Variance, m.Kurtosis, m.MaxResponse))
               select (response, metrics);
    }
    
    /// <summary>
    /// Normalizes the image to zero mean and unit variance
    /// </summary>
    private static Result<NormalizedImage> NormalizeImage(GrayscaleImage image)
    {
        var pixels = image.GetFlattenedPixels();
        var mean = pixels.Average();
        var variance = pixels.Select(p => (p - mean) * (p - mean)).Average();
        var stdDev = MathF.Sqrt(variance);
        
        if (stdDev < 0.0001f)
        {
            return Result.Failure<NormalizedImage>(
                "Image has insufficient contrast for sharpness analysis (std dev < 0.0001)");
        }
        
        // Normalize to zero mean, unit variance
        var normalizedPixels = ImmutableArray.CreateBuilder<ImmutableArray<float>>(image.Height);
        for (int y = 0; y < image.Height; y++)
        {
            var row = ImmutableArray.CreateBuilder<float>(image.Width);
            for (int x = 0; x < image.Width; x++)
            {
                row.Add((image[y, x] - mean) / stdDev);
            }
            normalizedPixels.Add(row.ToImmutable());
        }
        
        return Result.Success(new NormalizedImage(
            normalizedPixels.ToImmutable(),
            image.Width,
            image.Height,
            0f, // Zero mean after normalization
            1f  // Unit variance after normalization
        ));
    }
    
    /// <summary>
    /// Applies Laplacian of Gaussian filter to detect edges
    /// </summary>
    private static Result<GrayscaleImage> ApplyLaplacianOfGaussian(NormalizedImage image)
    {
        return ImageConvolution.Convolve2D(image, LaplacianOfGaussian);
    }
    
    /// <summary>
    /// Computes sharpness metrics from Laplacian response
    /// </summary>
    private static Result<LaplacianMetrics> ComputeMetrics(GrayscaleImage laplacianResponse)
    {
        var pixels = laplacianResponse.GetFlattenedPixels();
        
        // Calculate variance (primary sharpness indicator)
        var mean = pixels.Average();
        var variance = pixels.Select(p => (p - mean) * (p - mean)).Average();
        var stdDev = MathF.Sqrt(variance);
        
        // Calculate kurtosis (peakedness of distribution)
        var kurtosis = ComputeKurtosis(pixels, mean, stdDev);
        
        // Find maximum absolute response
        var maxResponse = pixels.Select(Math.Abs).Max();
        
        // Build histogram for additional analysis
        var histogram = BuildHistogram(pixels, bins: 50);
        
        return Result.Success(new LaplacianMetrics(
            variance,
            stdDev,
            kurtosis,
            maxResponse,
            histogram));
    }
    
    /// <summary>
    /// Computes excess kurtosis of the distribution
    /// </summary>
    private static float ComputeKurtosis(ImmutableArray<float> values, float mean, float stdDev)
    {
        if (stdDev < 0.0001f) return 0f;
        
        var fourthMoment = values
            .Select(x => MathF.Pow((x - mean) / stdDev, 4))
            .Average();
            
        return fourthMoment - 3f; // Excess kurtosis (0 for normal distribution)
    }
    
    /// <summary>
    /// Builds a histogram of Laplacian response values
    /// </summary>
    private static ImmutableArray<float> BuildHistogram(ImmutableArray<float> values, int bins)
    {
        var min = values.Min();
        var max = values.Max();
        var range = max - min;
        
        if (range < 0.0001f)
        {
            // All values are the same
            var singleBin = ImmutableArray.CreateBuilder<float>(bins);
            singleBin.Add(values.Length);
            for (int i = 1; i < bins; i++)
                singleBin.Add(0);
            return singleBin.ToImmutable();
        }
        
        var histogram = new float[bins];
        var binWidth = range / bins;
        
        foreach (var value in values)
        {
            var binIndex = (int)((value - min) / binWidth);
            if (binIndex >= bins) binIndex = bins - 1;
            if (binIndex < 0) binIndex = 0;
            histogram[binIndex]++;
        }
        
        // Normalize histogram
        var total = values.Length;
        return histogram.Select(count => count / total).ToImmutableArray();
    }
    
    /// <summary>
    /// Builds a Laplacian of Gaussian kernel with specified sigma
    /// </summary>
    private static ConvolutionKernel BuildLoGKernel(float sigma)
    {
        // Kernel size should be at least 6σ + 1
        var size = (int)(6 * sigma + 1);
        if (size % 2 == 0) size++; // Ensure odd size
        
        var center = size / 2;
        var sigma2 = sigma * sigma;
        var kernel = ImmutableArray.CreateBuilder<ImmutableArray<float>>(size);
        
        for (int y = 0; y < size; y++)
        {
            var row = ImmutableArray.CreateBuilder<float>(size);
            for (int x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var r2 = dx * dx + dy * dy;
                
                // LoG formula: -1/(π·σ⁴) · (1 - r²/2σ²) · exp(-r²/2σ²)
                var value = -(1f / (MathF.PI * sigma2 * sigma2)) *
                           (1f - r2 / (2f * sigma2)) *
                           MathF.Exp(-r2 / (2f * sigma2));
                           
                row.Add(value);
            }
            kernel.Add(row.ToImmutable());
        }
        
        var result = new ConvolutionKernel(kernel.ToImmutable(), size);
        
        // Normalize to zero sum (important for LoG)
        var sum = result.Sum;
        if (Math.Abs(sum) > 0.0001f)
        {
            // Subtract mean to ensure zero sum
            var mean = sum / (size * size);
            var adjusted = result.Values.Select(row =>
                row.Select(v => v - mean).ToImmutableArray()
            ).ToImmutableArray();
            result = new ConvolutionKernel(adjusted, size);
        }
        
        return result;
    }
}

/// <summary>
/// Image convolution operations
/// </summary>
[PublicAPI]
public static class ImageConvolution
{
    /// <summary>
    /// Performs 2D convolution on a grayscale image
    /// </summary>
    public static Result<GrayscaleImage> Convolve2D(
        GrayscaleImage image,
        ConvolutionKernel kernel)
    {
        var output = ImmutableArray.CreateBuilder<ImmutableArray<float>>(image.Height);
        var kernelCenter = kernel.Center;
        
        for (int y = 0; y < image.Height; y++)
        {
            var row = ImmutableArray.CreateBuilder<float>(image.Width);
            
            for (int x = 0; x < image.Width; x++)
            {
                float sum = 0;
                
                // Apply kernel
                for (int ky = 0; ky < kernel.Size; ky++)
                {
                    for (int kx = 0; kx < kernel.Size; kx++)
                    {
                        var imageY = y + ky - kernelCenter;
                        var imageX = x + kx - kernelCenter;
                        
                        // Handle boundaries with zero padding
                        if (imageY >= 0 && imageY < image.Height &&
                            imageX >= 0 && imageX < image.Width)
                        {
                            sum += image[imageY, imageX] * kernel[ky, kx];
                        }
                    }
                }
                
                row.Add(sum);
            }
            
            output.Add(row.ToImmutable());
        }
        
        return Result.Success(new GrayscaleImage(
            output.ToImmutable(),
            image.Width,
            image.Height));
    }
}
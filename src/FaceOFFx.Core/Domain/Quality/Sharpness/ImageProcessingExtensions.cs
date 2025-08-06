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
using CSharpFunctionalExtensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality.Sharpness;

/// <summary>
/// Extension methods for image processing operations
/// </summary>
[PublicAPI]
public static class ImageProcessingExtensions
{
    // ITU-R BT.709 luma coefficients
    private const float LumaRed = 0.2126f;
    private const float LumaGreen = 0.7152f;
    private const float LumaBlue = 0.0722f;
    
    /// <summary>
    /// Extracts a grayscale region of interest from an RGBA image
    /// </summary>
    public static Result<GrayscaleImage> ExtractGrayscaleRoi(
        this Image<Rgba32> image,
        Rectangle roi)
    {
        if (image == null)
        {
            return Result.Failure<GrayscaleImage>("Image cannot be null");
        }
        
        // Validate ROI bounds
        if (roi.X < 0 || roi.Y < 0 || 
            roi.X + roi.Width > image.Width || 
            roi.Y + roi.Height > image.Height)
        {
            return Result.Failure<GrayscaleImage>(
                $"ROI bounds ({roi.X},{roi.Y},{roi.Width}x{roi.Height}) exceed image dimensions ({image.Width}x{image.Height})");
        }
        
        if (roi.Width <= 0 || roi.Height <= 0)
        {
            return Result.Failure<GrayscaleImage>(
                $"ROI dimensions must be positive, got {roi.Width}x{roi.Height}");
        }
        
        try
        {
            var pixels = ImmutableArray.CreateBuilder<ImmutableArray<float>>(roi.Height);
            
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < roi.Height; y++)
                {
                    var sourceY = roi.Y + y;
                    var rowSpan = accessor.GetRowSpan(sourceY);
                    var row = ImmutableArray.CreateBuilder<float>(roi.Width);
                    
                    for (int x = 0; x < roi.Width; x++)
                    {
                        var sourceX = roi.X + x;
                        var pixel = rowSpan[sourceX];
                        
                        // Convert to grayscale using ITU-R BT.709 luma
                        var gray = LumaRed * pixel.R + 
                                  LumaGreen * pixel.G + 
                                  LumaBlue * pixel.B;
                        
                        // Normalize to 0-1 range
                        row.Add(gray / 255f);
                    }
                    
                    pixels.Add(row.ToImmutable());
                }
            });
            
            return Result.Success(new GrayscaleImage(
                pixels.ToImmutable(),
                roi.Width,
                roi.Height));
        }
        catch (Exception ex)
        {
            return Result.Failure<GrayscaleImage>(
                $"Failed to extract grayscale ROI: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Converts an entire RGBA image to grayscale
    /// </summary>
    public static Result<GrayscaleImage> ToGrayscale(this Image<Rgba32> image)
    {
        return image.ExtractGrayscaleRoi(new Rectangle(0, 0, image.Width, image.Height));
    }
    
    /// <summary>
    /// Computes image statistics
    /// </summary>
    public static ImageStatistics ComputeStatistics(this GrayscaleImage image)
    {
        var pixels = image.GetFlattenedPixels();
        var mean = pixels.Average();
        var variance = pixels.Select(p => (p - mean) * (p - mean)).Average();
        var stdDev = MathF.Sqrt(variance);
        
        var sorted = pixels.OrderBy(p => p).ToArray();
        var median = sorted[sorted.Length / 2];
        var min = sorted[0];
        var max = sorted[^1];
        
        return new ImageStatistics(mean, stdDev, median, min, max);
    }
    
    /// <summary>
    /// Creates a binary edge map from an edge list
    /// </summary>
    public static BinaryImage ToBinaryImage(this EdgeMap edges, int width, int height)
    {
        var pixels = new bool[height, width];
        
        foreach (var edge in edges.Edges)
        {
            var x = (int)edge.Location.X;
            var y = (int)edge.Location.Y;
            
            if (x >= 0 && x < width && y >= 0 && y < height)
            {
                pixels[y, x] = true;
            }
        }
        
        return new BinaryImage(pixels, width, height);
    }
    
    /// <summary>
    /// Applies a threshold to create a binary image
    /// </summary>
    public static BinaryImage Threshold(this GrayscaleImage image, float threshold)
    {
        var pixels = new bool[image.Height, image.Width];
        
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                pixels[y, x] = image[y, x] > threshold;
            }
        }
        
        return new BinaryImage(pixels, image.Width, image.Height);
    }
}

/// <summary>
/// Basic image statistics
/// </summary>
[PublicAPI]
public record ImageStatistics(
    float Mean,
    float StandardDeviation,
    float Median,
    float Min,
    float Max)
{
    /// <summary>
    /// Coefficient of variation (relative standard deviation)
    /// </summary>
    public float CoefficientOfVariation => Mean > 0 ? StandardDeviation / Mean : 0;
    
    /// <summary>
    /// Dynamic range of the image
    /// </summary>
    public float DynamicRange => Max - Min;
}

/// <summary>
/// Binary image representation
/// </summary>
[PublicAPI]
public record BinaryImage(bool[,] Pixels, int Width, int Height)
{
    /// <summary>
    /// Gets the pixel value at the specified position
    /// </summary>
    public bool this[int y, int x] => Pixels[y, x];
    
    /// <summary>
    /// Counts the number of true pixels
    /// </summary>
    public int CountTrue()
    {
        int count = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (Pixels[y, x]) count++;
            }
        }
        return count;
    }
    
    /// <summary>
    /// Gets the density of true pixels (0-1)
    /// </summary>
    public float Density => CountTrue() / (float)(Width * Height);
}
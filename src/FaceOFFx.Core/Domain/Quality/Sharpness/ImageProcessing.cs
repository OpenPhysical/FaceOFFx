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

using System.Collections.Immutable;
using SixLabors.ImageSharp;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality.Sharpness;

/// <summary>
/// Represents a grayscale image with normalized pixel values
/// </summary>
[PublicAPI]
public record GrayscaleImage(
    ImmutableArray<ImmutableArray<float>> Pixels,
    int Width,
    int Height)
{
    /// <summary>
    /// Gets the pixel value at the specified position
    /// </summary>
    public float this[int y, int x] => Pixels[y][x];
    
    /// <summary>
    /// Total number of pixels in the image
    /// </summary>
    public int PixelCount => Width * Height;
    
    /// <summary>
    /// Image bounds as a rectangle
    /// </summary>
    public Rectangle Bounds => new(0, 0, Width, Height);
    
    /// <summary>
    /// Gets a flattened array of all pixel values
    /// </summary>
    public ImmutableArray<float> GetFlattenedPixels() =>
        Pixels.SelectMany(row => row).ToImmutableArray();
    
    /// <summary>
    /// Extracts a region of interest from the image
    /// </summary>
    public GrayscaleImage ExtractRegion(Rectangle roi)
    {
        var regionPixels = ImmutableArray.CreateBuilder<ImmutableArray<float>>(roi.Height);
        
        for (int y = 0; y < roi.Height; y++)
        {
            var row = ImmutableArray.CreateBuilder<float>(roi.Width);
            for (int x = 0; x < roi.Width; x++)
            {
                var sourceY = roi.Y + y;
                var sourceX = roi.X + x;
                
                if (sourceY >= 0 && sourceY < Height && sourceX >= 0 && sourceX < Width)
                {
                    row.Add(this[sourceY, sourceX]);
                }
                else
                {
                    row.Add(0f); // Pad with zeros if outside bounds
                }
            }
            regionPixels.Add(row.ToImmutable());
        }
        
        return new GrayscaleImage(regionPixels.ToImmutable(), roi.Width, roi.Height);
    }
}

/// <summary>
/// Represents a normalized grayscale image with zero mean
/// </summary>
[PublicAPI]
public record NormalizedImage(
    ImmutableArray<ImmutableArray<float>> Pixels,
    int Width,
    int Height,
    float Mean,
    float StandardDeviation) : GrayscaleImage(Pixels, Width, Height)
{
    /// <summary>
    /// Indicates if the image has sufficient contrast for analysis
    /// </summary>
    public bool HasSufficientContrast => StandardDeviation > 0.01f;
}

/// <summary>
/// Convolution kernel for image filtering
/// </summary>
[PublicAPI]
public record ConvolutionKernel(
    ImmutableArray<ImmutableArray<float>> Values,
    int Size)
{
    /// <summary>
    /// Center index of the kernel
    /// </summary>
    public int Center => Size / 2;
    
    /// <summary>
    /// Gets the kernel value at the specified position
    /// </summary>
    public float this[int y, int x] => Values[y][x];
    
    /// <summary>
    /// Sum of all kernel values (for normalization)
    /// </summary>
    public float Sum => Values.SelectMany(row => row).Sum();
    
    /// <summary>
    /// Creates a normalized version of this kernel
    /// </summary>
    public ConvolutionKernel Normalize()
    {
        var sum = Sum;
        if (Math.Abs(sum) < 0.0001f) return this; // Already normalized or zero-sum
        
        var normalized = Values.Select(row =>
            row.Select(v => v / sum).ToImmutableArray()
        ).ToImmutableArray();
        
        return new ConvolutionKernel(normalized, Size);
    }
}

/// <summary>
/// Represents a region definition for analysis
/// </summary>
[PublicAPI]
public record FaceRegionDefinition(
    FaceRegion Region,
    Rectangle Bounds,
    float ImportanceWeight = 1.0f)
{
    /// <summary>
    /// Area of the region in pixels
    /// </summary>
    public int Area => Bounds.Width * Bounds.Height;
    
    /// <summary>
    /// Center point of the region
    /// </summary>
    public Point Center => new(
        Bounds.X + Bounds.Width / 2,
        Bounds.Y + Bounds.Height / 2);
}
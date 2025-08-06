using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality.Sharpness;

/// <summary>
/// Extracts elliptical regions of interest for sharpness analysis
/// </summary>
[PublicAPI]
public static class EllipticalRoiExtractor
{
    /// <summary>
    /// Extracts grayscale pixels within an elliptical face region
    /// </summary>
    public static Result<GrayscaleImage> ExtractFaceEllipse(
        Image<Rgba32> image,
        FaceLandmarks68 landmarks)
    {
        return FaceEllipseCalculator.CalculateFromLandmarks(landmarks)
            .Bind(ellipse => ExtractEllipticalRegion(image, ellipse));
    }
    
    /// <summary>
    /// Analyzes sharpness within an elliptical face region using masked Laplacian
    /// This preserves spatial relationships by applying LoG to the full image first
    /// </summary>
    public static Result<float> AnalyzeEllipticalSharpness(
        Image<Rgba32> image,
        FaceLandmarks68 landmarks,
        Microsoft.Extensions.Logging.ILogger? logger = null)
    {
        return FaceEllipseCalculator.CalculateFromLandmarks(landmarks)
            .Bind(ellipse =>
            {
                var bounds = ellipse.GetBoundingBox();
                
                // Ensure bounds are within image
                var imageBounds = new Rectangle(0, 0, image.Width, image.Height);
                var clippedBounds = Rectangle.Intersect(bounds, imageBounds);
                
                if (clippedBounds.Width <= 0 || clippedBounds.Height <= 0)
                {
                    return Result.Failure<float>("Ellipse is outside image bounds");
                }
                
                // Extract the rectangular region containing the ellipse
                using var cropped = image.Clone(ctx => ctx.Crop(clippedBounds));
                
                // Convert to grayscale
                var grayscalePixels = new float[clippedBounds.Height, clippedBounds.Width];
                cropped.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < clippedBounds.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        for (int x = 0; x < clippedBounds.Width; x++)
                        {
                            var pixel = row[x];
                            // ITU-R BT.709 luma coefficients
                            grayscalePixels[y, x] = (0.2126f * pixel.R + 0.7152f * pixel.G + 0.0722f * pixel.B) / 255f;
                        }
                    }
                });
                
                // Create GrayscaleImage from rectangular region
                var rows = ImmutableArray.CreateBuilder<ImmutableArray<float>>(clippedBounds.Height);
                for (int y = 0; y < clippedBounds.Height; y++)
                {
                    var row = ImmutableArray.CreateBuilder<float>(clippedBounds.Width);
                    for (int x = 0; x < clippedBounds.Width; x++)
                    {
                        row.Add(grayscalePixels[y, x]);
                    }
                    rows.Add(row.ToImmutable());
                }
                var grayscaleImage = new GrayscaleImage(rows.ToImmutable(), clippedBounds.Width, clippedBounds.Height);
                
                // Apply Laplacian analysis and get the response image
                var analysisResult = LaplacianAnalyzer.AnalyzeWithResponse(grayscaleImage, logger);
                if (analysisResult.IsFailure)
                {
                    return Result.Failure<float>(analysisResult.Error);
                }
                
                var (logResponse, _) = analysisResult.Value;
                
                // Adjust ellipse center to cropped coordinates
                var adjustedEllipse = new FaceEllipse(
                    new Point2D(ellipse.Center.X - clippedBounds.X, ellipse.Center.Y - clippedBounds.Y),
                    ellipse.Width,
                    ellipse.Height,
                    ellipse.AngleRadians);
                
                // Collect LoG response values only for pixels inside the ellipse
                var maskedLogValues = new List<float>();
                for (int y = 0; y < clippedBounds.Height; y++)
                {
                    for (int x = 0; x < clippedBounds.Width; x++)
                    {
                        var point = new Point2D(x, y);
                        if (adjustedEllipse.Contains(point))
                        {
                            // Get the LoG response value at this pixel
                            maskedLogValues.Add(logResponse[y, x]);
                        }
                    }
                }
                
                if (maskedLogValues.Count == 0)
                {
                    return Result.Failure<float>("No pixels found inside ellipse");
                }
                
                // Calculate variance of the masked LoG response values
                var mean = maskedLogValues.Average();
                var maskedVariance = maskedLogValues.Select(v => (v - mean) * (v - mean)).Average();
                
                return Result.Success(maskedVariance);
            });
    }
    
    /// <summary>
    /// Extracts pixels within an ellipse as a grayscale image
    /// </summary>
    public static Result<GrayscaleImage> ExtractEllipticalRegion(
        Image<Rgba32> image,
        FaceEllipse ellipse)
    {
        var bounds = ellipse.GetBoundingBox();
        
        // Ensure bounds are within image
        var imageBounds = new Rectangle(0, 0, image.Width, image.Height);
        var clippedBounds = Rectangle.Intersect(bounds, imageBounds);
        
        if (clippedBounds.Width <= 0 || clippedBounds.Height <= 0)
        {
            return Result.Failure<GrayscaleImage>("Ellipse is outside image bounds");
        }
        
        // Extract pixels within ellipse
        var pixels = ExtractEllipsePixels(image, ellipse, clippedBounds);
        
        // Calculate actual dimensions of the extracted pixel arrangement
        var actualHeight = pixels.Length;
        var actualWidth = actualHeight > 0 ? pixels[0].Length : 0;
        
        return Result.Success(new GrayscaleImage(
            pixels,
            actualWidth,
            actualHeight));
    }
    
    private static ImmutableArray<ImmutableArray<float>> ExtractEllipsePixels(
        Image<Rgba32> image,
        FaceEllipse ellipse,
        Rectangle bounds)
    {
        // Collect only pixels that are actually inside the ellipse
        var interiorPixels = new List<float>();
        
        image.ProcessPixelRows(accessor =>
        {
            for (int y = bounds.Y; y < bounds.Y + bounds.Height; y++)
            {
                if (y >= 0 && y < image.Height)
                {
                    var rowSpan = accessor.GetRowSpan(y);
                    
                    for (int x = bounds.X; x < bounds.X + bounds.Width; x++)
                    {
                        if (x >= 0 && x < image.Width)
                        {
                            var point = new Point2D(x, y);
                            
                            // Only collect pixels that are actually inside the ellipse
                            if (ellipse.Contains(point))
                            {
                                var pixel = rowSpan[x];
                                // ITU-R BT.709 luma coefficients
                                var gray = 0.2126f * pixel.R + 0.7152f * pixel.G + 0.0722f * pixel.B;
                                interiorPixels.Add(gray / 255f);
                            }
                        }
                    }
                }
            }
        });
        
        // Convert collected pixels into square-ish 2D array for LoG analysis
        // This avoids artificial black pixels while maintaining 2D structure needed by convolution
        if (interiorPixels.Count == 0)
        {
            // Fallback: single pixel to avoid empty arrays
            return ImmutableArray.Create(ImmutableArray.Create(0f));
        }
        
        // Calculate dimensions for roughly square arrangement
        var pixelCount = interiorPixels.Count;
        var sideLength = (int)Math.Ceiling(Math.Sqrt(pixelCount));
        var rows = ImmutableArray.CreateBuilder<ImmutableArray<float>>();
        
        for (int i = 0; i < sideLength; i++)
        {
            var row = ImmutableArray.CreateBuilder<float>();
            for (int j = 0; j < sideLength; j++)
            {
                var pixelIndex = i * sideLength + j;
                if (pixelIndex < interiorPixels.Count)
                {
                    row.Add(interiorPixels[pixelIndex]);
                }
                else
                {
                    // For the last row, pad with the last available pixel value
                    // This avoids artificial edges since we're repeating real pixel content
                    row.Add(interiorPixels[interiorPixels.Count - 1]);
                }
            }
            rows.Add(row.ToImmutable());
        }
        
        return rows.ToImmutable();
    }
}

/// <summary>
/// Creates a visualization mask showing the elliptical ROI
/// </summary>
[PublicAPI]
public static class EllipseMaskGenerator
{
    /// <summary>
    /// Creates a binary mask image showing the face ellipse region
    /// </summary>
    public static Result<Image<L8>> CreateEllipseMask(
        int width,
        int height,
        FaceEllipse ellipse)
    {
        try
        {
            var mask = new Image<L8>(width, height);
            
            mask.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        var point = new Point2D(x, y);
                        row[x] = ellipse.Contains(point) ? new L8(255) : new L8(0);
                    }
                }
            });
            
            return Result.Success(mask);
        }
        catch (Exception ex)
        {
            return Result.Failure<Image<L8>>($"Failed to create ellipse mask: {ex.Message}");
        }
    }
}
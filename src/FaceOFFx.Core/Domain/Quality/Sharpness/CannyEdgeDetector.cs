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
using System.Collections.Immutable;
using System.Linq;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality.Sharpness;

/// <summary>
/// Implements the Canny edge detection algorithm
/// </summary>
[PublicAPI]
public static class CannyEdgeDetector
{
    /// <summary>
    /// Sobel operator for X-direction gradient
    /// </summary>
    private static readonly ConvolutionKernel SobelX = new(
        ImmutableArray.Create(
            ImmutableArray.Create(-1f, 0f, 1f),
            ImmutableArray.Create(-2f, 0f, 2f),
            ImmutableArray.Create(-1f, 0f, 1f)),
        Size: 3);
    
    /// <summary>
    /// Sobel operator for Y-direction gradient
    /// </summary>
    private static readonly ConvolutionKernel SobelY = new(
        ImmutableArray.Create(
            ImmutableArray.Create(-1f, -2f, -1f),
            ImmutableArray.Create( 0f,  0f,  0f),
            ImmutableArray.Create( 1f,  2f,  1f)),
        Size: 3);
    
    /// <summary>
    /// Detects edges using the Canny algorithm
    /// </summary>
    public static Result<CannyEdgeMetrics> Detect(
        GrayscaleImage image,
        CannyConfiguration config,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        using var scope = logger.BeginScope("CannyEdgeDetection");
        
        if (!config.IsValid)
        {
            return Result.Failure<CannyEdgeMetrics>(
                "Invalid Canny configuration: thresholds must be positive and high > low");
        }
        
        logger.LogDebug("Starting Canny edge detection with config: {@Config}", config);
        
        return from blurred in GaussianBlur(image, config.GaussianSigma)
                   .Tap(_ => logger.LogDebug("Applied Gaussian blur with σ={Sigma}", config.GaussianSigma))
               from gradients in ComputeGradients(blurred)
                   .Tap(g => logger.LogDebug("Computed gradients, max magnitude: {MaxMag:F2}", g.MaxMagnitude))
               from suppressed in NonMaximumSuppression(gradients)
                   .Tap(e => logger.LogDebug("Non-maximum suppression complete: {Count} candidates", e.Edges.Length))
               from edges in HysteresisThresholding(suppressed, config.LowThreshold, config.HighThreshold)
                   .Tap(e => logger.LogDebug("Hysteresis thresholding complete: {Strong} strong, {Weak} weak edges",
                       e.StrongEdges.Length, e.WeakEdges.Length))
               from segments in TraceEdgeSegments(edges)
                   .Tap(s => logger.LogDebug("Traced {Count} edge segments", s.Length))
               from metrics in ComputeEdgeMetrics(edges, segments, gradients, image.PixelCount)
                   .Tap(m => logger.LogDebug("Edge metrics: density={Density:F1}/Mpx, continuity={Continuity:F2}",
                       m.EdgeDensity, m.EdgeContinuity))
               select metrics;
    }
    
    /// <summary>
    /// Applies Gaussian blur to reduce noise
    /// </summary>
    private static Result<GrayscaleImage> GaussianBlur(GrayscaleImage image, float sigma)
    {
        var kernel = BuildGaussianKernel(sigma);
        return ImageConvolution.Convolve2D(image, kernel);
    }
    
    /// <summary>
    /// Computes gradient magnitude and direction using Sobel operators
    /// </summary>
    private static Result<GradientMap> ComputeGradients(GrayscaleImage image)
    {
        return from gradX in ImageConvolution.Convolve2D(image, SobelX)
               from gradY in ImageConvolution.Convolve2D(image, SobelY)
               select BuildGradientMap(gradX, gradY);
    }
    
    /// <summary>
    /// Builds a gradient map from X and Y gradients
    /// </summary>
    private static GradientMap BuildGradientMap(GrayscaleImage gradX, GrayscaleImage gradY)
    {
        var gradients = ImmutableArray.CreateBuilder<ImmutableArray<GradientPixel>>(gradX.Height);
        
        for (int y = 0; y < gradX.Height; y++)
        {
            var row = ImmutableArray.CreateBuilder<GradientPixel>(gradX.Width);
            for (int x = 0; x < gradX.Width; x++)
            {
                row.Add(new GradientPixel(
                    new Point2D(x, y),
                    gradX[y, x],
                    gradY[y, x]));
            }
            gradients.Add(row.ToImmutable());
        }
        
        return new GradientMap(gradients.ToImmutable(), gradX.Width, gradX.Height);
    }
    
    /// <summary>
    /// Performs non-maximum suppression to thin edges
    /// </summary>
    private static Result<EdgeMap> NonMaximumSuppression(GradientMap gradients)
    {
        var candidates = ImmutableArray.CreateBuilder<EdgePixel>();
        
        for (int y = 1; y < gradients.Height - 1; y++)
        {
            for (int x = 1; x < gradients.Width - 1; x++)
            {
                var current = gradients[y, x];
                if (current.Magnitude < 0.01f) continue; // Skip very weak gradients
                
                // Quantize gradient direction to 4 orientations
                var direction = QuantizeGradientDirection(current.OrientationDegrees);
                
                // Get interpolated neighbor magnitudes in gradient direction
                var (mag1, mag2) = GetNeighborMagnitudes(gradients, x, y, direction);
                
                // Keep only if local maximum along gradient direction
                if (current.Magnitude >= mag1 && current.Magnitude >= mag2)
                {
                    candidates.Add(new EdgePixel(
                        current.Location,
                        current.Magnitude,
                        current.Orientation,
                        EdgeType.Weak)); // Type determined later by thresholding
                }
            }
        }
        
        return Result.Success(new EdgeMap(candidates.ToImmutable()));
    }
    
    /// <summary>
    /// Quantizes gradient angle to one of 4 directions
    /// </summary>
    private static GradientDirection QuantizeGradientDirection(float angleDegrees)
    {
        // Normalize angle to [0, 180)
        while (angleDegrees < 0) angleDegrees += 180;
        while (angleDegrees >= 180) angleDegrees -= 180;
        
        return angleDegrees switch
        {
            < 22.5f => GradientDirection.Horizontal,
            < 67.5f => GradientDirection.Diagonal45,
            < 112.5f => GradientDirection.Vertical,
            < 157.5f => GradientDirection.Diagonal135,
            _ => GradientDirection.Horizontal
        };
    }
    
    /// <summary>
    /// Gets neighbor magnitudes in the gradient direction
    /// </summary>
    private static (float mag1, float mag2) GetNeighborMagnitudes(
        GradientMap gradients, int x, int y, GradientDirection direction)
    {
        return direction switch
        {
            GradientDirection.Horizontal => 
                (gradients[y, x - 1].Magnitude, gradients[y, x + 1].Magnitude),
                
            GradientDirection.Diagonal45 => 
                (gradients[y - 1, x + 1].Magnitude, gradients[y + 1, x - 1].Magnitude),
                
            GradientDirection.Vertical => 
                (gradients[y - 1, x].Magnitude, gradients[y + 1, x].Magnitude),
                
            GradientDirection.Diagonal135 => 
                (gradients[y - 1, x - 1].Magnitude, gradients[y + 1, x + 1].Magnitude),
                
            _ => (0f, 0f)
        };
    }
    
    /// <summary>
    /// Performs hysteresis thresholding to identify strong and weak edges
    /// </summary>
    private static Result<EdgeMap> HysteresisThresholding(
        EdgeMap candidates, float lowThreshold, float highThreshold)
    {
        // Classify edges as strong or weak
        var classifiedEdges = candidates.Edges.Select(edge =>
            edge with { Type = edge.Magnitude >= highThreshold ? EdgeType.Strong : EdgeType.Weak }
        ).ToImmutableArray();
        
        // Create spatial lookup for efficient neighbor queries
        var edgeLookup = classifiedEdges
            .GroupBy(e => ((int)e.Location.Y, (int)e.Location.X))
            .ToDictionary(g => g.Key, g => g.First());
        
        // Track which edges to keep
        var keepEdges = new HashSet<EdgePixel>(classifiedEdges.Where(e => e.IsStrong));
        var visited = new HashSet<(int, int)>();
        
        // BFS from each strong edge to connect weak edges
        var queue = new Queue<EdgePixel>(keepEdges);
        
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var pos = ((int)current.Location.Y, (int)current.Location.X);
            
            if (!visited.Add(pos)) continue;
            
            // Check 8-connected neighbors
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    
                    var neighborPos = (pos.Item1 + dy, pos.Item2 + dx);
                    
                    if (edgeLookup.TryGetValue(neighborPos, out var neighbor) &&
                        !visited.Contains(neighborPos) &&
                        neighbor.Magnitude >= lowThreshold)
                    {
                        keepEdges.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }
        
        return Result.Success(new EdgeMap(keepEdges.ToImmutableArray()));
    }
    
    /// <summary>
    /// Traces connected edge segments
    /// </summary>
    private static Result<ImmutableArray<EdgeSegment>> TraceEdgeSegments(EdgeMap edges)
    {
        var segments = ImmutableArray.CreateBuilder<EdgeSegment>();
        var visited = new HashSet<Point2D>();
        var edgeLookup = edges.CreateLookup();
        
        foreach (var startEdge in edges.Edges.OrderByDescending(e => e.Magnitude))
        {
            if (visited.Contains(startEdge.Location)) continue;
            
            // Trace connected component
            var segment = TraceSegment(startEdge, edgeLookup, visited);
            if (segment.Points.Length >= 3) // Keep only segments with at least 3 points
            {
                segments.Add(segment);
            }
        }
        
        return Result.Success(segments.ToImmutable());
    }
    
    /// <summary>
    /// Traces a single connected edge segment
    /// </summary>
    private static EdgeSegment TraceSegment(
        EdgePixel start,
        ImmutableDictionary<Point2D, EdgePixel> edgeLookup,
        HashSet<Point2D> visited)
    {
        var points = ImmutableArray.CreateBuilder<Point2D>();
        var magnitudes = new List<float>();
        var queue = new Queue<Point2D>();
        
        queue.Enqueue(start.Location);
        
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!visited.Add(current)) continue;
            
            if (edgeLookup.TryGetValue(current, out var edge))
            {
                points.Add(current);
                magnitudes.Add(edge.Magnitude);
                
                // Add unvisited neighbors
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        
                        var neighbor = new Point2D(current.X + dx, current.Y + dy);
                        if (!visited.Contains(neighbor) && edgeLookup.ContainsKey(neighbor))
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }
        }
        
        var segmentPoints = points.ToImmutable();
        var avgStrength = magnitudes.Any() ? magnitudes.Average() : 0f;
        var length = CalculateSegmentLength(segmentPoints);
        
        return new EdgeSegment(segmentPoints, avgStrength, length);
    }
    
    /// <summary>
    /// Calculates the length of an edge segment
    /// </summary>
    private static float CalculateSegmentLength(ImmutableArray<Point2D> points)
    {
        if (points.Length < 2) return 0f;
        
        float length = 0f;
        for (int i = 1; i < points.Length; i++)
        {
            var dx = points[i].X - points[i - 1].X;
            var dy = points[i].Y - points[i - 1].Y;
            length += MathF.Sqrt(dx * dx + dy * dy);
        }
        
        return length;
    }
    
    /// <summary>
    /// Computes comprehensive edge metrics
    /// </summary>
    private static Result<CannyEdgeMetrics> ComputeEdgeMetrics(
        EdgeMap edges,
        ImmutableArray<EdgeSegment> segments,
        GradientMap gradients,
        int totalPixels)
    {
        var strongCount = edges.StrongEdges.Length;
        var weakCount = edges.WeakEdges.Length;
        var totalEdges = strongCount + weakCount;
        
        // Edge density per megapixel
        var edgeDensity = totalEdges * 1_000_000f / totalPixels;
        
        // Edge continuity: ratio of edges in segments vs isolated edges
        var edgesInSegments = segments.SelectMany(s => s.Points).Distinct().Count();
        var edgeContinuity = totalEdges > 0 ? edgesInSegments / (float)totalEdges : 0f;
        
        // Mean gradient magnitude of edge pixels
        var meanGradient = edges.Edges.Any() 
            ? edges.Edges.Average(e => e.Magnitude)
            : 0f;
        
        return Result.Success(new CannyEdgeMetrics(
            strongCount,
            weakCount,
            edgeDensity,
            edgeContinuity,
            meanGradient,
            segments));
    }
    
    /// <summary>
    /// Builds a Gaussian kernel for blurring
    /// </summary>
    private static ConvolutionKernel BuildGaussianKernel(float sigma)
    {
        // Kernel size: 6σ + 1, ensure odd
        var size = (int)(6 * sigma + 1);
        if (size % 2 == 0) size++;
        
        var center = size / 2;
        var kernel = ImmutableArray.CreateBuilder<ImmutableArray<float>>(size);
        var sum = 0f;
        
        for (int y = 0; y < size; y++)
        {
            var row = ImmutableArray.CreateBuilder<float>(size);
            for (int x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var value = MathF.Exp(-(dx * dx + dy * dy) / (2f * sigma * sigma));
                row.Add(value);
                sum += value;
            }
            kernel.Add(row.ToImmutable());
        }
        
        // Normalize kernel
        var normalized = kernel.ToImmutable().Select(row =>
            row.Select(v => v / sum).ToImmutableArray()
        ).ToImmutableArray();
        
        return new ConvolutionKernel(normalized, size);
    }
}
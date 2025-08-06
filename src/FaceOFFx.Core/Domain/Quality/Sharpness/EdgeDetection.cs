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
using FaceOFFx.Core.Domain.Common;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality.Sharpness;

/// <summary>
/// Represents a single edge pixel with gradient information
/// </summary>
[PublicAPI]
public record EdgePixel(
    Point2D Location,
    float Magnitude,
    float Orientation,
    EdgeType Type = EdgeType.Weak)
{
    /// <summary>
    /// X coordinate of the edge pixel
    /// </summary>
    public int X => (int)Location.X;
    
    /// <summary>
    /// Y coordinate of the edge pixel
    /// </summary>
    public int Y => (int)Location.Y;
    
    /// <summary>
    /// Indicates if this is a strong edge
    /// </summary>
    public bool IsStrong => Type == EdgeType.Strong;
}

/// <summary>
/// Edge classification based on gradient magnitude
/// </summary>
[PublicAPI]
public enum EdgeType
{
    /// <summary>
    /// Edge with gradient magnitude below high threshold but above low threshold
    /// </summary>
    Weak,
    
    /// <summary>
    /// Edge with gradient magnitude above high threshold
    /// </summary>
    Strong
}

/// <summary>
/// Gradient information for a pixel
/// </summary>
[PublicAPI]
public record GradientPixel(
    Point2D Location,
    float GradientX,
    float GradientY)
{
    /// <summary>
    /// Gradient magnitude (strength)
    /// </summary>
    public float Magnitude => MathF.Sqrt(GradientX * GradientX + GradientY * GradientY);
    
    /// <summary>
    /// Gradient orientation in radians (-π to π)
    /// </summary>
    public float Orientation => MathF.Atan2(GradientY, GradientX);
    
    /// <summary>
    /// Gradient orientation in degrees (0 to 360)
    /// </summary>
    public float OrientationDegrees
    {
        get
        {
            var degrees = Orientation * 180f / MathF.PI;
            return degrees < 0 ? degrees + 360 : degrees;
        }
    }
}

/// <summary>
/// Represents a gradient map for edge detection
/// </summary>
[PublicAPI]
public record GradientMap(
    ImmutableArray<ImmutableArray<GradientPixel>> Gradients,
    int Width,
    int Height)
{
    /// <summary>
    /// Gets the gradient at the specified position
    /// </summary>
    public GradientPixel this[int y, int x] => Gradients[y][x];
    
    /// <summary>
    /// Gets the maximum gradient magnitude in the map
    /// </summary>
    public float MaxMagnitude => Gradients
        .SelectMany(row => row)
        .Max(g => g.Magnitude);
}

/// <summary>
/// Represents an edge map after non-maximum suppression and thresholding
/// </summary>
[PublicAPI]
public record EdgeMap(ImmutableArray<EdgePixel> Edges)
{
    /// <summary>
    /// Gets all strong edges
    /// </summary>
    public ImmutableArray<EdgePixel> StrongEdges => 
        Edges.Where(e => e.IsStrong).ToImmutableArray();
    
    /// <summary>
    /// Gets all weak edges
    /// </summary>
    public ImmutableArray<EdgePixel> WeakEdges => 
        Edges.Where(e => !e.IsStrong).ToImmutableArray();
    
    /// <summary>
    /// Creates a lookup for efficient neighbor queries
    /// </summary>
    public ImmutableDictionary<Point2D, EdgePixel> CreateLookup() =>
        Edges.ToImmutableDictionary(e => e.Location);
}

/// <summary>
/// Direction for gradient comparison in non-maximum suppression
/// </summary>
[PublicAPI]
public enum GradientDirection
{
    /// <summary>
    /// Horizontal direction (0° or 180°)
    /// </summary>
    Horizontal,
    
    /// <summary>
    /// Diagonal direction (45° or 225°)
    /// </summary>
    Diagonal45,
    
    /// <summary>
    /// Vertical direction (90° or 270°)
    /// </summary>
    Vertical,
    
    /// <summary>
    /// Diagonal direction (135° or 315°)
    /// </summary>
    Diagonal135
}

/// <summary>
/// Configuration for Canny edge detection
/// </summary>
[PublicAPI]
public record CannyConfiguration(
    float GaussianSigma = 1.4f,
    float LowThreshold = 50f,
    float HighThreshold = 150f,
    bool UseAdaptiveThresholds = false)
{
    /// <summary>
    /// Ratio of high to low threshold (typically 2-3)
    /// </summary>
    public float ThresholdRatio => HighThreshold / LowThreshold;
    
    /// <summary>
    /// Validates the configuration
    /// </summary>
    public bool IsValid => 
        GaussianSigma > 0 && 
        LowThreshold > 0 && 
        HighThreshold > LowThreshold;
}
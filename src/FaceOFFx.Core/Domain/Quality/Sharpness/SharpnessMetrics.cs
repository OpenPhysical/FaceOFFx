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
using FaceOFFx.Core.Domain.Common;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality.Sharpness;

/// <summary>
/// Comprehensive sharpness analysis results combining multiple measurement techniques
/// </summary>
[PublicAPI]
public record SharpnessAnalysis(
    LaplacianMetrics Laplacian,
    CannyEdgeMetrics Edges,
    FrequencyMetrics Frequency,
    ImmutableDictionary<FaceRegion, RegionMetrics> RegionalAnalysis,
    TimeSpan ProcessingTime)
{
    /// <summary>
    /// Gets a summary of the most significant quality issues detected
    /// </summary>
    public ImmutableArray<string> GetQualityIssues()
    {
        var issues = ImmutableArray.CreateBuilder<string>();
        
        if (Laplacian.Variance < 0.001f)
            issues.Add("Very low image sharpness detected");
            
        if (Edges.EdgeDensity < 100)
            issues.Add("Insufficient edge detail for reliable feature detection");
            
        if (Frequency.HighFrequencyEnergy < 0.1f)
            issues.Add("Image lacks fine detail (possible motion blur or defocus)");
            
        foreach (var (region, metrics) in RegionalAnalysis)
        {
            if (region == FaceRegion.Eyes && metrics.LocalSharpness < 0.3f)
                issues.Add("Eye region is not sufficiently sharp for reliable identification");
        }
        
        return issues.ToImmutable();
    }
}

/// <summary>
/// Metrics from Laplacian of Gaussian analysis
/// </summary>
[PublicAPI]
public record LaplacianMetrics(
    float Variance,
    float StandardDeviation,
    float Kurtosis,
    float MaxResponse,
    ImmutableArray<float> ResponseHistogram)
{
    /// <summary>
    /// Normalized variance (0-1) using sigmoid function for perceptually accurate quality assessment
    /// 
    /// Mathematical Foundation:
    /// - Sigmoid: 1 / (1 + exp(-(variance - midpoint) * steepness))
    /// - Parameters calibrated for proper masked elliptical analysis
    /// 
    /// This follows Weber-Fechner law of human perception where quality changes are:
    /// - Most noticeable in low-variance (blurry) region (steep slope)
    /// - Moderately noticeable in mid-variance (acceptable) region
    /// - Least noticeable in high-variance (sharp) region (asymptotic)
    /// 
    /// Calibration based on empirical data:
    /// - Blurry images (blur_20): ~0.00015 variance
    /// - Acceptable boundary: ~0.0015 variance (between blur_2 and blur_5)
    /// - Sharp photos: ~0.003-0.009 variance
    /// - Computer graphics: ~0.013 variance
    /// 
    /// Parameters: midpoint=0.002, steepness=1500
    /// This places the inflection point between blurry and acceptable quality
    /// </summary>
    public float NormalizedVariance => 
        1.0f / (1.0f + MathF.Exp(-(Variance - 0.002f) * 1500f));
}

/// <summary>
/// Metrics from Canny edge detection
/// </summary>
[PublicAPI]
public record CannyEdgeMetrics(
    int StrongEdgeCount,
    int WeakEdgeCount,
    float EdgeDensity,
    float EdgeContinuity,
    float MeanGradientMagnitude,
    ImmutableArray<EdgeSegment> EdgeSegments)
{
    /// <summary>
    /// Total number of edge pixels detected
    /// </summary>
    public int TotalEdgeCount => StrongEdgeCount + WeakEdgeCount;
    
    /// <summary>
    /// Ratio of strong edges to total edges (quality indicator)
    /// </summary>
    public float StrongEdgeRatio => TotalEdgeCount > 0 ? StrongEdgeCount / (float)TotalEdgeCount : 0;
}

/// <summary>
/// Represents a connected edge segment
/// </summary>
[PublicAPI]
public record EdgeSegment(
    ImmutableArray<Point2D> Points,
    float AverageStrength,
    float Length)
{
    /// <summary>
    /// Indicates if this is a significant edge (long and strong)
    /// </summary>
    public bool IsSignificant => Length > 10 && AverageStrength > 0.5f;
}

/// <summary>
/// Frequency domain analysis metrics
/// </summary>
[PublicAPI]
public record FrequencyMetrics(
    float HighFrequencyEnergy,
    float MidFrequencyEnergy,
    float LowFrequencyEnergy,
    float SpectralFalloff)
{
    /// <summary>
    /// Total energy across all frequency bands
    /// </summary>
    public float TotalEnergy => HighFrequencyEnergy + MidFrequencyEnergy + LowFrequencyEnergy;
    
    /// <summary>
    /// Ratio of high frequency to total energy (sharpness indicator)
    /// </summary>
    public float HighFrequencyRatio => TotalEnergy > 0 ? HighFrequencyEnergy / TotalEnergy : 0;
}

/// <summary>
/// Metrics for a specific facial region
/// </summary>
[PublicAPI]
public record RegionMetrics(
    FaceRegion Region,
    float LocalSharpness,
    float EdgeDensity,
    float TextureComplexity)
{
    /// <summary>
    /// Overall quality score for this region (0-1)
    /// </summary>
    public float OverallQuality => (LocalSharpness + EdgeDensity + TextureComplexity) / 3f;
}

/// <summary>
/// Progress information for sharpness analysis
/// </summary>
[PublicAPI]
public record AnalysisProgress(
    int PercentComplete,
    string CurrentStep,
    TimeSpan? EstimatedTimeRemaining = null)
{
    /// <summary>
    /// Indicates if the analysis is complete
    /// </summary>
    public bool IsComplete => PercentComplete >= 100;
}
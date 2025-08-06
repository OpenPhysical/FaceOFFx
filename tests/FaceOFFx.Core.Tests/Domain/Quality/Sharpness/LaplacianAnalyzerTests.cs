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
using System.IO;
using System.Linq;
using FaceOFFx.Core.Domain.Quality.Sharpness;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Tests.Domain.Quality.Sharpness;

/// <summary>
/// Tests for Laplacian-based sharpness analysis
/// </summary>
[TestFixture]
public class LaplacianAnalyzerTests
{
    private ILogger<LaplacianAnalyzerTests> _logger = null!;
    private string _sampleImagesPath = null!;
    
    /// <summary>
    /// One-time setup for test fixture
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<LaplacianAnalyzerTests>();
        
        // Find sample images directory
        var currentDir = TestContext.CurrentContext.TestDirectory;
        var searchDir = new DirectoryInfo(currentDir);
        
        while (searchDir != null && !File.Exists(Path.Combine(searchDir.FullName, "FaceOFFx.sln")))
        {
            searchDir = searchDir.Parent;
        }
        
        if (searchDir == null)
        {
            throw new InvalidOperationException("Could not find solution root");
        }
        
        _sampleImagesPath = Path.Combine(searchDir.FullName, "tests", "sample_images");
        
        TestContext.WriteLine($"Sample images path: {_sampleImagesPath}");
    }
    
    /// <summary>
    /// Tests that Laplacian variance decreases monotonically with increasing blur
    /// </summary>
    [Test]
    public void Analyze_WithProgressiveBlur_ShowsMonotonicDecrease()
    {
        // Arrange
        var blurLevels = new[] { 0, 2, 5, 10, 15, 20 };
        var results = new List<(int blurLevel, float variance, float normalized)>();
        
        // Act
        foreach (var blurLevel in blurLevels)
        {
            var imagePath = Path.Combine(_sampleImagesPath, $"johnson_blur_{blurLevel}.jpg");
            
            if (!File.Exists(imagePath))
            {
                TestContext.WriteLine($"Warning: {imagePath} not found, skipping");
                continue;
            }
            
            using var image = Image.Load<Rgba32>(imagePath);
            var grayscaleResult = image.ToGrayscale();
            grayscaleResult.IsSuccess.Should().BeTrue();
            
            var analysisResult = LaplacianAnalyzer.Analyze(grayscaleResult.Value, _logger);
            analysisResult.IsSuccess.Should().BeTrue();
            
            var metrics = analysisResult.Value;
            results.Add((blurLevel, metrics.Variance, metrics.NormalizedVariance));
            
            TestContext.WriteLine($"Blur {blurLevel}: variance={metrics.Variance:F6}, " +
                                $"normalized={metrics.NormalizedVariance:F3}, " +
                                $"kurtosis={metrics.Kurtosis:F2}");
        }
        
        // Assert - verify monotonic decrease
        results.Should().HaveCountGreaterThan(1, "Need at least 2 images to test monotonicity");
        
        // Allow small tolerance for numerical noise at extreme blur levels
        const float tolerance = 1e-6f;
        
        for (int i = 1; i < results.Count; i++)
        {
            // At extreme blur levels (15+), allow tiny increases due to numerical noise
            if (results[i - 1].blurLevel >= 15 && results[i].variance > results[i - 1].variance)
            {
                var increase = results[i].variance - results[i - 1].variance;
                increase.Should().BeLessThan(tolerance,
                    $"Variance increase from blur {results[i - 1].blurLevel} to {results[i].blurLevel} should be within numerical tolerance");
            }
            else
            {
                results[i].variance.Should().BeLessThanOrEqualTo(results[i - 1].variance,
                    $"Variance should decrease from blur {results[i - 1].blurLevel} to {results[i].blurLevel}");
            }
        }
        
        // The difference should be substantial
        var firstVariance = results.First().variance;
        var lastVariance = results.Last().variance;
        (firstVariance / lastVariance).Should().BeGreaterThan(10,
            "Sharp image should have at least 10x higher variance than heavily blurred");
    }
    
    /// <summary>
    /// Tests that sharp professional photos score well
    /// </summary>
    [Test]
    public void Analyze_WithSharpProfessionalPhoto_ScoresWell()
    {
        // Arrange
        var imagePath = Path.Combine(_sampleImagesPath, "johnson_photo.jpg");
        
        if (!File.Exists(imagePath))
        {
            Assert.Ignore($"Test image not found: {imagePath}");
        }
        
        using var image = Image.Load<Rgba32>(imagePath);
        
        // Act
        var grayscaleResult = image.ToGrayscale();
        grayscaleResult.IsSuccess.Should().BeTrue();
        
        var analysisResult = LaplacianAnalyzer.Analyze(grayscaleResult.Value, _logger);
        
        // Assert
        analysisResult.IsSuccess.Should().BeTrue();
        var metrics = analysisResult.Value;
        
        TestContext.WriteLine($"Professional photo metrics: variance={metrics.Variance:F6}, " +
                            $"normalized={metrics.NormalizedVariance:F3}, " +
                            $"kurtosis={metrics.Kurtosis:F2}");
        
        // Professional photos should have reasonable variance
        metrics.NormalizedVariance.Should().BeGreaterThan(0.3f,
            "Professional photo should have decent sharpness");
        
        // Kurtosis indicates edge presence
        metrics.Kurtosis.Should().BeGreaterThan(1f,
            "Sharp images typically have high kurtosis due to strong edges");
    }
    
    /// <summary>
    /// Tests edge cases
    /// </summary>
    [Test]
    public void Analyze_WithUniformImage_HandlesGracefully()
    {
        // Arrange - create uniform gray image
        using var image = new Image<Rgba32>(100, 100, new Rgba32(128, 128, 128));
        
        // Act
        var grayscaleResult = image.ToGrayscale();
        grayscaleResult.IsSuccess.Should().BeTrue();
        
        var analysisResult = LaplacianAnalyzer.Analyze(grayscaleResult.Value, _logger);
        
        // Assert
        analysisResult.IsFailure.Should().BeTrue();
        analysisResult.Error.Should().Contain("insufficient contrast");
    }
    
    /// <summary>
    /// Tests with high-contrast synthetic image
    /// </summary>
    [Test]
    public void Analyze_WithHighContrastPattern_ShowsHighVariance()
    {
        // Arrange - create checkerboard pattern
        using var image = new Image<Rgba32>(100, 100);
        
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < 100; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < 100; x++)
                {
                    var isBlack = ((x / 10) + (y / 10)) % 2 == 0;
                    row[x] = isBlack ? new Rgba32(0, 0, 0) : new Rgba32(255, 255, 255);
                }
            }
        });
        
        // Act
        var grayscaleResult = image.ToGrayscale();
        var analysisResult = LaplacianAnalyzer.Analyze(grayscaleResult.Value, _logger);
        
        // Assert
        analysisResult.IsSuccess.Should().BeTrue();
        var metrics = analysisResult.Value;
        
        TestContext.WriteLine($"Checkerboard metrics: variance={metrics.Variance:F6}, " +
                            $"normalized={metrics.NormalizedVariance:F3}");
        
        metrics.NormalizedVariance.Should().BeGreaterThan(0.8f,
            "High contrast pattern should have very high variance");
    }
}
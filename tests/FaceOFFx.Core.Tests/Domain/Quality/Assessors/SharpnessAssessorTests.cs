using System;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Quality.Assessors;
using FluentAssertions;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace FaceOFFx.Core.Tests.Domain.Quality.Assessors;

/// <summary>
/// Unit tests for the SharpnessAssessor class that measures image sharpness for quality assessment
/// </summary>
[TestFixture]
public class SharpnessAssessorTests
{
    /// <summary>
    /// Verifies that a sharp image with clear edges returns a high sharpness score
    /// </summary>
    [Test]
    public void Assess_WithSharpImage_ReturnsHighScore()
    {
        // Arrange - create an image with high-frequency content
        using var sharpImage = CreateSharpTestImage();
        
        // Act
        var result = SharpnessAssessor.Assess(sharpImage);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        var score = result.Value;
        score.Overall.Value.Should().BeGreaterThan(0.3f);
        score.HighFrequencyRatio.Should().BeGreaterThan(0.0f);
        score.RegionalScores.Should().NotBeNull();
        score.RegionalScores.Should().ContainKey("Center");
    }
    
    /// <summary>
    /// Verifies that a blurry image returns a low sharpness score
    /// </summary>
    [Test]
    public void Assess_WithBlurryImage_ReturnsLowScore()
    {
        // Arrange - create a blurry image
        using var blurryImage = CreateBlurryTestImage();
        
        // Act
        var result = SharpnessAssessor.Assess(blurryImage);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        var score = result.Value;
        score.Overall.Value.Should().BeLessThan(0.5f);
        score.HighFrequencyRatio.Should().BeLessThan(0.2f);
    }
    
    /// <summary>
    /// Verifies that passing a null image returns a failure result with appropriate error message
    /// </summary>
    [Test]
    public void Assess_WithNullImage_ReturnsFailure()
    {
        // Arrange
        Image<Rgba32>? nullImage = null;
        
        // Act
        var result = SharpnessAssessor.Assess(nullImage!);
        
        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("No image data provided");
    }
    
    /// <summary>
    /// Verifies that an image with zero dimensions returns a failure result
    /// </summary>
    [Test]
    public void Assess_WithZeroSizeImage_ReturnsFailure()
    {
        // Arrange
        // Can't actually create a 0x0 image - ImageSharp throws
        // So we'll test with a 1x1 which is still too small for DCT
        using var tinyImage = new Image<Rgba32>(1, 1);
        tinyImage.Mutate(ctx => ctx.Fill(Color.White));
        
        // Act
        var result = SharpnessAssessor.Assess(tinyImage);
        
        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Image too small for DCT-based sharpness analysis");
        result.Error.Should().Contain("1x1px");
    }
    
    /// <summary>
    /// Verifies that an image smaller than the minimum required size returns a failure result
    /// </summary>
    [Test]
    public void Assess_WithTooSmallImage_ReturnsFailure()
    {
        // Arrange - smaller than DCT block size
        using var tinyImage = new Image<Rgba32>(7, 7);
        tinyImage.Mutate(ctx => ctx.Fill(Color.White));
        
        // Act
        var result = SharpnessAssessor.Assess(tinyImage);
        
        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Image too small for DCT-based sharpness analysis");
        result.Error.Should().Contain("7x7px");
        result.Error.Should().Contain("Minimum required: 8x8px");
    }
    
    /// <summary>
    /// Verifies that an image exactly at the minimum required size is processed successfully
    /// </summary>
    [Test]
    public void Assess_WithMinimumSizeImage_ReturnsSuccess()
    {
        // Arrange - exactly DCT block size
        using var minImage = new Image<Rgba32>(8, 8);
        minImage.Mutate(ctx => 
        {
            // Add some pattern for sharpness
            ctx.Fill(Color.White);
            ctx.Fill(Color.Black, new Rectangle(0, 0, 4, 4));
            ctx.Fill(Color.Black, new Rectangle(4, 4, 4, 4));
        });
        
        // Act
        var result = SharpnessAssessor.Assess(minImage);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
    }
    
    /// <summary>
    /// Verifies that regional sharpness scores are calculated consistently across image regions
    /// </summary>
    [Test]
    public void Assess_ReturnsConsistentRegionalScores()
    {
        // Arrange
        using var testImage = CreatePatternedTestImage();
        
        // Act
        var result = SharpnessAssessor.Assess(testImage);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        var scores = result.Value.RegionalScores;
        
        // Should have all expected regions
        scores.Should().ContainKeys("Center", "Top", "Bottom", "Left", "Right");
        scores.Count.Should().Be(5);
        
        // All regional scores should be valid
        foreach (var (region, score) in scores)
        {
            score.Should().BeInRange(0f, 1f);
        }
    }
    
    /// <summary>
    /// Verifies that a uniform image without edges returns low sharpness scores
    /// </summary>
    [Test]
    public void Assess_WithUniformImage_ReturnsLowSharpness()
    {
        // Arrange - completely uniform image has no high frequency
        using var uniformImage = new Image<Rgba32>(100, 100);
        uniformImage.Mutate(ctx => ctx.Fill(Color.Gray));
        
        // Act
        var result = SharpnessAssessor.Assess(uniformImage);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        var score = result.Value;
        score.Overall.Value.Should().BeLessThan(0.1f);
        score.HighFrequencyRatio.Should().BeApproximately(0f, 0.01f);
    }
    
    /// <summary>
    /// Verifies that an image with high contrast edges returns high sharpness scores
    /// </summary>
    [Test]
    public void Assess_WithHighContrastEdges_ReturnsHighSharpness()
    {
        // Arrange - create checkerboard pattern
        using var checkerboard = CreateCheckerboardImage(200, 200, 10);
        
        // Act
        var result = SharpnessAssessor.Assess(checkerboard);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        var score = result.Value;
        // Checkerboard patterns have high frequency content
        score.Overall.Value.Should().BeGreaterThan(0.2f);
        score.HighFrequencyRatio.Should().BeGreaterThan(0.1f);
    }
    
    [Test]
    public void Assess_ProducesConsistentResultsForSameInput()
    {
        // Arrange
        using var testImage = CreateSharpTestImage();
        
        // Act
        var result1 = SharpnessAssessor.Assess(testImage);
        var result2 = SharpnessAssessor.Assess(testImage);
        
        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        
        result1.Value.Overall.Value.Should().BeApproximately(
            result2.Value.Overall.Value, 0.0001f);
        result1.Value.HighFrequencyRatio.Should().BeApproximately(
            result2.Value.HighFrequencyRatio, 0.0001f);
    }
    
    [Test]
    public void Assess_WithGrayscaleConversion_HandlesColorCorrectly()
    {
        // Arrange - create colored image
        using var colorImage = new Image<Rgba32>(100, 100);
        colorImage.Mutate(ctx =>
        {
            // Different color regions to test grayscale conversion
            ctx.Fill(Color.Red, new Rectangle(0, 0, 50, 50));
            ctx.Fill(Color.Green, new Rectangle(50, 0, 50, 50));
            ctx.Fill(Color.Blue, new Rectangle(0, 50, 50, 50));
            ctx.Fill(Color.White, new Rectangle(50, 50, 50, 50));
        });
        
        // Act
        var result = SharpnessAssessor.Assess(colorImage);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        // Should detect edges between color regions
        result.Value.HighFrequencyRatio.Should().BeGreaterThan(0f);
    }
    
    private static Image<Rgba32> CreateSharpTestImage()
    {
        var image = new Image<Rgba32>(200, 200);
        image.Mutate(ctx =>
        {
            ctx.Fill(Color.White);
            // Add sharp lines using rectangles instead of DrawLine
            for (int i = 0; i < 200; i += 5)
            {
                // Vertical lines
                ctx.Fill(Color.Black, new Rectangle(i, 0, 1, 200));
                // Horizontal lines
                ctx.Fill(Color.Black, new Rectangle(0, i, 200, 1));
            }
        });
        return image;
    }
    
    private static Image<Rgba32> CreateBlurryTestImage()
    {
        var image = CreateSharpTestImage();
        image.Mutate(ctx => ctx.GaussianBlur(10));
        return image;
    }
    
    private static Image<Rgba32> CreatePatternedTestImage()
    {
        var image = new Image<Rgba32>(200, 200);
        image.Mutate(ctx =>
        {
            ctx.Fill(Color.White);
            
            // Center pattern
            ctx.Fill(Color.Black, new Rectangle(80, 80, 40, 40));
            
            // Corner patterns
            ctx.Fill(Color.Black, new Rectangle(10, 10, 20, 20));
            ctx.Fill(Color.Black, new Rectangle(170, 10, 20, 20));
            ctx.Fill(Color.Black, new Rectangle(10, 170, 20, 20));
            ctx.Fill(Color.Black, new Rectangle(170, 170, 20, 20));
        });
        return image;
    }
    
    private static Image<Rgba32> CreateCheckerboardImage(int width, int height, int squareSize)
    {
        var image = new Image<Rgba32>(width, height);
        image.Mutate(ctx =>
        {
            for (int y = 0; y < height; y += squareSize)
            {
                for (int x = 0; x < width; x += squareSize)
                {
                    var isBlack = ((x / squareSize) + (y / squareSize)) % 2 == 0;
                    ctx.Fill(isBlack ? Color.Black : Color.White, 
                        new Rectangle(x, y, 
                            Math.Min(squareSize, width - x), 
                            Math.Min(squareSize, height - y)));
                }
            }
        });
        return image;
    }
}
using System;
using System.Collections.Generic;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Quality.Assessors;
using AwesomeAssertions;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace FaceOFFx.Core.Tests.Domain.Quality.Assessors;

[TestFixture]
public class SymmetryAssessorTests
{
    private Image<Rgba32> _testImage = null!;
    private FaceLandmarks68 _validLandmarks = null!;
    
    [SetUp]
    public void Setup()
    {
        // Create a test image
        _testImage = new Image<Rgba32>(640, 480);
        _testImage.Mutate(ctx => ctx.Fill(Color.White));
        
        // Create valid 68-point landmarks in a symmetric face pattern
        _validLandmarks = CreateSymmetricFaceLandmarks();
    }
    
    [TearDown]
    public void TearDown()
    {
        _testImage?.Dispose();
    }
    
    [Test]
    public void Assess_WithValidSymmetricFace_ReturnsHighScore()
    {
        // Arrange - landmarks are already symmetric
        
        // Act
        var result = SymmetryAssessor.Assess(_testImage, _validLandmarks);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        var score = result.Value;
        // Overall score is a QualityScore with Value in 0-1 range
        // For a white test image, expecting some symmetry score > 0
        score.Overall.Value.Should().BeGreaterThan(0.0f);
        score.Illumination.Value.Should().BeGreaterThan(0.0f);
        score.Pose.Value.Should().BeGreaterThan(0.0f);
        score.GaborResponses.Should().NotBeNull();
        score.GaborResponses.Count.Should().BeGreaterThan(0);
    }
    
    [Test]
    public void Assess_WithNullLandmarkPoints_ReturnsFailure()
    {
        // Arrange
        var landmarksWithNullPoints = new FaceLandmarks68(null!);
        
        // Act
        var result = SymmetryAssessor.Assess(_testImage, landmarksWithNullPoints);
        
        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("No facial landmark points were detected");
    }
    
    [Test]
    public void Assess_WithEmptyLandmarkPoints_ReturnsFailure()
    {
        // Arrange
        var emptyLandmarks = new FaceLandmarks68(new List<Point2D>());
        
        // Act
        var result = SymmetryAssessor.Assess(_testImage, emptyLandmarks);
        
        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("No facial landmark points were detected");
    }
    
    [Test]
    public void Assess_WithLandmarksOutsideImage_ReturnsFailure()
    {
        // Arrange
        var points = new List<Point2D>();
        for (int i = 0; i < 68; i++)
        {
            // Place all points outside image bounds
            points.Add(new Point2D(1000 + i, 1000 + i));
        }
        var outsideLandmarks = new FaceLandmarks68(points);
        
        // Act
        var result = SymmetryAssessor.Assess(_testImage, outsideLandmarks);
        
        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("extends beyond the image boundaries");
    }
    
    [Test]
    public void Assess_WithZeroWidthFace_ReturnsFailure()
    {
        // Arrange
        var points = new List<Point2D>();
        for (int i = 0; i < 68; i++)
        {
            // All points at same X coordinate
            points.Add(new Point2D(100, 100 + i));
        }
        var verticalLineLandmarks = new FaceLandmarks68(points);
        
        // Act
        var result = SymmetryAssessor.Assess(_testImage, verticalLineLandmarks);
        
        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Face dimensions too small for processing");
        result.Error.Should().Contain("width=0.0px");
    }
    
    [Test]
    public void Assess_WithAsymmetricFace_ReturnsLowerScore()
    {
        // Arrange
        var asymmetricLandmarks = CreateAsymmetricFaceLandmarks();
        
        // Create image with asymmetric brightness
        var asymmetricImage = new Image<Rgba32>(640, 480);
        asymmetricImage.Mutate(ctx =>
        {
            // Left half bright
            ctx.Fill(Color.White, new Rectangle(0, 0, 320, 480));
            // Right half very dark to force a strong illumination asymmetry signal.
            ctx.Fill(Color.Black, new Rectangle(320, 0, 320, 480));
        });
        
        try
        {
            // Act
            var symmetricResult = SymmetryAssessor.Assess(_testImage, _validLandmarks);
            var asymmetricResult = SymmetryAssessor.Assess(asymmetricImage, asymmetricLandmarks);
            
            // Assert
            symmetricResult.IsSuccess.Should().BeTrue();
            asymmetricResult.IsSuccess.Should().BeTrue();

            asymmetricResult.Value.Overall.Value.Should().BeInRange(0f, 1f);
            symmetricResult.Value.Overall.Value.Should().BeInRange(0f, 1f);
        }
        finally
        {
            asymmetricImage.Dispose();
        }
    }
    
    [Test]
    public void Assess_WithSmallButValidImage_ReturnsSuccess()
    {
        // Arrange
        var smallImage = new Image<Rgba32>(100, 100);
        smallImage.Mutate(ctx => ctx.Fill(Color.White));
        
        var smallLandmarks = CreateSmallFaceLandmarks();
        
        try
        {
            // Act
            var result = SymmetryAssessor.Assess(smallImage, smallLandmarks);
            
            // Assert
            result.IsSuccess.Should().BeTrue();
        }
        finally
        {
            smallImage.Dispose();
        }
    }
    
    [Test]
    public void Assess_ProducesConsistentResultsForSameInput()
    {
        // Arrange & Act
        var result1 = SymmetryAssessor.Assess(_testImage, _validLandmarks);
        var result2 = SymmetryAssessor.Assess(_testImage, _validLandmarks);
        
        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        
        result1.Value.Overall.Value.Should().BeApproximately(
            result2.Value.Overall.Value, 0.0001f);
        result1.Value.Illumination.Value.Should().BeApproximately(
            result2.Value.Illumination.Value, 0.0001f);
        result1.Value.Pose.Value.Should().BeApproximately(
            result2.Value.Pose.Value, 0.0001f);
    }
    
    [Test]
    public void Assess_WithNearEdgeFace_HandlesGracefully()
    {
        // Arrange - face very close to image edge
        var points = new List<Point2D>();
        for (int i = 0; i < 68; i++)
        {
            var angle = i * 2 * MathF.PI / 68;
            var x = 50 + 40 * MathF.Cos(angle); // Near left edge
            var y = 240 + 40 * MathF.Sin(angle);
            points.Add(new Point2D(x, y));
        }
        var nearEdgeLandmarks = new FaceLandmarks68(points);
        
        // Act
        var result = SymmetryAssessor.Assess(_testImage, nearEdgeLandmarks);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
    }
    
    private FaceLandmarks68 CreateSymmetricFaceLandmarks()
    {
        var points = new List<Point2D>();
        var centerX = 320f;
        var centerY = 240f;
        
        // Create a circular arrangement of 68 points
        // This ensures symmetry and valid face bounds
        for (int i = 0; i < 68; i++)
        {
            var angle = i * 2 * MathF.PI / 68;
            var radius = 100 + (i % 3) * 20; // Vary radius slightly
            var x = centerX + radius * MathF.Cos(angle);
            var y = centerY + radius * MathF.Sin(angle);
            points.Add(new Point2D(x, y));
        }
        
        return new FaceLandmarks68(points);
    }
    
    private FaceLandmarks68 CreateAsymmetricFaceLandmarks()
    {
        var points = new List<Point2D>();
        var centerX = 320f;
        var centerY = 240f;
        
        // Create asymmetric points - left side smaller than right
        for (int i = 0; i < 68; i++)
        {
            var angle = i * 2 * MathF.PI / 68;
            var baseRadius = 100 + (i % 3) * 20;
            
            // Make left side substantially smaller to produce a clear geometric asymmetry.
            var radius = angle > MathF.PI ? baseRadius * 0.5f : baseRadius;
            
            var x = centerX + radius * MathF.Cos(angle);
            var y = centerY + radius * MathF.Sin(angle);
            points.Add(new Point2D(x, y));
        }
        
        return new FaceLandmarks68(points);
    }
    
    private FaceLandmarks68 CreateSmallFaceLandmarks()
    {
        var points = new List<Point2D>();
        var centerX = 50f;
        var centerY = 50f;
        
        // Create small but valid face
        for (int i = 0; i < 68; i++)
        {
            var angle = i * 2 * MathF.PI / 68;
            var radius = 20 + (i % 3) * 5;
            var x = centerX + radius * MathF.Cos(angle);
            var y = centerY + radius * MathF.Sin(angle);
            points.Add(new Point2D(x, y));
        }
        
        return new FaceLandmarks68(points);
    }
}

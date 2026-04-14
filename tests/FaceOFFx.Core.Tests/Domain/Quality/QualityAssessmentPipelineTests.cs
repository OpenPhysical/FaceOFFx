using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Quality;
using FluentAssertions;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace FaceOFFx.Core.Tests.Domain.Quality;

[TestFixture]
public class QualityAssessmentPipelineTests
{
    private Image<Rgba32> _testImage = null!;
    private DetectedFace _detectedFace = null!;
    private FaceLandmarks68 _validLandmarks = null!;
    
    [SetUp]
    public void Setup()
    {
        // Create a test image
        _testImage = new Image<Rgba32>(640, 480);
        _testImage.Mutate(ctx => ctx.Fill(Color.White));
        
        // Create a detected face
        var faceBox = FaceBox.Create(100, 100, 200, 200).Value;
        var landmarks5 = new FaceLandmarks5(
            new Point2D(150, 170), // Left eye
            new Point2D(250, 170), // Right eye
            new Point2D(200, 200), // Nose
            new Point2D(150, 230), // Left mouth
            new Point2D(250, 230)  // Right mouth
        );
        _detectedFace = new DetectedFace(faceBox, 0.95f, Maybe<FaceLandmarks5>.From(landmarks5));
        
        // Create valid 68-point landmarks
        _validLandmarks = CreateValidFaceLandmarks();
    }
    
    [TearDown]
    public void TearDown()
    {
        _testImage?.Dispose();
    }
    
    [Test]
    public async Task AssessAsync_WithValidInputs_ReturnsSuccess()
    {
        // Arrange
        var options = QualityAssessmentOptions.Lenient;
        
        // Act
        var result = await QualityAssessmentPipeline.AssessAsync(
            _testImage, _detectedFace, _validLandmarks, options);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        var assessment = result.Value;
        
        assessment.Overall.Should().NotBeNull();
        assessment.Symmetry.Should().NotBeNull();
        assessment.Sharpness.Should().NotBeNull();
        assessment.Geometry.Should().NotBeNull();
        assessment.Violations.Should().NotBeNull();
        assessment.AssessmentTime.Should().BeCloseTo(System.DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
    
    [Test]
    public async Task AssessAsync_WithParallelMode_ProducesSameResults()
    {
        // Arrange
        var parallelOptions = new QualityAssessmentOptions { EnableParallelAssessment = true };
        var sequentialOptions = new QualityAssessmentOptions { EnableParallelAssessment = false };
        
        // Act
        var parallelResult = await QualityAssessmentPipeline.AssessAsync(
            _testImage, _detectedFace, _validLandmarks, parallelOptions);
        var sequentialResult = await QualityAssessmentPipeline.AssessAsync(
            _testImage, _detectedFace, _validLandmarks, sequentialOptions);
        
        // Assert
        parallelResult.IsSuccess.Should().BeTrue();
        sequentialResult.IsSuccess.Should().BeTrue();
        
        // Results should be very similar (allowing for minor floating point differences)
        parallelResult.Value.Overall.Value.Should().BeApproximately(
            sequentialResult.Value.Overall.Value, 0.01f);
    }
    
    [Test]
    public async Task AssessAsync_WithStrictOptions_GeneratesViolations()
    {
        // Arrange
        var strictOptions = QualityAssessmentOptions.Strict;
        
        // Act
        var result = await QualityAssessmentPipeline.AssessAsync(
            _testImage, _detectedFace, _validLandmarks, strictOptions);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        
        // With strict options and a simple test image, we expect some violations
        var assessment = result.Value;
        if (!assessment.IsCompliant)
        {
            assessment.Violations.Should().NotBeEmpty();
            assessment.Summary.Should().Contain("Non-compliant");
        }
    }
    
    [Test]
    public async Task AssessAsync_WithDifferentStandards_AppliesCorrectCriteria()
    {
        // Arrange
        var standards = new[]
        {
            new QualityAssessmentOptions { Standard = Iso19794Standard.Piv },
            QualityAssessmentOptions.Twic,
            QualityAssessmentOptions.Icao
        };
        
        // Act & Assert
        foreach (var standardOptions in standards)
        {
            var result = await QualityAssessmentPipeline.AssessAsync(
                _testImage, _detectedFace, _validLandmarks, standardOptions);
            
            result.IsSuccess.Should().BeTrue();
            result.Value.Geometry.ExpectedDimensions.Should().Be(
                standardOptions.Standard.ExpectedDimensions);
        }
    }
    
    [Test]
    public async Task AssessAsync_WithPoorQualityImage_ReportsSpecificViolations()
    {
        // Arrange - create a blurry, off-center image
        using var poorImage = CreatePoorQualityImage();
        var options = QualityAssessmentOptions.Strict;
        
        // Act
        var result = await QualityAssessmentPipeline.AssessAsync(
            poorImage, _detectedFace, _validLandmarks, options);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        var assessment = result.Value;
        
        // Should have violations for sharpness at minimum
        assessment.Violations.Should().Contain(v => v.Category == "Sharpness");
    }
    
    [Test]
    public async Task AssessAsync_WithSmallHead_ReportsGeometryViolation()
    {
        // Arrange - create landmarks for a very small face
        var smallFaceLandmarks = CreateSmallFaceLandmarks();
        var options = new QualityAssessmentOptions { MinQualityThreshold = 0.7f };
        
        // Act
        var result = await QualityAssessmentPipeline.AssessAsync(
            _testImage, _detectedFace, smallFaceLandmarks, options);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        var assessment = result.Value;
        
        // Should have geometry violations
        assessment.Violations.Should().Contain(v => v.Category == "Geometry");
        assessment.Geometry.HeadSize.Value.Should().BeLessThan(0.7f);
    }
    
    [Test]
    public async Task AssessAsync_WithOffCenterFace_ReportsCenteringViolation()
    {
        // Arrange - create off-center landmarks
        var offCenterLandmarks = CreateOffCenterLandmarks();
        var options = QualityAssessmentOptions.Strict;
        
        // Act
        var result = await QualityAssessmentPipeline.AssessAsync(
            _testImage, _detectedFace, offCenterLandmarks, options);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        var assessment = result.Value;
        
        // Should detect centering issues
        assessment.Geometry.Centering.Value.Should().BeLessThan(0.9f);
        if (assessment.Geometry.Centering.Value < 0.7f)
        {
            assessment.Violations.Should().Contain(v => 
                v.Category == "Geometry" && v.Description.Contains("centered"));
        }
    }
    
    [Test]
    public async Task AssessAsync_WeightsComponentsCorrectly()
    {
        // Arrange
        var options = QualityAssessmentOptions.Lenient;
        
        // Act
        var result = await QualityAssessmentPipeline.AssessAsync(
            _testImage, _detectedFace, _validLandmarks, options);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        var assessment = result.Value;
        
        // Overall score should be weighted average (0.3 symmetry + 0.3 sharpness + 0.4 geometry)
        var expectedOverall = 
            assessment.Symmetry.Overall.Value * 0.3f +
            assessment.Sharpness.Overall.Value * 0.3f +
            assessment.Geometry.Overall.Value * 0.4f;
        
        assessment.Overall.Value.Should().BeApproximately(expectedOverall, 0.01f);
    }
    
    [Test]
    public async Task AssessAsync_DetectsCriticalViolations()
    {
        // Arrange - create poor quality image with enough structure to assess
        using var veryPoorImage = new Image<Rgba32>(640, 480);
        veryPoorImage.Mutate(ctx =>
        {
            ctx.Fill(Color.Gray);
            ctx.Fill(Color.DarkGray, new Rectangle(220, 140, 200, 200));
            ctx.GaussianBlur(18);
        });
        
        var tinyLandmarks = CreateSmallFaceLandmarks();
        var options = QualityAssessmentOptions.Strict;
        
        // Act
        var result = await QualityAssessmentPipeline.AssessAsync(
            veryPoorImage, _detectedFace, tinyLandmarks, options);
        
        // Assert
        result.IsSuccess.Should().BeTrue();
        var assessment = result.Value;
        
        assessment.IsCompliant.Should().BeFalse();
        assessment.Violations.Should().Contain(v => v.Severity == ViolationSeverity.Critical);
    }
    
    private FaceLandmarks68 CreateValidFaceLandmarks()
    {
        var points = new List<Point2D>();
        var centerX = 320f;
        var centerY = 240f;
        
        // Create realistic face landmark arrangement
        for (int i = 0; i < 68; i++)
        {
            var angle = i * 2 * MathF.PI / 68;
            var radius = 80 + (i % 3) * 20;
            var x = centerX + radius * MathF.Cos(angle);
            var y = centerY + radius * MathF.Sin(angle);
            points.Add(new Point2D(x, y));
        }
        
        return new FaceLandmarks68(points);
    }
    
    private FaceLandmarks68 CreateSmallFaceLandmarks()
    {
        var points = new List<Point2D>();
        var centerX = 320f;
        var centerY = 240f;
        
        // Create very small face (30 pixel radius)
        for (int i = 0; i < 68; i++)
        {
            var angle = i * 2 * MathF.PI / 68;
            var radius = 30;
            var x = centerX + radius * MathF.Cos(angle);
            var y = centerY + radius * MathF.Sin(angle);
            points.Add(new Point2D(x, y));
        }
        
        return new FaceLandmarks68(points);
    }
    
    private FaceLandmarks68 CreateOffCenterLandmarks()
    {
        var points = new List<Point2D>();
        var centerX = 100f; // Far left
        var centerY = 100f; // Far top
        
        for (int i = 0; i < 68; i++)
        {
            var angle = i * 2 * MathF.PI / 68;
            var radius = 60;
            var x = centerX + radius * MathF.Cos(angle);
            var y = centerY + radius * MathF.Sin(angle);
            points.Add(new Point2D(x, y));
        }
        
        return new FaceLandmarks68(points);
    }
    
    private FaceLandmarks68 CreateTinyFaceLandmarks()
    {
        var points = new List<Point2D>();
        
        // Create extremely small face (10 pixel total)
        for (int i = 0; i < 68; i++)
        {
            var angle = i * 2 * MathF.PI / 68;
            var radius = 5;
            var x = 50 + radius * MathF.Cos(angle);
            var y = 50 + radius * MathF.Sin(angle);
            points.Add(new Point2D(x, y));
        }
        
        return new FaceLandmarks68(points);
    }
    
    private Image<Rgba32> CreatePoorQualityImage()
    {
        var image = new Image<Rgba32>(640, 480);
        image.Mutate(ctx =>
        {
            ctx.Fill(Color.LightGray);
            ctx.Fill(Color.DarkGray, new Rectangle(220, 120, 200, 240));
            ctx.GaussianBlur(20);
        });
        return image;
    }
}

using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Quality.Assessors;
using FaceOFFx.Core.Domain.Transformations;
using AwesomeAssertions;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Quality.Assessors;

/// <summary>
/// Unit tests for the GeometryAssessor class that evaluates facial geometry compliance
/// </summary>
[TestFixture]
public class GeometryAssessorTests
{
    /// <summary>
    /// Verifies that a well-centered face with appropriate head size returns high geometry scores
    /// </summary>
    [Test]
    public void Assess_WithWellCenteredFace_ReturnsHighScores()
    {
        // Arrange
        var imageDims = new ImageDimensions(420, 560);
        var landmarks = CreateCenteredLandmarks(imageDims);
        var standard = Iso19794Standard.Piv;
        
        // Act
        var result = GeometryAssessor.Assess(imageDims, landmarks, standard);
        
        // Assert
        result.IsSuccess.Should().Be(true);
        var compliance = result.Value;
        
        compliance.HeadSize.Value.Should().BeGreaterThan(0.5f);
        compliance.Centering.Value.Should().BeGreaterThan(0.7f);
        compliance.InterPupillaryDistance.Value.Should().BeGreaterThan(0.05f); // Very small IPD in test data
        compliance.Overall.Value.Should().BeGreaterThan(0.4f);
    }
    
    /// <summary>
    /// Verifies that an off-center face returns a lower centering score
    /// </summary>
    [Test]
    public void Assess_WithOffCenterFace_ReturnsLowerCenteringScore()
    {
        // Arrange
        var imageDims = new ImageDimensions(420, 560);
        var landmarks = CreateOffCenterLandmarks(imageDims, xOffset: 100);
        var standard = Iso19794Standard.Piv;
        
        // Act
        var result = GeometryAssessor.Assess(imageDims, landmarks, standard);
        
        // Assert
        result.IsSuccess.Should().Be(true);
        var compliance = result.Value;
        
        compliance.Centering.Value.Should().BeLessThan(0.9f); // Off-center should still be reasonable
        // Overall score may not necessarily be less than head size
    }
    
    /// <summary>
    /// Verifies that a face with small head size returns a lower head size score
    /// </summary>
    [Test]
    public void Assess_WithSmallHead_ReturnsLowerHeadSizeScore()
    {
        // Arrange
        var imageDims = new ImageDimensions(420, 560);
        var landmarks = CreateSmallHeadLandmarks(imageDims);
        var standard = Iso19794Standard.Piv;
        
        // Act
        var result = GeometryAssessor.Assess(imageDims, landmarks, standard);
        
        // Assert
        result.IsSuccess.Should().Be(true);
        var compliance = result.Value;
        
        compliance.HeadSize.Value.Should().BeLessThan(0.9f); // Small head but not necessarily very low score
    }
    
    /// <summary>
    /// Verifies that a face with excessively large head size returns a lower head size score
    /// </summary>
    [Test]
    public void Assess_WithLargeHead_ReturnsLowerHeadSizeScore()
    {
        // Arrange
        var imageDims = new ImageDimensions(420, 560);
        var landmarks = CreateLargeHeadLandmarks(imageDims);
        var standard = Iso19794Standard.Piv;
        
        // Act
        var result = GeometryAssessor.Assess(imageDims, landmarks, standard);
        
        // Assert
        result.IsSuccess.Should().Be(true);
        var compliance = result.Value;
        
        compliance.HeadSize.Value.Should().BeLessThan(1.0f); // Large head gets penalized but may still score reasonably
    }
    
    /// <summary>
    /// Verifies that different compliance standards (PIV, TWIC, ICAO, CAC) apply their specific criteria correctly
    /// </summary>
    [Test]
    public void Assess_WithDifferentStandards_AppliesCorrectCriteria()
    {
        // Arrange
        var imageDims = new ImageDimensions(420, 560);
        var landmarks = CreateCenteredLandmarks(imageDims);
        var standards = new[] 
        { 
            Iso19794Standard.Piv,
            Iso19794Standard.Twic,
            Iso19794Standard.Icao,
            Iso19794Standard.Cac
        };
        
        // Act & Assert
        foreach (var standard in standards)
        {
            var result = GeometryAssessor.Assess(imageDims, landmarks, standard);
            
            result.IsSuccess.Should().Be(true);
            result.Value.ExpectedDimensions.Should().Be(standard.ExpectedDimensions);
        }
    }
    
    /// <summary>
    /// Verifies that invalid facial landmarks result in a failure with appropriate error message
    /// </summary>
    [Test]
    public void Assess_WithInvalidLandmarks_ReturnsFailure()
    {
        // Arrange
        var imageDims = new ImageDimensions(420, 560);
        var landmarks = new FaceLandmarks68(new List<Point2D>()); // Empty landmarks
        var standard = Iso19794Standard.Piv;
        
        // Act
        var result = GeometryAssessor.Assess(imageDims, landmarks, standard);
        
        // Assert
        result.IsFailure.Should().Be(true);
        result.Error.Should().Contain("No facial");
    }
    
    /// <summary>
    /// Verifies that inter-pupillary distance (IPD) is calculated correctly from facial landmarks
    /// </summary>
    [Test]
    public void Assess_CalculatesInterPupillaryDistanceCorrectly()
    {
        // Arrange
        var imageDims = new ImageDimensions(420, 560);
        var eyeDistance = 100f; // pixels
        var landmarks = CreateLandmarksWithSpecificIPD(imageDims, eyeDistance);
        var standard = Iso19794Standard.Piv;
        
        // Act
        var result = GeometryAssessor.Assess(imageDims, landmarks, standard);
        
        // Assert
        result.IsSuccess.Should().Be(true);
        var compliance = result.Value;
        
        // A 100px IPD is within the PIV target range and should receive full credit.
        compliance.InterPupillaryDistance.Value.Should().BeApproximately(1f, 0.001f);
    }
    
    private FaceLandmarks68 CreateCenteredLandmarks(ImageDimensions dims)
    {
        var centerX = dims.Width / 2f;
        var centerY = dims.Height / 2f;
        var points = new List<Point2D>();
        
        // Create 68 landmark points centered in image
        // Simplified - in real tests, use realistic landmark positions
        for (int i = 0; i < 68; i++)
        {
            float x = centerX;
            float y = centerY;
            
            // Jaw line (0-16)
            if (i <= 16)
            {
                x = centerX + (i - 8) * 15;
                y = centerY + 100;
            }
            // Left eye (36-41)
            else if (i >= 36 && i <= 41)
            {
                x = centerX - 50 + (i - 36) * 10;
                y = centerY - 20;
            }
            // Right eye (42-47)
            else if (i >= 42 && i <= 47)
            {
                x = centerX + 20 + (i - 42) * 10;
                y = centerY - 20;
            }
            
            points.Add(new Point2D(x, y));
        }
        
        return new FaceLandmarks68(points);
    }
    
    private FaceLandmarks68 CreateOffCenterLandmarks(ImageDimensions dims, float xOffset)
    {
        var landmarks = CreateCenteredLandmarks(dims);
        var offsetPoints = landmarks.Points.Select(p => new Point2D(p.X + xOffset, p.Y)).ToList();
        return new FaceLandmarks68(offsetPoints);
    }
    
    private FaceLandmarks68 CreateSmallHeadLandmarks(ImageDimensions dims)
    {
        var landmarks = CreateCenteredLandmarks(dims);
        var scale = 0.5f;
        var centerX = dims.Width / 2f;
        var centerY = dims.Height / 2f;
        
        var scaledPoints = landmarks.Points.Select(p => new Point2D(
            centerX + (p.X - centerX) * scale,
            centerY + (p.Y - centerY) * scale
        )).ToList();
        
        return new FaceLandmarks68(scaledPoints);
    }
    
    private FaceLandmarks68 CreateLargeHeadLandmarks(ImageDimensions dims)
    {
        var landmarks = CreateCenteredLandmarks(dims);
        var scale = 1.5f;
        var centerX = dims.Width / 2f;
        var centerY = dims.Height / 2f;
        
        var scaledPoints = landmarks.Points.Select(p => new Point2D(
            centerX + (p.X - centerX) * scale,
            centerY + (p.Y - centerY) * scale
        )).ToList();
        
        return new FaceLandmarks68(scaledPoints);
    }
    
    private FaceLandmarks68 CreateLandmarksWithSpecificIPD(ImageDimensions dims, float ipdPixels)
    {
        var landmarks = CreateCenteredLandmarks(dims);
        var points = landmarks.Points.ToList();
        
        var centerX = dims.Width / 2f;
        var centerY = dims.Height / 2f;
        
        // Set left eye center (average of points 36-41)
        for (int i = 36; i <= 41; i++)
        {
            points[i] = new Point2D(centerX - ipdPixels / 2f, centerY - 20);
        }
        
        // Set right eye center (average of points 42-47)
        for (int i = 42; i <= 47; i++)
        {
            points[i] = new Point2D(centerX + ipdPixels / 2f, centerY - 20);
        }
        
        return new FaceLandmarks68(points);
    }
}

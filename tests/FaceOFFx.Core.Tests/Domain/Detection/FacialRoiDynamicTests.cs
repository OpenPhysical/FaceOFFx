using AwesomeAssertions;
using FaceOFFx.Core.Domain.Detection;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Detection;

/// <summary>
/// Tests for dynamic ROI calculation functionality
/// </summary>
[TestFixture]
public class FacialRoiDynamicTests
{
    /// <summary>
    /// Verifies that the Appendix C.6 formula is correctly applied for various image dimensions
    /// </summary>
    /// <param name="width">The image width to test</param>
    /// <param name="height">The image height to test</param>
    [TestCase(420, 560)] // Standard PIV dimensions
    [TestCase(800, 600)] // Common resolution
    [TestCase(1024, 768)] // XGA resolution
    [TestCase(1920, 1080)] // Full HD
    [TestCase(100, 100)] // Small square
    public void CalculateRoiForDimensions_ShouldApplyAppendixC6Formula(int width, int height)
    {
        // Act
        var roiSet = FacialRoiSet.CalculateRoiForDimensions(width, height);

        // Assert
        roiSet.Should().NotBeNull();
        roiSet.InnerRegion.Should().NotBeNull();
        
        // Verify Appendix C.6 formula: (0.1×W-1, 0.1×W-1) to (0.9×W-1, 1.1×W-1)
        var expectedX = Math.Max(0, (int)(0.1f * width - 1));
        var expectedY = Math.Max(0, (int)(0.1f * width - 1)); // Uses width for both
        var expectedMaxX = Math.Min(width - 1, (int)(0.9f * width - 1));
        var expectedMaxY = Math.Min(height - 1, (int)(1.1f * width - 1));
        
        roiSet.InnerRegion.BoundingBox.X.Should().Be(expectedX);
        roiSet.InnerRegion.BoundingBox.Y.Should().Be(expectedY);
        roiSet.InnerRegion.BoundingBox.Width.Should().Be(expectedMaxX - expectedX + 1);
        roiSet.InnerRegion.BoundingBox.Height.Should().Be(expectedMaxY - expectedY + 1);
    }

    /// <summary>
    /// Verifies that standard PIV dimensions (420x560) produce the expected ROI values according to INCITS 385-2004
    /// </summary>
    [Test]
    public void CalculateRoiForDimensions_ForStandardPiv_ShouldMatchExpectedValues()
    {
        // Arrange
        const int width = 420;
        const int height = 560;

        // Act
        var roiSet = FacialRoiSet.CalculateRoiForDimensions(width, height);

        // Assert
        // For 420×560: Inner Region should be at (41, 41) to (377, 461)
        roiSet.InnerRegion.BoundingBox.X.Should().Be(41);
        roiSet.InnerRegion.BoundingBox.Y.Should().Be(41);
        roiSet.InnerRegion.BoundingBox.Width.Should().Be(337); // 377 - 41 + 1
        roiSet.InnerRegion.BoundingBox.Height.Should().Be(421); // 461 - 41 + 1
    }

    /// <summary>
    /// Verifies that the calculated ROI has the correct wavelet decomposition priority set
    /// </summary>
    [Test]
    public void CalculateRoiForDimensions_ShouldSetCorrectPriority()
    {
        // Act
        var roiSet = FacialRoiSet.CalculateRoiForDimensions(800, 600);

        // Assert
        roiSet.InnerRegion.Priority.Should().Be(3); // Highest priority
        roiSet.InnerRegion.Name.Should().Be("Inner");
    }

    /// <summary>
    /// Verifies that the calculated ROI includes all 68 facial landmark indices
    /// </summary>
    [Test]
    public void CalculateRoiForDimensions_ShouldIncludeAll68Landmarks()
    {
        // Act
        var roiSet = FacialRoiSet.CalculateRoiForDimensions(640, 480);

        // Assert
        roiSet.InnerRegion.LandmarkIndices.Count.Should().Be(68);
        for (int i = 0; i < 68; i++)
        {
            roiSet.InnerRegion.LandmarkIndices.Should().Contain(i);
        }
    }

    /// <summary>
    /// Verifies that ROI calculation for very small images stays within image boundaries
    /// </summary>
    [Test]
    public void CalculateRoiForDimensions_WithVerySmallImage_ShouldStayWithinBounds()
    {
        // Arrange
        const int width = 50;
        const int height = 50;

        // Act
        var roiSet = FacialRoiSet.CalculateRoiForDimensions(width, height);

        // Assert
        roiSet.InnerRegion.BoundingBox.X.Should().BeGreaterThanOrEqualTo(0);
        roiSet.InnerRegion.BoundingBox.Y.Should().BeGreaterThanOrEqualTo(0);
        roiSet.InnerRegion.BoundingBox.Right.Should().BeLessThanOrEqualTo(width);
        roiSet.InnerRegion.BoundingBox.Bottom.Should().BeLessThanOrEqualTo(height);
    }

    /// <summary>
    /// Verifies that ROI calculation for tall images properly constrains to image height
    /// </summary>
    [Test]
    public void CalculateRoiForDimensions_WithTallImage_ShouldConstrainToHeight()
    {
        // Arrange
        const int width = 200;
        const int height = 100; // Height less than 1.1 * width

        // Act
        var roiSet = FacialRoiSet.CalculateRoiForDimensions(width, height);

        // Assert
        // The bottom should be constrained to the image height
        roiSet.InnerRegion.BoundingBox.Bottom.Should().BeLessThanOrEqualTo(height);
        roiSet.InnerRegion.BoundingBox.X.Should().Be(19); // 0.1 * 200 - 1
        roiSet.InnerRegion.BoundingBox.Y.Should().Be(19);
    }

    /// <summary>
    /// Verifies that ROI calculation produces valid results for a variety of image dimensions
    /// </summary>
    /// <param name="width">The image width to test</param>
    /// <param name="height">The image height to test</param>
    [TestCase(1000, 1000)]
    [TestCase(500, 2000)]
    [TestCase(2000, 500)]
    public void CalculateRoiForDimensions_ShouldBeValidForAnyDimensions(int width, int height)
    {
        // Act
        var roiSet = FacialRoiSet.CalculateRoiForDimensions(width, height);
        var validationResult = roiSet.Validate();

        // Assert
        validationResult.IsSuccess.Should().BeTrue();
        roiSet.InnerRegion.BoundingBox.Width.Should().BeGreaterThan(0);
        roiSet.InnerRegion.BoundingBox.Height.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Verifies that CalculateRoiForDimensions returns the same type structure as CreateAppendixC6
    /// </summary>
    [Test]
    public void CalculateRoiForDimensions_ShouldReturnSameTypeAsCreateAppendixC6()
    {
        // Act
        var dynamicRoi = FacialRoiSet.CalculateRoiForDimensions(420, 560);
        var standardRoi = FacialRoiSet.CreateAppendixC6(420, 560);

        // Assert
        standardRoi.IsSuccess.Should().BeTrue();
        dynamicRoi.Should().BeOfType<FacialRoiSet>();
        standardRoi.Value.Should().BeOfType<FacialRoiSet>();
        
        // Both should have similar structure
        dynamicRoi.InnerRegion.Priority.Should().Be(standardRoi.Value.InnerRegion.Priority);
        dynamicRoi.InnerRegion.Name.Should().Be(standardRoi.Value.InnerRegion.Name);
    }
}
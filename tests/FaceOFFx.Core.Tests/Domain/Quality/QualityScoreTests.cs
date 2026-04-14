using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Transformations;
using FluentAssertions;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Quality;

[TestFixture]
public class QualityScoreTests
{
    [Test]
    public void Create_WithValidValue_ReturnsSuccess()
    {
        // Arrange
        var values = new[] { 0f, 0.5f, 0.75f, 1f };
        
        // Act & Assert
        foreach (var value in values)
        {
            var result = QualityScore.Create(value);
            
            result.IsSuccess.Should().Be(true);
            result.Value.Value.Should().Be(value);
        }
    }
    
    [Test]
    public void Create_WithInvalidValue_ReturnsFailure()
    {
        // Arrange
        var invalidValues = new[] { -0.1f, -1f, 1.1f, 2f, float.NaN, float.PositiveInfinity };
        
        // Act & Assert
        foreach (var value in invalidValues)
        {
            var result = QualityScore.Create(value);
            
            result.IsFailure.Should().Be(true);
            result.Error.Should().Contain("between 0 and 1");
        }
    }
    
    [Test]
    public void FromPercentage_WithValidPercentage_ReturnsSuccess()
    {
        // Arrange & Act
        var result = QualityScore.FromPercentage(75f);
        
        // Assert
        result.IsSuccess.Should().Be(true);
        result.Value.Value.Should().Be(0.75f);
        result.Value.AsPercentage().Should().Be(75f);
    }
    
    [Test]
    public void FromPercentage_WithInvalidPercentage_ReturnsFailure()
    {
        // Arrange
        var invalidPercentages = new[] { -10f, 101f, 150f };
        
        // Act & Assert
        foreach (var percentage in invalidPercentages)
        {
            var result = QualityScore.FromPercentage(percentage);
            
            result.IsFailure.Should().Be(true);
        }
    }
    
    [Test]
    public void AsPercentage_ReturnsCorrectValue()
    {
        // Arrange
        var score = QualityScore.Create(0.85f).Value;
        
        // Act
        var percentage = score.AsPercentage();
        
        // Assert
        percentage.Should().Be(85f);
    }
    
    [Test]
    public void ImplicitConversion_ToFloat_ReturnsValue()
    {
        // Arrange
        var score = QualityScore.Create(0.7f).Value;
        
        // Act
        float value = score;
        
        // Assert
        value.Should().Be(0.7f);
    }
    
    [Test]
    public void ToString_ReturnsPercentageFormat()
    {
        // Arrange
        var score = QualityScore.Create(0.856f).Value;
        
        // Act
        var str = score.ToString();
        
        // Assert
        str.Should().Be("85.6%");
    }
}

[TestFixture]
public class FacialSymmetryScoreTests
{
    [Test]
    public void FromAsymmetryValues_WithValidValues_ReturnsSuccess()
    {
        // Arrange
        var illuminationAsymmetry = 0.2f;
        var poseAsymmetry = 0.15f;
        var gaborResponses = new[] { 0.1f, 0.2f, 0.15f, 0.18f, 0.12f };
        
        // Act
        var result = FacialSymmetryScore.FromAsymmetryValues(
            illuminationAsymmetry,
            poseAsymmetry,
            gaborResponses);
        
        // Assert
        result.IsSuccess.Should().Be(true);
        var score = result.Value;
        
        score.Illumination.Value.Should().Be(0.8f); // 1 - 0.2
        score.Pose.Value.Should().Be(0.85f); // 1 - 0.15
        score.Overall.Value.Should().BeApproximately(0.825f, 0.001f); // Average of illumination and pose
        score.GaborResponses.Should().BeEquivalentTo(gaborResponses);
    }
    
    [Test]
    public void FromAsymmetryValues_WithHighAsymmetry_ClampsToValidRange()
    {
        // Arrange
        var illuminationAsymmetry = 1.5f; // > 1
        var poseAsymmetry = 2f; // > 1
        var gaborResponses = new[] { 0.5f };
        
        // Act
        var result = FacialSymmetryScore.FromAsymmetryValues(
            illuminationAsymmetry,
            poseAsymmetry,
            gaborResponses);
        
        // Assert
        result.IsSuccess.Should().Be(true);
        var score = result.Value;
        
        score.Illumination.Value.Should().Be(0f); // 1 - min(1.5, 1) = 0
        score.Pose.Value.Should().Be(0f); // 1 - min(2, 1) = 0
        score.Overall.Value.Should().Be(0f);
    }

    [Test]
    public void FromAsymmetryValues_WithLowerAsymmetry_ProducesHigherQuality()
    {
        var lowAsymmetry = FacialSymmetryScore.FromAsymmetryValues(
            0.1f,
            0.15f,
            Array.Empty<float>());
        var highAsymmetry = FacialSymmetryScore.FromAsymmetryValues(
            0.65f,
            0.7f,
            Array.Empty<float>());

        lowAsymmetry.IsSuccess.Should().BeTrue();
        highAsymmetry.IsSuccess.Should().BeTrue();
        lowAsymmetry.Value.Overall.Value.Should().BeGreaterThan(highAsymmetry.Value.Overall.Value);
        lowAsymmetry.Value.Illumination.Value.Should().BeGreaterThan(highAsymmetry.Value.Illumination.Value);
        lowAsymmetry.Value.Pose.Value.Should().BeGreaterThan(highAsymmetry.Value.Pose.Value);
    }
}

[TestFixture]
public class SharpnessScoreTests
{
    [Test]
    public void FromDctAnalysis_WithValidRatio_ReturnsSuccess()
    {
        // Arrange
        var highFrequencyRatio = 0.75f;
        var regionalScores = new Dictionary<string, float>
        {
            { "center", 0.8f },
            { "top", 0.7f },
            { "bottom", 0.65f }
        };
        
        // Act
        var result = SharpnessScore.FromDctAnalysis(highFrequencyRatio, regionalScores);
        
        // Assert
        result.IsSuccess.Should().Be(true);
        var score = result.Value;
        
        score.Overall.Value.Should().Be(0.75f);
        score.HighFrequencyRatio.Should().Be(0.75f);
        score.RegionalScores.Should().BeEquivalentTo(regionalScores);
    }
    
    [Test]
    public void FromDctAnalysis_WithInvalidRatio_ReturnsFailure()
    {
        // Arrange
        var highFrequencyRatio = 1.5f; // > 1
        var regionalScores = new Dictionary<string, float>();
        
        // Act
        var result = SharpnessScore.FromDctAnalysis(highFrequencyRatio, regionalScores);
        
        // Assert
        result.IsFailure.Should().Be(true);
    }
}

[TestFixture]
public class GeometricComplianceTests
{
    [Test]
    public void Overall_CalculatesAverageOfComponents()
    {
        // Arrange
        var headSize = QualityScore.Create(0.8f).Value;
        var centering = QualityScore.Create(0.9f).Value;
        var ipd = QualityScore.Create(0.7f).Value;
        var actualDims = new ImageDimensions(420, 560);
        var expectedDims = new ImageDimensions(420, 560);
        
        var compliance = new GeometricCompliance(
            headSize,
            centering,
            ipd,
            actualDims,
            expectedDims);
        
        // Act
        var overall = compliance.Overall;
        
        // Assert
        overall.Value.Should().Be(0.8f); // (0.8 + 0.9 + 0.7) / 3
    }
}

[TestFixture]
public class Iso19794AssessmentTests
{
    [Test]
    public void IsCompliant_WithHighScoreAndNoCriticalViolations_ReturnsTrue()
    {
        // Arrange
        var overall = QualityScore.Create(0.75f).Value;
        var symmetry = CreateMockSymmetryScore(0.8f);
        var sharpness = CreateMockSharpnessScore(0.85f);
        var geometry = CreateMockGeometricCompliance(0.7f);
        var violations = new List<ComplianceViolation>
        {
            new("Lighting", "Minor shadow on left side", ViolationSeverity.Minor)
        };
        
        var assessment = new Iso19794Assessment(
            overall,
            symmetry,
            sharpness,
            geometry,
            violations,
            DateTime.UtcNow);
        
        // Act & Assert
        assessment.IsCompliant.Should().Be(true);
    }
    
    [Test]
    public void IsCompliant_WithLowScore_ReturnsFalse()
    {
        // Arrange
        var overall = QualityScore.Create(0.65f).Value;
        var symmetry = CreateMockSymmetryScore(0.6f);
        var sharpness = CreateMockSharpnessScore(0.7f);
        var geometry = CreateMockGeometricCompliance(0.65f);
        var violations = new List<ComplianceViolation>();
        
        var assessment = new Iso19794Assessment(
            overall,
            symmetry,
            sharpness,
            geometry,
            violations,
            DateTime.UtcNow);
        
        // Act & Assert
        assessment.IsCompliant.Should().Be(false);
    }
    
    [Test]
    public void IsCompliant_WithCriticalViolation_ReturnsFalse()
    {
        // Arrange
        var overall = QualityScore.Create(0.8f).Value;
        var symmetry = CreateMockSymmetryScore(0.85f);
        var sharpness = CreateMockSharpnessScore(0.9f);
        var geometry = CreateMockGeometricCompliance(0.8f);
        var violations = new List<ComplianceViolation>
        {
            new("Focus", "Image is severely out of focus", ViolationSeverity.Critical)
        };
        
        var assessment = new Iso19794Assessment(
            overall,
            symmetry,
            sharpness,
            geometry,
            violations,
            DateTime.UtcNow);
        
        // Act & Assert
        assessment.IsCompliant.Should().Be(false);
    }
    
    [Test]
    public void Summary_ReturnsCorrectMessage()
    {
        // Arrange
        var overall = QualityScore.Create(0.85f).Value;
        var assessment = new Iso19794Assessment(
            overall,
            CreateMockSymmetryScore(0.85f),
            CreateMockSharpnessScore(0.85f),
            CreateMockGeometricCompliance(0.85f),
            new List<ComplianceViolation>(),
            DateTime.UtcNow);
        
        // Act
        var summary = assessment.Summary;
        
        // Assert
        summary.Should().Contain("Compliant");
        summary.Should().Contain("85.0%");
    }
    
    private FacialSymmetryScore CreateMockSymmetryScore(float overallValue)
    {
        var overall = QualityScore.Create(overallValue).Value;
        var illumination = QualityScore.Create(overallValue + 0.05f).Value;
        var pose = QualityScore.Create(overallValue - 0.05f).Value;
        return new FacialSymmetryScore(overall, illumination, pose, Array.Empty<float>());
    }
    
    private SharpnessScore CreateMockSharpnessScore(float overallValue)
    {
        var overall = QualityScore.Create(overallValue).Value;
        return new SharpnessScore(overall, overallValue, new Dictionary<string, float>());
    }
    
    private GeometricCompliance CreateMockGeometricCompliance(float avgValue)
    {
        var headSize = QualityScore.Create(avgValue).Value;
        var centering = QualityScore.Create(avgValue + 0.1f).Value;
        var ipd = QualityScore.Create(avgValue - 0.1f).Value;
        var dims = new ImageDimensions(420, 560);
        return new GeometricCompliance(headSize, centering, ipd, dims, dims);
    }
}

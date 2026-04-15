using AwesomeAssertions;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using FaceOFFx.Tests.Common;
using NUnit.Framework;

namespace FaceOFFx.Infrastructure.Tests.Services;

/// <summary>
/// Tests for FacialImageEncoder parameter functionality (rotation, confidence)
/// </summary>
[TestFixture]
[NonParallelizable]
public class FacialImageEncoderParameterTests : IntegrationTestBase
{

    /// <summary>
    /// Tests processing with default 15 degree rotation limit
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithDefaultRotationLimit_Processes15DegreeRotation()
    {
        var imagePath = PeopleCorpus.SubjectVariant("generic-guy", "rotated_15.png");
        var imageData = await File.ReadAllBytesAsync(imagePath);

        var result = await FacialImageEncoder.ProcessAsync(imageData);

        result.Should().NotBeNull();
        result.ImageData.Should().NotBeEmpty();
        // Should apply rotation close to 15 degrees (within tolerance)
        Math.Abs(result.Metadata.RotationApplied).Should().BeGreaterThan(10f);
        Math.Abs(result.Metadata.RotationApplied).Should().BeLessThanOrEqualTo(15f);
    }

    /// <summary>
    /// Tests that rotation beyond default limit is clamped
    /// </summary>
    [Test]
    public async Task ProcessAsync_With20DegreeRotation_ClampsToDefault15()
    {
        var imagePath = PeopleCorpus.SubjectVariant("generic-guy", "rotated_20.png");
        var imageData = await File.ReadAllBytesAsync(imagePath);

        var result = await FacialImageEncoder.ProcessAsync(imageData);

        result.Should().NotBeNull();
        // Rotation should be clamped to 15 degrees
        Math.Abs(result.Metadata.RotationApplied).Should().BeLessThanOrEqualTo(15f);
    }

    /// <summary>
    /// Tests custom rotation limit
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithCustomRotationLimit_RespectsLimit()
    {
        var imagePath = PeopleCorpus.SubjectVariant("generic-guy", "rotated_10.png");
        var imageData = await File.ReadAllBytesAsync(imagePath);

        // Set custom 5 degree limit
        var options = ProcessingOptions.PivBalanced with
        {
            MaxRotationDegrees = 5.0f,
        };
        var result = await FacialImageEncoder.ProcessAsync(imageData, options);

        result.Should().NotBeNull();
        // Rotation should be clamped to 5 degrees
        Math.Abs(result.Metadata.RotationApplied).Should().BeLessThanOrEqualTo(5f);
    }

    /// <summary>
    /// Tests negative rotation handling
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithNegativeRotation_HandlesCorrectly()
    {
        var imagePath = PeopleCorpus.SubjectVariant("generic-guy", "rotated_neg10.png");
        var imageData = await File.ReadAllBytesAsync(imagePath);

        var result = await FacialImageEncoder.ProcessAsync(imageData);

        result.Should().NotBeNull();
        // The rotation applied is to correct the image, so if the image is rotated -10 degrees,
        // the correction would be +10 degrees to make it level
        result.Metadata.RotationApplied.Should().BePositive();
        Math.Abs(result.Metadata.RotationApplied).Should().BeGreaterThan(5f);
    }

    /// <summary>
    /// Tests various rotation angles
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithVariousRotations_ProcessesCorrectly()
    {
        var rotations = new[] { 5, 10, 15, -10 };
        var results = new Dictionary<int, float>();

        foreach (var rotation in rotations)
        {
            var fileName =
                rotation >= 0
                    ? $"rotated_{rotation}.png"
                    : $"rotated_neg{Math.Abs(rotation)}.png";
            var imagePath = PeopleCorpus.SubjectVariant("generic-guy", fileName);
            var imageData = await File.ReadAllBytesAsync(imagePath);

            var result = await FacialImageEncoder.ProcessAsync(imageData);
            results[rotation] = result.Metadata.RotationApplied;
        }

        // Verify rotations are in expected ranges
        // Note: Actual rotation correction may be slightly higher than input rotation
        // due to face detection and landmark extraction on rotated images
        Math.Abs(results[5]).Should().BeGreaterThan(2f).And.BeLessThanOrEqualTo(6f);
        Math.Abs(results[10]).Should().BeGreaterThan(5f).And.BeLessThanOrEqualTo(12f);
        Math.Abs(results[15]).Should().BeGreaterThan(10f).And.BeLessThanOrEqualTo(15f);
        // Negative rotation in image requires positive correction
        results[-10].Should().BePositive();
        Math.Abs(results[-10]).Should().BeGreaterThan(5f).And.BeLessThanOrEqualTo(12f);
    }

    /// <summary>
    /// Tests custom confidence threshold
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithHighConfidenceThreshold_ProcessesHighConfidenceFaces()
    {
        var imagePath = PeopleCorpus.SubjectSource("generic-guy", "png");
        var imageData = await File.ReadAllBytesAsync(imagePath);

        // Set high confidence threshold
        var options = ProcessingOptions.PivBalanced with
        {
            MinFaceConfidence = 0.95f,
        };
        var result = await FacialImageEncoder.ProcessAsync(imageData, options);

        result.Should().NotBeNull();
        result.Metadata.FaceConfidence.Should().BeGreaterThanOrEqualTo(0.95f);
    }

    /// <summary>
    /// Tests low confidence threshold
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithLowConfidenceThreshold_AcceptsLowerConfidenceFaces()
    {
        var imagePath = PeopleCorpus.SubjectSource("generic-guy", "png");
        var imageData = await File.ReadAllBytesAsync(imagePath);

        // Set low confidence threshold
        var options = ProcessingOptions.PivBalanced with
        {
            MinFaceConfidence = 0.5f,
        };
        var result = await FacialImageEncoder.ProcessAsync(imageData, options);

        result.Should().NotBeNull();
        // Should process successfully even with lower threshold
        result.ImageData.Should().NotBeEmpty();
    }

    /// <summary>
    /// Tests combined rotation and confidence parameters
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithCombinedParameters_RespectsAllSettings()
    {
        var imagePath = PeopleCorpus.SubjectVariant("generic-guy", "rotated_10.png");
        var imageData = await File.ReadAllBytesAsync(imagePath);

        // Set custom parameters
        var options = ProcessingOptions.PivBalanced with
        {
            MaxRotationDegrees = 8.0f,
            MinFaceConfidence = 0.9f,
        };

        var result = await FacialImageEncoder.ProcessAsync(imageData, options);

        result.Should().NotBeNull();
        // Rotation should be limited to 8 degrees
        Math.Abs(result.Metadata.RotationApplied).Should().BeLessThanOrEqualTo(8f);
        // Face confidence should meet threshold
        result.Metadata.FaceConfidence.Should().BeGreaterThanOrEqualTo(0.9f);
    }

    /// <summary>
    /// Tests that presets maintain their specific settings
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithPresets_MaintainsPresetSettings()
    {
        var imagePath = PeopleCorpus.SubjectSource("generic-guy", "png");
        var imageData = await File.ReadAllBytesAsync(imagePath);

        // Test archival preset (has high confidence requirement)
        var archivalResult = await FacialImageEncoder.ProcessAsync(
            imageData,
            ProcessingOptions.Archival
        );
        archivalResult.Metadata.FaceConfidence.Should().BeGreaterThanOrEqualTo(0.95f); // Archival has 0.95 threshold

        // Test fast preset (has lower confidence requirement)
        var fastResult = await FacialImageEncoder.ProcessAsync(imageData, ProcessingOptions.Fast);
        fastResult.Should().NotBeNull(); // Fast has 0.7 threshold
    }

    /// <summary>
    /// Tests edge cases for rotation parameters
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithEdgeCaseRotations_HandlesCorrectly()
    {
        var imagePath = PeopleCorpus.SubjectSource("generic-guy", "png");
        var imageData = await File.ReadAllBytesAsync(imagePath);

        // Test zero rotation limit (should disable rotation)
        var zeroRotationOptions = ProcessingOptions.PivBalanced with
        {
            MaxRotationDegrees = 0.0f,
        };
        var zeroResult = await FacialImageEncoder.ProcessAsync(imageData, zeroRotationOptions);
        zeroResult.Metadata.RotationApplied.Should().Be(0.0f);

        // Test very high rotation limit
        var highRotationOptions = ProcessingOptions.PivBalanced with
        {
            MaxRotationDegrees = 45.0f,
        };
        var highResult = await FacialImageEncoder.ProcessAsync(imageData, highRotationOptions);
        highResult.Should().NotBeNull();
    }
}

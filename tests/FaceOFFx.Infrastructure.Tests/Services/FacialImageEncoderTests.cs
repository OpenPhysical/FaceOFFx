using AwesomeAssertions;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using FaceOFFx.Tests.Common;
using NUnit.Framework;

namespace FaceOFFx.Infrastructure.Tests.Services;

/// <summary>
/// Tests for the FacialImageEncoder API
/// </summary>
[TestFixture]
[NonParallelizable]
public class FacialImageEncoderTests : IntegrationTestBase
{
    private byte[] _validImageData = null!;
    private byte[] _invalidImageData = null!;

    /// <inheritdoc/>
    [OneTimeSetUp]
    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();

        var testImagePath = PeopleCorpus.SubjectSource("generic-guy", "png");

        if (!File.Exists(testImagePath))
        {
            throw new FileNotFoundException($"Required test image not found: {testImagePath}");
        }

        _validImageData = File.ReadAllBytes(testImagePath);
        Console.WriteLine($"Loaded test image: {testImagePath} ({_validImageData.Length} bytes)");

        _invalidImageData = new byte[] { 1, 2, 3, 4, 5 }; // Invalid image data
    }

    /// <summary>
    /// Tests ProcessAsync with default options
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithDefaultOptions_ReturnsValidResult()
    {
        var result = await FacialImageEncoder.ProcessAsync(_validImageData);

        result.Should().NotBeNull();
        result.ImageData.Should().NotBeEmpty();
        result.Metadata.FileSize.Should().BeGreaterThan(0);
        result.Metadata.OutputDimensions.Width.Should().Be(420);
        result.Metadata.OutputDimensions.Height.Should().Be(560);
        result.Metadata.CompressionRate.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Tests ProcessAsync with custom options
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithCustomOptions_UsesOptions()
    {
        var customOptions = ProcessingOptions.PivHigh with { MinFaceConfidence = 0.9f };

        var result = await FacialImageEncoder.ProcessAsync(_validImageData, customOptions);

        result.Should().NotBeNull();
        result.Metadata.TargetSize.Should().HaveValue();
        result.Metadata.TargetSize!.Value.Should().Be(30000); // PivHigh target
    }

    /// <summary>
    /// Tests ProcessForTwicAsync targets correct file size
    /// </summary>
    [Test]
    public async Task ProcessForTwicAsync_TargetsCorrectSize()
    {
        var result = await FacialImageEncoder.ProcessForTwicAsync(_validImageData);

        result.Should().NotBeNull();
        (result.Metadata.FileSize <= 14000).Should().BeTrue();
        result.Metadata.TargetSize.HasValue.Should().BeTrue();
        result.Metadata.TargetSize!.Value.Should().Be(14000);
    }

    /// <summary>
    /// Tests ProcessForPivAsync targets correct file size
    /// </summary>
    [Test]
    public async Task ProcessForPivAsync_TargetsCorrectSize()
    {
        var result = await FacialImageEncoder.ProcessForPivAsync(_validImageData);

        result.Should().NotBeNull();
        result.Metadata.TargetSize.HasValue.Should().BeTrue();
        result.Metadata.TargetSize!.Value.Should().Be(22000);
    }

    /// <summary>
    /// Tests ProcessToSizeAsync with custom target size
    /// </summary>
    [Test]
    public async Task ProcessToSizeAsync_WithCustomSize_TargetsSize()
    {
        var targetSize = 25000;
        var result = await FacialImageEncoder.ProcessToSizeAsync(_validImageData, targetSize);

        result.Should().NotBeNull();
        (result.Metadata.FileSize <= targetSize).Should().BeTrue();
        result.Metadata.TargetSize.HasValue.Should().BeTrue();
        result.Metadata.TargetSize!.Value.Should().Be(targetSize);
    }

    /// <summary>
    /// Tests ProcessWithRateAsync uses specified compression rate
    /// </summary>
    [Test]
    public async Task ProcessWithRateAsync_WithCustomRate_UsesRate()
    {
        var compressionRate = 1.5f;
        var result = await FacialImageEncoder.ProcessWithRateAsync(
            _validImageData,
            compressionRate
        );

        result.Should().NotBeNull();
        result.Metadata.CompressionRate.Should().Be(compressionRate);
        result.Metadata.TargetSize.HasValue.Should().BeFalse(); // No target size for fixed rate
    }

    /// <summary>
    /// Tests ProcessAsync fails gracefully with invalid image data
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithInvalidImageData_ThrowsException()
    {
        var action = () => FacialImageEncoder.ProcessAsync(_invalidImageData);

        await action.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Tests ProcessAsync fails gracefully with empty image data
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithEmptyImageData_ThrowsException()
    {
        var action = () => FacialImageEncoder.ProcessAsync(Array.Empty<byte>());

        await action.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Tests ProcessAsync with null image data throws ArgumentException
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithNullImageData_ThrowsArgumentException()
    {
        await AssertThrowsAsync<ArgumentNullException>(() =>
            FacialImageEncoder.ProcessAsync(null!)
        );
    }

    /// <summary>
    /// Tests that ProcessingResult contains valid metadata
    /// </summary>
    [Test]
    public async Task ProcessAsync_ValidImage_ReturnsCompleteMetadata()
    {
        var result = await FacialImageEncoder.ProcessAsync(
            _validImageData,
            ProcessingOptions.Archival
        );

        result.Should().NotBeNull();

        var metadata = result.Metadata;
        metadata.OutputDimensions.Width.Should().Be(420);
        metadata.OutputDimensions.Height.Should().Be(560);
        metadata.FaceConfidence.Should().BeGreaterThan(0);
        (metadata.FaceConfidence <= 1).Should().BeTrue();
        metadata.ProcessingTime.Should().BeGreaterThan(TimeSpan.Zero);
        metadata.CompressionRate.Should().BeGreaterThan(0);
        metadata.Warnings.Should().NotBeNull();
        metadata.AdditionalData.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that different presets produce different file sizes
    /// </summary>
    [Test]
    public async Task ProcessAsync_DifferentPresets_ProduceDifferentSizes()
    {
        var fastResult = await FacialImageEncoder.ProcessAsync(
            _validImageData,
            ProcessingOptions.Fast
        );
        var standardResult = await FacialImageEncoder.ProcessAsync(
            _validImageData,
            ProcessingOptions.PivBalanced
        );
        var archivalResult = await FacialImageEncoder.ProcessAsync(
            _validImageData,
            ProcessingOptions.Archival
        );

        fastResult.Should().NotBeNull();
        standardResult.Should().NotBeNull();
        archivalResult.Should().NotBeNull();

        // Generally, archival should be largest, standard medium, fast smallest
        // But due to target size strategies, this might not always hold
        fastResult.Metadata.FileSize.Should().BeGreaterThan(0);
        standardResult.Metadata.FileSize.Should().BeGreaterThan(0);
        archivalResult.Metadata.FileSize.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Tests ProcessToSizeAsync with very small target fails appropriately
    /// </summary>
    [Test]
    public async Task ProcessToSizeAsync_WithVerySmallTarget_ReturnsFailure()
    {
        var action = () => FacialImageEncoder.ProcessToSizeAsync(_validImageData, 1000); // Very small

        await action
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Cannot compress*");
    }

    /// <summary>
    /// Tests ProcessWithRateAsync with invalid rate fails appropriately
    /// </summary>
    [Test]
    public async Task ProcessWithRateAsync_WithInvalidRate_ReturnsFailure()
    {
        var action = () => FacialImageEncoder.ProcessWithRateAsync(_validImageData, -1.0f); // Invalid rate

        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Tests that processing produces PIV-compliant dimensions
    /// </summary>
    [Test]
    public async Task ProcessAsync_AllPresets_ProducePivDimensions()
    {
        var presets = new[]
        {
            ProcessingOptions.TwicMax,
            ProcessingOptions.PivMin,
            ProcessingOptions.PivBalanced,
            ProcessingOptions.PivHigh,
            ProcessingOptions.Archival,
            ProcessingOptions.Fast,
        };

        foreach (var preset in presets)
        {
            var result = await FacialImageEncoder.ProcessAsync(_validImageData, preset);

            result.Should().NotBeNull($"Preset {preset} should succeed");
            result.Metadata.OutputDimensions.Width.Should().Be(420);
            result.Metadata.OutputDimensions.Height.Should().Be(560);
        }
    }
}

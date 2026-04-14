using AwesomeAssertions;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using FaceOFFx.Tests.Common;
using NUnit.Framework;

namespace FaceOFFx.Infrastructure.Tests.Services;

/// <summary>
/// Tests for FacialImageEncoder with various image formats and configurations
/// </summary>
[TestFixture]
[NonParallelizable]
public class FacialImageEncoderFormatTests : IntegrationTestBase
{
    private static readonly string TestImagesPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "..",
        "tests",
        "sample_images"
    );

    /// <summary>
    /// Tests that JPEG format is processed correctly
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithJpegInput_ProcessesSuccessfully()
    {
        var jpegPath = Path.Combine(TestImagesPath, "generic_guy.jpg");
        var imageData = await File.ReadAllBytesAsync(jpegPath);

        var result = await FacialImageEncoder.ProcessAsync(imageData);

        result.Should().NotBeNull();
        result.ImageData.Should().NotBeEmpty();
        result.Metadata.OutputDimensions.Width.Should().Be(420);
        result.Metadata.OutputDimensions.Height.Should().Be(560);
    }

    /// <summary>
    /// Tests that PNG format is processed correctly
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithPngInput_ProcessesSuccessfully()
    {
        var pngPath = Path.Combine(TestImagesPath, "generic_guy.png");
        var imageData = await File.ReadAllBytesAsync(pngPath);

        var result = await FacialImageEncoder.ProcessAsync(imageData);

        result.Should().NotBeNull();
        result.ImageData.Should().NotBeEmpty();
        result.Metadata.OutputDimensions.Width.Should().Be(420);
        result.Metadata.OutputDimensions.Height.Should().Be(560);
    }

    /// <summary>
    /// Tests that TIFF format is processed correctly
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithTiffInput_ProcessesSuccessfully()
    {
        var tiffPath = Path.Combine(TestImagesPath, "generic_guy.tif");
        var imageData = await File.ReadAllBytesAsync(tiffPath);

        var result = await FacialImageEncoder.ProcessAsync(imageData);

        result.Should().NotBeNull();
        result.ImageData.Should().NotBeEmpty();
        result.Metadata.OutputDimensions.Width.Should().Be(420);
        result.Metadata.OutputDimensions.Height.Should().Be(560);
    }

    /// <summary>
    /// Tests that JPEG 2000 format is NOT supported as input (ImageSharp limitation)
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithJpeg2000Input_ThrowsException()
    {
        var jp2Path = Path.Combine(TestImagesPath, "generic_guy.jp2");
        var imageData = await File.ReadAllBytesAsync(jp2Path);

        // ImageSharp doesn't support JP2 as input, only as output through our encoder
        await AssertThrowsAsync<ArgumentException>(() =>
            FacialImageEncoder.ProcessAsync(imageData)
        );
    }

    /// <summary>
    /// Tests processing multiple formats in sequence
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithMultipleFormats_AllProcessSuccessfully()
    {
        var formats = new[] { "jpg", "png", "tif" }; // JP2 not supported as input
        var results = new List<ProcessingResultDto>();

        foreach (var format in formats)
        {
            var imagePath = Path.Combine(TestImagesPath, $"generic_guy.{format}");
            var imageData = await File.ReadAllBytesAsync(imagePath);

            var result = await FacialImageEncoder.ProcessAsync(imageData);
            results.Add(result);
        }

        results.Should().HaveCount(3);
        results
            .Should()
            .AllSatisfy(r =>
            {
                r.Should().NotBeNull();
                r.ImageData.Should().NotBeEmpty();
                r.Metadata.OutputDimensions.Width.Should().Be(420);
                r.Metadata.OutputDimensions.Height.Should().Be(560);
            });
    }

    /// <summary>
    /// Tests that Try pattern works with different formats
    /// </summary>
    [Test]
    public async Task TryProcessAsync_WithDifferentFormats_ReturnsSuccess()
    {
        var formats = new[] { "jpg", "png", "tif" }; // JP2 not supported as input

        foreach (var format in formats)
        {
            var imagePath = Path.Combine(TestImagesPath, $"generic_guy.{format}");
            var imageData = await File.ReadAllBytesAsync(imagePath);

            var (success, result, error) = await FacialImageEncoder.TryProcessAsync(imageData);

            success.Should().BeTrue($"Format {format} should process successfully");
            result.Should().NotBeNull();
            error.Should().BeNull();
        }
    }

    /// <summary>
    /// Tests that all formats produce consistent results
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithDifferentFormats_ProducesConsistentResults()
    {
        var formats = new[] { "jpg", "png", "tif" }; // JP2 not supported as input
        var results = new Dictionary<string, ProcessingResultDto>();

        foreach (var format in formats)
        {
            var imagePath = Path.Combine(TestImagesPath, $"generic_guy.{format}");
            var imageData = await File.ReadAllBytesAsync(imagePath);

            results[format] = await FacialImageEncoder.ProcessAsync(imageData);
        }

        // All should have same output dimensions
        var dimensions = results.Values.Select(r => r.Metadata.OutputDimensions).ToList();
        dimensions.Should().AllBeEquivalentTo(dimensions.First());

        // Face confidence should be similar across formats (within 5%)
        var confidences = results.Values.Select(r => r.Metadata.FaceConfidence).ToList();
        var avgConfidence = confidences.Average();
        confidences
            .Should()
            .AllSatisfy(c => Math.Abs(c - avgConfidence).Should().BeLessThan(0.05f));

        // Rotation should be very similar (within 0.1 degrees)
        var rotations = results.Values.Select(r => r.Metadata.RotationApplied).ToList();
        var avgRotation = rotations.Average();
        rotations.Should().AllSatisfy(r => Math.Abs(r - avgRotation).Should().BeLessThan(0.1f));
    }

    /// <summary>
    /// Tests error handling with corrupted image data
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithCorruptedData_ThrowsInvalidOperationException()
    {
        var corruptedData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }; // Partial JPEG header

        await AssertThrowsAsync<ArgumentException>(() =>
            FacialImageEncoder.ProcessAsync(corruptedData)
        );
    }

    /// <summary>
    /// Tests that unsupported format throws appropriate exception
    /// </summary>
    [Test]
    public async Task ProcessAsync_WithUnsupportedFormat_ThrowsInvalidOperationException()
    {
        var bmpHeader = new byte[] { 0x42, 0x4D, 0x00, 0x00, 0x00, 0x00 }; // BMP header (not supported by ImageSharp)

        await AssertThrowsAsync<ArgumentException>(() =>
            FacialImageEncoder.ProcessAsync(bmpHeader)
        );
    }
}

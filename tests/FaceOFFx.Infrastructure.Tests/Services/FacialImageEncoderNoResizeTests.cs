using AwesomeAssertions;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using FaceOFFx.Tests.Common;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Infrastructure.Tests.Services;

/// <summary>
/// Tests for the new no-resize functionality in FacialImageEncoder
/// </summary>
[TestFixture]
public class FacialImageEncoderNoResizeTests : IntegrationTestBase
{
    [Test]
    public async Task ProcessWithoutFaceDetectionAsync_WithJpegFormat_ShouldPreserveOriginalDimensions()
    {
        // Arrange
        using var image = new Image<Rgba32>(800, 600);
        image.Mutate(x => x.BackgroundColor(Color.Blue));
        
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms);
        var imageData = ms.ToArray();

        // Act
        var result = await FacialImageEncoder.ProcessWithoutFaceDetectionAsync(
            imageData,
            options: null,
            outputFormat: "jpeg",
            jpegQuality: 85
        );

        // Assert
        result.Should().NotBeNull();
        result.ImageData.Should().NotBeNull();
        result.ImageData.Length.Should().BeGreaterThan(0);
        result.Metadata.OutputDimensions.Width.Should().Be(800);
        result.Metadata.OutputDimensions.Height.Should().Be(600);
        result.Metadata.AdditionalData["OutputFormat"].Should().Be("JPEG");
        result.Metadata.AdditionalData["ProcessingMode"].Should().Be("NoResize");
    }

    [Test]
    public async Task ProcessWithoutFaceDetectionAsync_WithPngFormat_ShouldProducePngOutput()
    {
        // Arrange
        using var image = new Image<Rgba32>(640, 480);
        image.Mutate(x => x.BackgroundColor(Color.Green));
        
        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms);
        var imageData = ms.ToArray();

        // Act
        var result = await FacialImageEncoder.ProcessWithoutFaceDetectionAsync(
            imageData,
            outputFormat: "png"
        );

        // Assert
        result.Should().NotBeNull();
        result.ImageData.Should().NotBeNull();
        
        // Verify PNG signature
        result.ImageData.Length.Should().BeGreaterThan(8);
        result.ImageData[0].Should().Be((byte)0x89); // PNG signature
        result.ImageData[1].Should().Be((byte)0x50); // P
        result.ImageData[2].Should().Be((byte)0x4E); // N
        result.ImageData[3].Should().Be((byte)0x47); // G
        
        result.Metadata.AdditionalData["OutputFormat"].Should().Be("PNG");
    }

    [Test]
    public async Task ProcessWithoutFaceDetectionAsync_WithTiffFormat_ShouldProduceTiffOutput()
    {
        // Arrange
        using var image = new Image<Rgba32>(320, 240);
        image.Mutate(x => x.BackgroundColor(Color.Red));
        
        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms);
        var imageData = ms.ToArray();

        // Act
        var result = await FacialImageEncoder.ProcessWithoutFaceDetectionAsync(
            imageData,
            outputFormat: "tiff"
        );

        // Assert
        result.Should().NotBeNull();
        result.ImageData.Should().NotBeNull();
        
        // Verify TIFF signature (little-endian or big-endian)
        result.ImageData.Length.Should().BeGreaterThan(4);
        var isLittleEndian = result.ImageData[0] == 0x49 && result.ImageData[1] == 0x49;
        var isBigEndian = result.ImageData[0] == 0x4D && result.ImageData[1] == 0x4D;
        (isLittleEndian || isBigEndian).Should().BeTrue("Invalid TIFF signature");
        
        result.Metadata.AdditionalData["OutputFormat"].Should().Be("TIFF");
    }

    [Test]
    public async Task ProcessWithoutFaceDetectionAsync_WithJp2Format_ShouldApplyDynamicRoi()
    {
        // Arrange
        using var image = new Image<Rgba32>(1000, 800);
        image.Mutate(x => x.BackgroundColor(Color.Blue));
        
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms);
        var imageData = ms.ToArray();

        var options = ProcessingOptions.PivBalanced with { EnableRoi = true };

        // Act
        var result = await FacialImageEncoder.ProcessWithoutFaceDetectionAsync(
            imageData,
            options: options,
            outputFormat: "jp2"
        );

        // Assert
        result.Should().NotBeNull();
        result.ImageData.Should().NotBeNull();
        result.ImageData.Length.Should().BeGreaterThan(0);
        result.Metadata.OutputDimensions.Width.Should().Be(1000);
        result.Metadata.OutputDimensions.Height.Should().Be(800);
        result.Metadata.CompressionRate.Should().BeGreaterThan(0);
        result.Metadata.AdditionalData["OutputFormat"].Should().Be("JP2");
    }

    [TestCase("jpeg")]
    [TestCase("jpg")]
    [TestCase("JPEG")]
    [TestCase("JPG")]
    public async Task ProcessWithoutFaceDetectionAsync_WithJpegVariants_ShouldAllWork(string format)
    {
        // Arrange
        using var image = new Image<Rgba32>(200, 200);
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms);
        var imageData = ms.ToArray();

        // Act
        var result = await FacialImageEncoder.ProcessWithoutFaceDetectionAsync(
            imageData,
            outputFormat: format,
            jpegQuality: 90
        );

        // Assert
        result.Should().NotBeNull();
        result.ImageData.Should().NotBeNull();
        result.ImageData.Length.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task ProcessWithoutFaceDetectionAsync_ShouldSetFaceConfidenceToOne()
    {
        // Arrange
        using var image = new Image<Rgba32>(100, 100);
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms);
        var imageData = ms.ToArray();

        // Act
        var result = await FacialImageEncoder.ProcessWithoutFaceDetectionAsync(
            imageData,
            outputFormat: "png"
        );

        // Assert
        result.Metadata.FaceConfidence.Should().Be(1.0f);
    }

    [Test]
    public async Task ProcessWithoutFaceDetectionAsync_ShouldHaveNoRotation()
    {
        // Arrange
        using var image = new Image<Rgba32>(100, 100);
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms);
        var imageData = ms.ToArray();

        // Act
        var result = await FacialImageEncoder.ProcessWithoutFaceDetectionAsync(
            imageData,
            outputFormat: "tiff"
        );

        // Assert
        result.Metadata.RotationApplied.Should().Be(0f);
    }

    [TestCase("png")]
    [TestCase("tiff")]
    public async Task ProcessWithoutFaceDetectionAsync_WithDifferentFormats_ShouldSetCorrectMetadata(string format)
    {
        // Arrange
        using var image = new Image<Rgba32>(300, 400);
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms);
        var imageData = ms.ToArray();

        // Act
        var result = await FacialImageEncoder.ProcessWithoutFaceDetectionAsync(
            imageData,
            outputFormat: format
        );

        // Assert
        result.Metadata.OutputDimensions.Width.Should().Be(300);
        result.Metadata.OutputDimensions.Height.Should().Be(400);
        result.Metadata.AdditionalData["ProcessingMode"].Should().Be("NoResize");
        result.Metadata.AdditionalData["OutputFormat"].Should().Be(format.ToUpperInvariant());
    }
}
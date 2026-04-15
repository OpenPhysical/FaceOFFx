using CoreJ2K;
using CoreJ2K.ImageSharp;
using FaceOFFx.Core.Domain.Common;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Service for encoding images to JPEG 2000 format with ROI support for PIV compliance.
/// </summary>
[PublicAPI]
public class Jpeg2000EncoderService : IJpeg2000Encoder
{
    private readonly ILogger<Jpeg2000EncoderService> _logger;
    private static bool _isRegistered;
    private static readonly object RegisterLock = new object();

    /// <summary>
    /// Service for encoding images to JPEG 2000 format with ROI Inner Region for PIV compliance.
    /// Implements methods for region-based encoding with configurable parameters.
    /// </summary>
    public Jpeg2000EncoderService(ILogger<Jpeg2000EncoderService> logger)
    {
        _logger = logger;
        _logger.LogDebug("Jpeg2000EncoderService constructor called");
    }

    private void EnsureImageSharpRegistered()
    {
        _logger.LogDebug(
            "EnsureImageSharpRegistered called, _isRegistered: {IsRegistered}",
            _isRegistered
        );

        if (_isRegistered)
        {
            return;
        }
        lock (RegisterLock)
        {
            if (_isRegistered)
            {
                return;
            }
            try
            {
                _logger.LogDebug("Registering ImageSharpImageCreator");
                ImageSharpImageCreator.Register();
                _isRegistered = true;
                _logger.LogInformation("ImageSharpImageCreator registered successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register ImageSharpImageCreator");
                // Try to continue anyway - it might already be registered
                _isRegistered = true;
            }
        }
    }

    /// <summary>
    /// Encodes an image to JPEG 2000 with ROI Inner Region for PIV compliance.
    /// Uses JPEG 2000 maxshift ROI encoding with configurable quality balance.
    /// </summary>
    public Result<byte[], PipelineError> EncodeWithRoi(
        Image<Rgba32> image,
        FacialRoiSet roiSet,
        float baseRate = 1.0f,
        int roiStartLevel = 1,
        bool enableRoi = false,
        bool roiAlign = true
    )
    {
        _logger.LogDebug(
            "EncodeWithRoi started - Image: {Width}x{Height}, BaseRate: {BaseRate}, RoiStartLevel: {RoiStartLevel}, EnableRoi: {EnableRoi}, RoiAlign: {RoiAlign}",
            image.Width,
            image.Height,
            baseRate,
            roiStartLevel,
            enableRoi,
            roiAlign
        );

        try
        {
            // Ensure ImageSharp support is registered
            EnsureImageSharpRegistered();

            // Get default encoder parameter list from CoreJ2K
            _logger.LogDebug("Getting default encoder parameters from J2kImage");
            var parameters = J2kImage.GetDefaultEncoderParameterList();

            // Build ROI specification only if ROI is enabled
            if (enableRoi)
            {
                // Appendix C.6 approach: single Inner Region with higher priority
                var innerRegion = roiSet.InnerRegion;
                var roiSpec =
                    $"R {innerRegion.BoundingBox.X} {innerRegion.BoundingBox.Y} "
                    + $"{innerRegion.BoundingBox.Width} {innerRegion.BoundingBox.Height}";

                parameters["Rroi"] = roiSpec;
                _logger.LogDebug("Appendix C.6 Inner Region ROI specification: {RoiSpec}", roiSpec);

                // Control ROI resolution level priority
                // Higher values include more resolution levels in ROI priority, reducing quality difference
                // 0 = only base level (aggressive ROI priority), 1-2 = more balanced quality
                parameters["Rstart_level"] = roiStartLevel.ToString();
                _logger.LogDebug("ROI start level: {RoiStartLevel}", roiStartLevel);

                // Use block alignment for more balanced quality distribution
                // With small tiles, alignment gives precise ROI control
                parameters["Ralign"] = roiAlign ? "on" : "off";
                _logger.LogDebug("ROI alignment: {RoiAlign}", roiAlign ? "enabled" : "disabled");
            }
            else
            {
                _logger.LogDebug("ROI disabled");
            }

            // Set base compression rate - higher values improve overall quality
            // This affects both ROI and background areas proportionally
            parameters["rate"] = baseRate.ToString("F1");
            _logger.LogDebug("Base rate: {BaseRate} bits/pixel", baseRate);

            // Use single tile covering entire PIV image for optimal compression efficiency
            // Single tile eliminates tile boundary artifacts and maximizes compression
            parameters["Stiles"] = $"{image.Width} {image.Height}";
            _logger.LogDebug(
                "Tile size: {Width}x{Height} (single tile)",
                image.Width,
                image.Height
            );

            // Encode with ROI parameters
            _logger.LogDebug("Starting J2kImage encoding");
            var encodedData = J2kImage.ToBytes(image, parameters);
            _logger.LogDebug(
                "J2kImage encoding completed, output size: {Size} bytes",
                encodedData.Length
            );

            _logger.LogInformation(
                "EncodeWithRoi completed successfully, encoded {Bytes} bytes",
                encodedData.Length
            );
            return Result.Success<byte[], PipelineError>(encodedData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JPEG 2000 ROI encoding failed");
            return Result.Failure<byte[], PipelineError>(
                new RenderError($"JPEG 2000 ROI encoding failed: {ex.Message}", "jpeg2000"));
        }
        finally
        {
            _logger.LogDebug("EncodeWithRoi ended");
        }
    }
}

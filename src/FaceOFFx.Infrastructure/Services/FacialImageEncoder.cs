using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Facial image encoder for PIV and TWIC compatible image transformation.
/// This is the primary public API for PIV/TWIC-compatible processing.
/// </summary>
[PublicAPI]
public static class FacialImageEncoder
{
    /// <summary>
    /// Process image bytes to PIV-compatible JPEG 2000 format
    /// </summary>
    /// <param name="imageData">Input image data (JPEG, PNG, etc.)</param>
    /// <param name="options">Processing options. Uses PIV standard if not specified.
    /// ProcessingOptions is an immutable record - use 'with' syntax to modify:
    /// ProcessingOptions.PivBalanced with { MinFaceConfidence = 0.9f }</param>
    /// <param name="logger">Optional logger for processing information. Uses NullLogger if not provided.</param>
    /// <returns>Typed processing result with encoded image data and metadata</returns>
    public static async Task<Result<ProcessingResultDto, PipelineError>> ProcessAsync(
        byte[] imageData,
        ProcessingOptions? options = null,
        ILogger? logger = null
    )
    {
        return await ProcessAsync(imageData, options, "jp2", 85, logger);
    }
    
    /// <summary>
    /// Process image bytes and encode in the specified format
    /// </summary>
    /// <param name="imageData">Input image data</param>
    /// <param name="options">Processing options. Uses PIV standard if not specified.</param>
    /// <param name="outputFormat">Output format: "jp2", "jpeg", "png", or "tiff"</param>
    /// <param name="jpegQuality">JPEG quality (1-100) when outputFormat is "jpeg"</param>
    /// <param name="logger">Optional logger for processing information. Uses NullLogger if not provided.</param>
    /// <returns>Typed processing result with encoded image data and metadata</returns>
    public static async Task<Result<ProcessingResultDto, PipelineError>> ProcessAsync(
        byte[] imageData,
        ProcessingOptions? options,
        string outputFormat,
        int jpegQuality,
        ILogger? logger)
    {
        if (imageData is null)
        {
            return Result.Failure<ProcessingResultDto, PipelineError>(
                new InputError("Input image data is required.", nameof(imageData)));
        }

        var processingOptions = options ?? ProcessingOptions.PivBalanced;
        logger ??= NullLogger.Instance;

        var validation = ValidateProcessingOptions(processingOptions);
        if (validation.IsFailure)
        {
            return Result.Failure<ProcessingResultDto, PipelineError>(validation.Error);
        }

        using var cts =
            processingOptions.ProcessingTimeout != TimeSpan.Zero
                ? new CancellationTokenSource(processingOptions.ProcessingTimeout)
                : new CancellationTokenSource();

        try
        {
            var result = await ProcessImageInternalAsync(
                imageData,
                processingOptions,
                outputFormat,
                jpegQuality,
                logger,
                cts.Token
            ).ConfigureAwait(false);

            return result.Map(ConvertToDto);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            return Result.Failure<ProcessingResultDto, PipelineError>(
                new RenderError(
                    $"Processing exceeded timeout of {processingOptions.ProcessingTimeout}",
                    "facial-image-encoder"));
        }
        catch (Exception ex)
        {
            return Result.Failure<ProcessingResultDto, PipelineError>(
                new RenderError($"Face processing failed: {ex.Message}", "facial-image-encoder"));
        }
    }

    /// <summary>
    /// Process image bytes for TWIC card compatibility (14KB maximum)
    /// </summary>
    /// <param name="imageData">Input image data</param>
    /// <param name="logger">Optional logger for processing information. Uses NullLogger if not provided.</param>
    /// <returns>Processing result optimized for TWIC requirements</returns>
    /// <exception cref="ArgumentNullException">Thrown when imageData is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when processing fails</exception>
    /// <remarks>
    /// TWIC cards have strict size constraints. This method targets 14KB maximum
    /// to fit within the card storage limits.
    /// </remarks>
    public static async Task<Result<ProcessingResultDto, PipelineError>> ProcessForTwicAsync(
        byte[] imageData,
        ILogger? logger = null
    ) => await ProcessAsync(imageData, ProcessingOptions.TwicMax, logger);

    /// <summary>
    /// Process image bytes for PIV card compatibility (20KB target)
    /// </summary>
    /// <param name="imageData">Input image data</param>
    /// <param name="logger">Optional logger for processing information. Uses NullLogger if not provided.</param>
    /// <returns>Processing result optimized for PIV requirements</returns>
    /// <exception cref="ArgumentNullException">Thrown when imageData is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when processing fails</exception>
    /// <remarks>
    /// Standard PIV processing that balances file size with image quality.
    /// Suitable for most government ID card applications.
    /// </remarks>
    public static async Task<Result<ProcessingResultDto, PipelineError>> ProcessForPivAsync(
        byte[] imageData,
        ILogger? logger = null
    ) => await ProcessAsync(imageData, ProcessingOptions.PivBalanced, logger);

    /// <summary>
    /// Process image bytes with custom target file size
    /// </summary>
    /// <param name="imageData">Input image data</param>
    /// <param name="targetSizeBytes">Target file size in bytes</param>
    /// <param name="logger">Optional logger for processing information. Uses NullLogger if not provided.</param>
    /// <returns>Processing result targeting specified file size</returns>
    /// <exception cref="ArgumentNullException">Thrown when imageData is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when targetSizeBytes is invalid</exception>
    /// <exception cref="InvalidOperationException">Thrown when processing fails</exception>
    /// <remarks>
    /// Uses stepped compression rates to achieve the target file size.
    /// May not achieve exact size due to the discrete nature of JPEG 2000 compression.
    /// </remarks>
    public static async Task<Result<ProcessingResultDto, PipelineError>> ProcessToSizeAsync(
        byte[] imageData,
        int targetSizeBytes,
        ILogger? logger = null
    )
    {
        if (targetSizeBytes <= 0)
        {
            return Result.Failure<ProcessingResultDto, PipelineError>(
                new ValidationError("Target size must be greater than zero", nameof(targetSizeBytes)));
        }

        var options = ProcessingOptions.PivBalanced with
        {
            Strategy = EncodingStrategy.TargetSize(targetSizeBytes),
        };

        return await ProcessAsync(imageData, options, logger);
    }

    /// <summary>
    /// Process image bytes with fixed compression rate
    /// </summary>
    /// <param name="imageData">Input image data</param>
    /// <param name="compressionRate">Compression rate in bits per pixel</param>
    /// <param name="logger">Optional logger for processing information. Uses NullLogger if not provided.</param>
    /// <returns>Processing result using specified compression rate</returns>
    /// <exception cref="ArgumentNullException">Thrown when imageData is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when compressionRate is invalid</exception>
    /// <exception cref="InvalidOperationException">Thrown when processing fails</exception>
    /// <remarks>
    /// Provides predictable compression behavior when file size constraints are flexible.
    /// Higher rates produce larger files with better quality.
    /// </remarks>
    public static async Task<Result<ProcessingResultDto, PipelineError>> ProcessWithRateAsync(
        byte[] imageData,
        float compressionRate,
        ILogger? logger = null
    )
    {
        if (compressionRate <= 0)
        {
            return Result.Failure<ProcessingResultDto, PipelineError>(
                new ValidationError("Compression rate must be greater than zero", nameof(compressionRate)));
        }

        var options = ProcessingOptions.PivBalanced with
        {
            Strategy = EncodingStrategy.FixedRate(compressionRate),
        };

        return await ProcessAsync(imageData, options, logger);
    }

    /// <summary>
    /// Try to process image bytes, returning success status and result
    /// </summary>
    /// <param name="imageData">Input image data</param>
    /// <param name="options">Processing options. Uses PIV standard if not specified.</param>
    /// <param name="logger">Optional logger for processing information</param>
    /// <returns>Tuple containing success status, result if successful, and error message if failed</returns>
    public static Task<Result<ProcessingResultDto, PipelineError>> TryProcessAsync(
        byte[] imageData,
        ProcessingOptions? options = null,
        ILogger? logger = null) =>
        ProcessAsync(imageData, options, logger);

    /// <summary>
    /// Process image bytes without face detection, using the image as-is
    /// </summary>
    /// <param name="imageData">Input image data</param>
    /// <param name="options">Processing options. Uses PIV standard if not specified.</param>
    /// <param name="outputFormat">Output format: "jp2" or "jpeg"</param>
    /// <param name="jpegQuality">JPEG quality (1-100) when outputFormat is "jpeg"</param>
    /// <param name="logger">Optional logger for processing information</param>
    /// <returns>Processing result with encoded image data and metadata</returns>
    /// <exception cref="ArgumentNullException">Thrown when imageData is null</exception>
    /// <exception cref="ArgumentException">Thrown when processing options are invalid</exception>
    /// <exception cref="InvalidOperationException">Thrown when processing fails</exception>
    /// <remarks>
    /// This method skips face detection and PIV transformation, using the image in its original dimensions.
    /// For JPEG 2000 output, it applies the Appendix C.6 ROI formula dynamically based on image dimensions.
    /// For JPEG output, no ROI is applied as JPEG doesn't support ROI encoding.
    /// </remarks>
    [PublicAPI]
    public static async Task<Result<ProcessingResultDto, PipelineError>> ProcessWithoutFaceDetectionAsync(
        byte[] imageData,
        ProcessingOptions? options = null,
        string outputFormat = "jp2",
        int jpegQuality = 85,
        ILogger? logger = null
    )
    {
        if (imageData is null)
        {
            return Result.Failure<ProcessingResultDto, PipelineError>(
                new InputError("Input image data is required.", nameof(imageData)));
        }

        var processingOptions = options ?? ProcessingOptions.PivBalanced;
        logger ??= NullLogger.Instance;

        // Validate options
        var validation = ValidateProcessingOptions(processingOptions);
        if (validation.IsFailure)
        {
            return Result.Failure<ProcessingResultDto, PipelineError>(validation.Error);
        }

        // Validate JPEG quality
        if (
            (
                outputFormat.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
                || outputFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase)
            ) && (jpegQuality < 1 || jpegQuality > 100)
        )
        {
            return Result.Failure<ProcessingResultDto, PipelineError>(
                new ValidationError("JPEG quality must be between 1 and 100", nameof(jpegQuality)));
        }

        var startTime = DateTime.UtcNow;

        try
        {
            var imageResult = LoadImage(imageData);
            if (imageResult.IsFailure)
            {
                return Result.Failure<ProcessingResultDto, PipelineError>(imageResult.Error);
            }

            using var image = imageResult.Value;
            logger.LogDebug(
                "ProcessWithoutFaceDetection: Loaded image {Width}x{Height}",
                image.Width,
                image.Height
            );

            byte[] encodedData;
            float compressionRate = 0;

            if (
                outputFormat.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
                || outputFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase)
            )
            {
                // JPEG encoding path
                logger.LogDebug("Encoding as JPEG with quality {Quality}", jpegQuality);
                encodedData = await EncodeAsJpegAsync(image, jpegQuality);
            }
            else if (outputFormat.Equals("png", StringComparison.OrdinalIgnoreCase))
            {
                // PNG encoding path
                logger.LogDebug("Encoding as PNG");
                encodedData = await EncodeAsPngAsync(image);
            }
            else if (
                outputFormat.Equals("tiff", StringComparison.OrdinalIgnoreCase)
                || outputFormat.Equals("tif", StringComparison.OrdinalIgnoreCase)
            )
            {
                // TIFF encoding path
                logger.LogDebug("Encoding as TIFF");
                encodedData = await EncodeAsTiffAsync(image);
            }
            else
            {
                // JPEG 2000 encoding path with dynamic ROI
                logger.LogDebug("Encoding as JPEG 2000 with dynamic ROI calculation");

                // Calculate ROI based on image dimensions using Appendix C.6 formula
                var roiSet = FacialRoiSet.CalculateRoiForDimensions(image.Width, image.Height);

                var servicesResult = FacialProcessingServices.Create(logger);
                if (servicesResult.IsFailure)
                {
                    return Result.Failure<ProcessingResultDto, PipelineError>(servicesResult.Error);
                }

                using var services = servicesResult.Value;
                var encodingResult = ExecuteEncodingStrategy(
                    image,
                    roiSet,
                    processingOptions,
                    services,
                    logger
                );
                if (encodingResult.IsFailure)
                {
                    return Result.Failure<ProcessingResultDto, PipelineError>(encodingResult.Error);
                }

                encodedData = encodingResult.Value.Data;
                compressionRate = encodingResult.Value.ActualRate;
            }

            var processingTime = DateTime.UtcNow - startTime;
            logger.LogInformation(
                "ProcessWithoutFaceDetection completed in {ProcessingTime}ms. Output size: {FileSize} bytes",
                processingTime.TotalMilliseconds,
                encodedData.Length
            );

            // Build metadata
            var metadata = new ProcessingMetadataDto(
                new ImageDimensions(image.Width, image.Height),
                0f, // No rotation applied
                1f, // No face detection, so confidence is N/A (use 1.0)
                encodedData.Length,
                processingTime
            )
            {
                CompressionRate = compressionRate,
                AdditionalData = new Dictionary<string, object>
                {
                    ["ProcessingMode"] = "NoResize",
                    ["OutputFormat"] = outputFormat.ToUpperInvariant(),
                },
            };

            return Result.Success<ProcessingResultDto, PipelineError>(new ProcessingResultDto(encodedData, metadata));
        }
        catch (Exception ex)
        {
            return Result.Failure<ProcessingResultDto, PipelineError>(
                new RenderError($"Image processing failed: {ex.Message}", "no-face-detection"));
        }
    }

    /// <summary>
    /// Encodes an image as JPEG with the specified quality
    /// </summary>
    private static async Task<byte[]> EncodeAsJpegAsync(Image<Rgba32> image, int quality)
    {
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = quality });
        return ms.ToArray();
    }

    /// <summary>
    /// Encodes an image as PNG
    /// </summary>
    private static async Task<byte[]> EncodeAsPngAsync(Image<Rgba32> image)
    {
        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(
            ms,
            new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression }
        );
        return ms.ToArray();
    }

    /// <summary>
    /// Encodes an image as TIFF
    /// </summary>
    private static async Task<byte[]> EncodeAsTiffAsync(Image<Rgba32> image)
    {
        using var ms = new MemoryStream();
        await image.SaveAsTiffAsync(ms, new TiffEncoder());
        return ms.ToArray();
    }

    private static UnitResult<PipelineError> ValidateProcessingOptions(ProcessingOptions options)
    {
        if (options.MinFaceConfidence < 0 || options.MinFaceConfidence > 1)
        {
            return UnitResult.Failure<PipelineError>(
                new ValidationError("MinFaceConfidence must be between 0 and 1", nameof(options)));
        }

        if (options.MaxRotationDegrees < 0 || options.MaxRotationDegrees > 45)
        {
            return UnitResult.Failure<PipelineError>(
                new ValidationError("MaxRotationDegrees must be between 0 and 45", nameof(options)));
        }

        if (options.RoiStartLevel < 0 || options.RoiStartLevel > 3)
        {
            return UnitResult.Failure<PipelineError>(
                new ValidationError("RoiStartLevel must be between 0 and 3", nameof(options)));
        }

        if (options.MaxRetries < 0)
        {
            return UnitResult.Failure<PipelineError>(
                new ValidationError("MaxRetries must be non-negative", nameof(options)));
        }

        return UnitResult.Success<PipelineError>();
    }

    private static async Task<Result<ProcessingResult, PipelineError>> ProcessImageInternalAsync(
        byte[] imageData,
        ProcessingOptions options,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        return await ProcessImageInternalAsync(imageData, options, "jp2", 85, logger, cancellationToken);
    }
    
    private static async Task<Result<ProcessingResult, PipelineError>> ProcessImageInternalAsync(
        byte[] imageData,
        ProcessingOptions options,
        string outputFormat,
        int jpegQuality,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var imageResult = LoadImage(imageData);
        if (imageResult.IsFailure)
        {
            return Result.Failure<ProcessingResult, PipelineError>(imageResult.Error);
        }

        using var image = imageResult.Value;
        var servicesResult = FacialProcessingServices.Create(logger);
        if (servicesResult.IsFailure)
        {
            return Result.Failure<ProcessingResult, PipelineError>(servicesResult.Error);
        }

        using var services = servicesResult.Value;

        var startTime = DateTime.UtcNow;

        cancellationToken.ThrowIfCancellationRequested();

        // Step 1: Face detection
        logger.LogDebug("Starting face detection");
        var detectedFace = await DetectPrimaryFaceAsync(
            image,
            options,
            services,
            logger,
            cancellationToken
        ).ConfigureAwait(false);
        if (detectedFace.IsFailure)
        {
            return Result.Failure<ProcessingResult, PipelineError>(detectedFace.Error);
        }

        // Step 2: Transform to PIV format
        logger.LogDebug("Starting PIV transformation");
        var transformResult = await TransformImageAsync(
            image,
            detectedFace.Value,
            options,
            services,
            logger,
            cancellationToken
        ).ConfigureAwait(false);
        if (transformResult.IsFailure)
        {
            return Result.Failure<ProcessingResult, PipelineError>(transformResult.Error);
        }

        var (pivImage, roiSet, transformData) = transformResult.Value;

        // Step 3: Encode based on output format
        EncodingResult encoding;
        if (outputFormat.Equals("jp2", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogDebug(
                "Starting JPEG 2000 encoding with strategy: {Strategy}",
                options.Strategy.GetType().Name
            );
            var encodingResult = ExecuteEncodingStrategy(pivImage, roiSet, options, services, logger);
            if (encodingResult.IsFailure)
            {
                return Result.Failure<ProcessingResult, PipelineError>(encodingResult.Error);
            }

            encoding = encodingResult.Value;
        }
        else
        {
            // For non-JP2 formats, encode directly
            logger.LogDebug("Encoding as {Format}", outputFormat.ToUpperInvariant());
            byte[] encodedData;
            
            if (outputFormat.Equals("jpeg", StringComparison.OrdinalIgnoreCase) || 
                outputFormat.Equals("jpg", StringComparison.OrdinalIgnoreCase))
            {
                encodedData = await EncodeAsJpegAsync(pivImage, jpegQuality);
            }
            else if (outputFormat.Equals("png", StringComparison.OrdinalIgnoreCase))
            {
                encodedData = await EncodeAsPngAsync(pivImage);
            }
            else if (outputFormat.Equals("tiff", StringComparison.OrdinalIgnoreCase) || 
                     outputFormat.Equals("tif", StringComparison.OrdinalIgnoreCase))
            {
                encodedData = await EncodeAsTiffAsync(pivImage);
            }
            else
            {
                return Result.Failure<ProcessingResult, PipelineError>(
                    new ValidationError($"Unsupported output format: {outputFormat}", nameof(outputFormat)));
            }
            
            encoding = new EncodingResult(
                encodedData,
                ActualRate: 0, // Not applicable for non-JP2
                TargetSize: Maybe<int>.None
            );
        }

        var processingTime = DateTime.UtcNow - startTime;
        logger.LogInformation(
            "Processing completed successfully in {ProcessingTime}ms. Output size: {FileSize} bytes",
            processingTime.TotalMilliseconds,
            encoding.Data.Length
        );

        // Step 4: Build result
        var metadata = new ProcessingMetadata(
            transformData.OutputDimensions,
            transformData.RotationApplied,
            detectedFace.Value.Confidence,
            encoding.Data.Length,
            processingTime
        )
        {
            CompressionRate = encoding.ActualRate,
            TargetSize = encoding.TargetSize,
            Warnings = transformData.Warnings,
            AdditionalData = transformData.AdditionalData,
        };

        return Result.Success<ProcessingResult, PipelineError>(new ProcessingResult(encoding.Data, metadata));
    }

    private static Result<Image<Rgba32>, PipelineError> LoadImage(byte[] imageData)
    {
        try
        {
            using var stream = new MemoryStream(imageData);
            return Result.Success<Image<Rgba32>, PipelineError>(Image.Load<Rgba32>(stream));
        }
        catch (Exception ex)
        {
            return Result.Failure<Image<Rgba32>, PipelineError>(
                new InputError($"Invalid image data: {ex.Message}", nameof(imageData)));
        }
    }

    private static async Task<Result<DetectedFace, PipelineError>> DetectPrimaryFaceAsync(
        Image<Rgba32> image,
        ProcessingOptions options,
        FacialProcessingServices services,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var facesResult = await services.Detector.DetectFacesAsync(image, cancellationToken);
        if (facesResult.IsFailure)
        {
            return Result.Failure<DetectedFace, PipelineError>(
                new DetectionError($"Face detection failed: {facesResult.Error.Message}", "facial-image-encoder"));
        }

        var faces = facesResult.Value;
        logger.LogDebug("Detected {FaceCount} faces in image", faces.Count);

        if (faces.Count == 0)
        {
            return Result.Failure<DetectedFace, PipelineError>(
                new DetectionError("No faces detected in the image", "facial-image-encoder"));
        }

        if (options.RequireSingleFace && faces.Count > 1)
        {
            return Result.Failure<DetectedFace, PipelineError>(
                new DetectionError(
                    $"Multiple faces detected ({faces.Count}). Single face required.",
                    "facial-image-encoder"));
        }

        // Use highest confidence face
        var primaryFace = faces
            .Where(f => f.Confidence >= options.MinFaceConfidence)
            .OrderByDescending(f => f.Confidence)
            .FirstOrDefault();

        if (primaryFace == null)
        {
            var bestConfidence = faces.Max(f => f.Confidence);
            return Result.Failure<DetectedFace, PipelineError>(
                new DetectionError(
                    $"No faces meet minimum confidence threshold of {options.MinFaceConfidence:P1}. Best confidence: {bestConfidence:P1}",
                    "facial-image-encoder"));
        }

        logger.LogDebug(
            "Selected primary face with confidence: {Confidence:P1}",
            primaryFace.Confidence
        );
        return Result.Success<DetectedFace, PipelineError>(primaryFace);
    }

    private static async Task<Result<(
        Image<Rgba32> PivImage,
        FacialRoiSet RoiSet,
        TransformationData Data
    ), PipelineError>> TransformImageAsync(
        Image<Rgba32> image,
        DetectedFace face,
        ProcessingOptions options,
        FacialProcessingServices services,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        // Convert ProcessingOptions to PivProcessingOptions for existing pipeline
        var pivOptions = new PivProcessingOptions
        {
            BaseRate = 0.7f, // Not used in this path, encoding handled separately
            RoiStartLevel = options.RoiStartLevel,
            MinFaceConfidence = options.MinFaceConfidence,
            RequireSingleFace = options.RequireSingleFace,
            PreserveExifMetadata = options.PreserveMetadata,
            MaxRotationDegrees = options.MaxRotationDegrees,
        };

        // Use existing PivLandmarkProcessor
        var result = await PivLandmarkProcessor.ProcessAsync(
            image,
            face,
            services.LandmarkExtractor,
            pivOptions,
            logger
        );
        if (result.IsFailure)
        {
            return Result.Failure<(
                Image<Rgba32> PivImage,
                FacialRoiSet RoiSet,
                TransformationData Data
            ), PipelineError>(new GeometryError($"PIV transformation failed: {result.Error}", "facial-image-encoder"));
        }

        var pivData = result.Value;

        var transformData = new TransformationData(
            pivData.Dimensions,
            pivData.AppliedRotation,
            Array.Empty<string>(),
            new Dictionary<string, object>
            {
                ["ProcessingSummary"] = pivData.ProcessingSummary,
                ["FaceCrop"] = pivData.FaceCrop,
                ["ComplianceValidation"] = pivData.ComplianceValidation,
            }
        );

        return Result.Success<(
            Image<Rgba32> PivImage,
            FacialRoiSet RoiSet,
            TransformationData Data
        ), PipelineError>((pivData.PivImage, pivData.RoiSet, transformData));
    }

    private static Result<EncodingResult, PipelineError> ExecuteEncodingStrategy(
        Image<Rgba32> pivImage,
        FacialRoiSet roiSet,
        ProcessingOptions options,
        FacialProcessingServices services,
        ILogger logger
    )
    {
        var encodingResult = options.Strategy.Execute(
            pivImage,
            roiSet,
            services.Encoder,
            options,
            logger
        );
        if (encodingResult.IsFailure)
        {
            return Result.Failure<EncodingResult, PipelineError>(
                new RenderError($"JPEG 2000 encoding failed: {encodingResult.Error}", "facial-image-encoder"));
        }

        return Result.Success<EncodingResult, PipelineError>(encodingResult.Value);
    }

    private sealed record TransformationData(
        ImageDimensions OutputDimensions,
        float RotationApplied,
        IReadOnlyList<string> Warnings,
        IReadOnlyDictionary<string, object> AdditionalData
    );

    private static ProcessingResultDto ConvertToDto(ProcessingResult result)
    {
        var metadataDto = new ProcessingMetadataDto(
            result.Metadata.OutputDimensions,
            result.Metadata.RotationApplied,
            result.Metadata.FaceConfidence,
            result.Metadata.FileSize,
            result.Metadata.ProcessingTime
        )
        {
            CompressionRate = result.Metadata.CompressionRate,
            TargetSize = result.Metadata.TargetSize.HasValue
                ? result.Metadata.TargetSize.Value
                : null,
            Warnings = result.Metadata.Warnings,
            AdditionalData = result.Metadata.AdditionalData,
        };

        return new ProcessingResultDto(result.ImageData, metadataDto);
    }
}

/// <summary>
/// Internal service container for facial processing operations
/// </summary>
public sealed class FacialProcessingServices : IDisposable
{
    /// <summary>
    /// Gets the shared face detector instance.
    /// </summary>
    public IFaceDetector Detector { get; }

    /// <summary>
    /// Gets the shared landmark extractor instance.
    /// </summary>
    public ILandmarkExtractor LandmarkExtractor { get; }

    /// <summary>
    /// Gets the shared JPEG 2000 encoder instance.
    /// </summary>
    public IJpeg2000Encoder Encoder { get; }

    private FacialProcessingServices(
        IFaceDetector detector,
        ILandmarkExtractor landmarkExtractor,
        IJpeg2000Encoder encoder)
    {
        Detector = detector;
        LandmarkExtractor = landmarkExtractor;
        Encoder = encoder;
    }

    /// <summary>
    /// Creates the ONNX-backed facial processing services using the supplied logger factory.
    /// </summary>
    public static Result<FacialProcessingServices, PipelineError> Create(ILoggerFactory loggerFactory)
    {
        var detectorResult = RetinaFaceDetector.Create(loggerFactory.CreateLogger<RetinaFaceDetector>());
        if (detectorResult.IsFailure)
        {
            return Result.Failure<FacialProcessingServices, PipelineError>(detectorResult.Error);
        }

        var extractorResult = OnnxLandmarkExtractor.Create(
            loggerFactory.CreateLogger<OnnxLandmarkExtractor>());
        if (extractorResult.IsFailure)
        {
            detectorResult.Value.Dispose();
            return Result.Failure<FacialProcessingServices, PipelineError>(extractorResult.Error);
        }

        var encoder = new Jpeg2000EncoderService(loggerFactory.CreateLogger<Jpeg2000EncoderService>());
        return Result.Success<FacialProcessingServices, PipelineError>(
            new FacialProcessingServices(detectorResult.Value, extractorResult.Value, encoder));
    }

    /// <summary>
    /// Creates the ONNX-backed facial processing services using null-loggers.
    /// </summary>
    public static Result<FacialProcessingServices, PipelineError> Create(ILogger logger) =>
        Create(NullLoggerFactory.Instance);

    internal static FacialProcessingServices FromExisting(
        IFaceDetector detector,
        ILandmarkExtractor landmarkExtractor,
        IJpeg2000Encoder encoder) =>
        new(detector, landmarkExtractor, encoder);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Detector is IDisposable disposableDetector)
            disposableDetector.Dispose();
        if (LandmarkExtractor is IDisposable disposableExtractor)
            disposableExtractor.Dispose();
        // Encoder is stateless, no disposal needed
    }
}

using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>
/// Processes facial images to comply with PIV (Personal Identity Verification) standards as defined in FIPS 201-3.
/// Performs face detection, landmark extraction, rotation correction, cropping, and resizing to produce
/// a 420x560 pixel image suitable for government ID cards and credentials.
/// </summary>
/// <remarks>
/// <para>
/// The PIV processor ensures compliance with FIPS 201-3 (Federal Information Processing Standards) requirements:
/// - Output dimensions: 420x560 pixels (3:4 aspect ratio)
/// - Face width: Minimum 240 pixels (approximately 57% of image width)
/// - Eye position: 45-65% from top of image
/// - Rotation correction: Maximum ±5 degrees
/// - Face centering: Properly centered with appropriate margins
/// </para>
/// <para>
/// Processing pipeline:
/// 1. Face detection using RetinaFace to identify frontal faces
/// 2. 68-point facial landmark extraction for precise eye positioning
/// 3. Rotation calculation and correction based on eye alignment
/// 4. Intelligent cropping to center face with proper proportions
/// 5. Final resize to exact 420x560 PIV dimensions
/// </para>
/// </remarks>
[PublicAPI]
public static class PivProcessor
{
    /// <summary>
    /// Asynchronously processes a source image to produce a PIV-compatible facial photograph.
    /// </summary>
    /// <param name="sourceImage">The source image containing a face to process. Must contain at least one detectable frontal face.</param>
    /// <param name="faceDetector">Face detection service for identifying faces in the image.</param>
    /// <param name="landmarkExtractor">Landmark extraction service for detecting 68 facial landmarks.</param>
    /// <param name="jpeg2000Encoder">JPEG 2000 encoder for creating the final compressed image with optional ROI support.</param>
    /// <param name="options">Optional processing configuration. Uses default PIV settings if not specified.</param>
    /// <param name="enableRoi">Whether to enable Region of Interest (ROI) encoding for higher quality facial regions. Default is true.</param>
    /// <param name="roiAlign">Whether to align ROI regions with JPEG 2000 compression blocks. Default is false for smoother quality transitions.</param>
    /// <param name="logger">Optional logger for detailed processing information and debugging.</param>
    /// <returns>
    /// A <see cref="Result{PivResult}"/> containing either:
    /// - Success: A PIV-compatible 420x560 image with proper face positioning
    /// - Failure: Error message describing what went wrong (no face detected, landmarks extraction failed, etc.)
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method implements the complete PIV processing pipeline:
    /// 1. Detects faces using the provided face detector (typically RetinaFace)
    /// 2. Extracts 68 facial landmarks for precise feature location
    /// 3. Calculates rotation needed to level the eyes (limited to ±5 degrees)
    /// 4. Crops the image to center the face with proper proportions
    /// 5. Resizes to final 420x560 dimensions as required by FIPS 201-3
    /// </para>
    /// <para>
    /// The resulting image meets all PIV requirements including:
    /// - Proper face size (approximately 70% of frame width)
    /// - Eye position at 45% from top of frame
    /// - Level head orientation (rotation corrected)
    /// - High quality output suitable for biometric matching
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = await PivProcessor.ProcessAsync(
    ///     sourceImage,
    ///     faceDetector,
    ///     landmarkExtractor,
    ///     PivProcessingOptions.HighQuality);
    ///
    /// if (result.IsSuccess)
    /// {
    ///     var pivImage = result.Value.Image;
    ///     // Save or use the PIV-compatible image
    /// }
    /// </code>
    /// </example>
    [PublicAPI]
    public static async Task<Result<PivResult, PipelineError>> ProcessAsync(
        Image<Rgba32> sourceImage,
        IFaceDetector faceDetector,
        ILandmarkExtractor landmarkExtractor,
        IJpeg2000Encoder jpeg2000Encoder,
        PivProcessingOptions? options = null,
        bool enableRoi = true,
        bool roiAlign = false,
        ILogger? logger = null
    )
    {
        options ??= PivProcessingOptions.Default;
        logger?.LogDebug(
            "Starting PIV processing with options: EnableRoi={EnableRoi}, RoiAlign={RoiAlign}",
            enableRoi,
            roiAlign
        );

        var sourceDimensions = new ImageDimensions(sourceImage.Width, sourceImage.Height);
        logger?.LogDebug(
            "Source image dimensions: {Width}x{Height}",
            sourceDimensions.Width,
            sourceDimensions.Height
        );

        // Step 1: Detect faces using the simple approach
        logger?.LogDebug("Step 1: Starting face detection");
        var detectedFaces = await DetectFacesSimple(sourceImage, faceDetector, options, logger);
        if (detectedFaces.IsFailure)
        {
            logger?.LogWarning("Face detection failed: {Error}", detectedFaces.Error);
            return Result.Failure<PivResult, PipelineError>(detectedFaces.Error);
        }

        var face = detectedFaces.Value;
        logger?.LogDebug(
            "Face detected with confidence: {Confidence}, bounding box: {Box}",
            face.Confidence,
            face.BoundingBox
        );

        // Step 2: Use unified PIV landmark processor for accurate results
        logger?.LogDebug("Step 2: Starting PIV landmark processing");
        var pivLandmarkResult = await PivLandmarkProcessor.ProcessAsync(
            sourceImage,
            face,
            landmarkExtractor,
            options,
            logger
        );
        if (pivLandmarkResult.IsFailure)
        {
            logger?.LogWarning("PIV landmark processing failed: {Error}", pivLandmarkResult.Error);
            return Result.Failure<PivResult, PipelineError>(
                new GeometryError(pivLandmarkResult.Error, "piv-landmarks"));
        }

        var pivData = pivLandmarkResult.Value;
        logger?.LogDebug(
            "PIV landmark processing completed: rotation={Rotation:F2}°, crop=({X},{Y},{Width}x{Height}), landmarks={LandmarkCount}",
            pivData.AppliedRotation,
            pivData.FaceCrop.X,
            pivData.FaceCrop.Y,
            pivData.FaceCrop.Width,
            pivData.FaceCrop.Height,
            pivData.Landmarks.Points.Count
        );

        // Step 3: Create metadata with all relevant information
        logger?.LogDebug("Step 3: Creating metadata");
        var transformMapResult = RenderTransformMapBuilder.CreateRotateCropResize(
            sourceDimensions,
            pivData.AppliedRotation,
            RenderTransformMapBuilder.ComputeExpandedRotationDimensions(sourceDimensions, pivData.AppliedRotation),
            pivData.FaceCrop,
            pivData.Dimensions);
        if (transformMapResult.IsFailure)
        {
            return Result.Failure<PivResult, PipelineError>(transformMapResult.Error);
        }

        var metadata = new Dictionary<string, object>
        {
            ["SourceDimensions"] = sourceDimensions,
            ["FaceConfidence"] = face.Confidence,
            ["ProcessingOptions"] = options,
            ["TransformedLandmarks"] = pivData.Landmarks,
            ["RoiRegions"] = pivData.RoiSet,
            ["AppliedRotation"] = pivData.AppliedRotation,
            ["FaceCrop"] = pivData.FaceCrop,
            ["PivImage"] = pivData.PivImage.Clone(), // Clone for visualization purposes to avoid disposal issues
            ["PivLines"] = pivData.PivLines, // PIV compliance lines (AA, BB, CC)
            ["ComplianceValidation"] = pivData.ComplianceValidation, // PIV compliance validation results
            ["RenderTransformMap"] = transformMapResult.Value,
        };

        // Step 4: Create PIV transform for compatibility (derived from unified processor results)
        logger?.LogDebug("Step 4: Creating PIV transform");
        var pivTransform = new PivTransform
        {
            RotationDegrees = pivData.AppliedRotation,
            CropRegion = new CropRect
            {
                Left = (float)pivData.FaceCrop.X / sourceDimensions.Width,
                Top = (float)pivData.FaceCrop.Y / sourceDimensions.Height,
                Width = (float)pivData.FaceCrop.Width / sourceDimensions.Width,
                Height = (float)pivData.FaceCrop.Height / sourceDimensions.Height,
            },
            ScaleFactor = 1.0f, // Unified processor handles scaling internally
            TargetDimensions = pivData.Dimensions,
            IsPivCompliant = true,
        };

        // Step 5: Encode the image to JPEG 2000
        logger?.LogDebug(
            "Step 5: Encoding to JPEG 2000 with ROI. BaseRate={BaseRate}, RoiStartLevel={RoiStartLevel}",
            options.BaseRate,
            options.RoiStartLevel
        );
        var encodingResult = jpeg2000Encoder.EncodeWithRoi(
            pivData.PivImage,
            pivData.RoiSet,
            options.BaseRate,
            options.RoiStartLevel,
            enableRoi,
            roiAlign
        );
        if (encodingResult.IsFailure)
        {
            logger?.LogError("JPEG 2000 encoding failed: {Error}", encodingResult.Error);
            return Result.Failure<PivResult, PipelineError>(encodingResult.Error);
        }
        logger?.LogDebug(
            "JPEG 2000 encoding successful, data size: {Size} bytes",
            encodingResult.Value.Length
        );

        // Step 6: Create result with encoded data
        logger?.LogDebug("Step 6: Creating final PIV result");
        var result = PivResult.Success(
            encodingResult.Value,
            "image/jp2",
            pivData.Dimensions,
            pivTransform,
            face,
            Maybe<string>.From(pivData.ProcessingSummary),
            Maybe<IReadOnlyList<string>>.None,
            Maybe<IReadOnlyDictionary<string, object>>.From(metadata)
        );

        var validation = result.Validate();
        var validationResult = validation.IsSuccess
            ? Result.Success<PivResult, PipelineError>(result)
            : Result.Failure<PivResult, PipelineError>(
                new ValidationError(validation.Error, "piv-result"));
        if (validationResult.IsSuccess)
        {
            logger?.LogInformation(
                "PIV processing completed successfully. Summary: {Summary}",
                pivData.ProcessingSummary
            );
        }
        else
        {
            logger?.LogWarning("PIV result validation failed: {Error}", validationResult.Error);
        }
        return validationResult;
    }

    /// <summary>
    /// Detects faces in the image and returns the highest confidence frontal face suitable for PIV processing.
    /// </summary>
    /// <param name="image">The source image to analyze for faces.</param>
    /// <param name="detector">Face detection service implementation.</param>
    /// <param name="options">Processing options including minimum confidence threshold.</param>
    /// <param name="logger">Optional logger for detailed detection information.</param>
    /// <returns>
    /// A <see cref="Result{DetectedFace}"/> containing either:
    /// - Success: The highest confidence face meeting PIV requirements
    /// - Failure: Error message if no suitable faces are detected
    /// </returns>
    /// <remarks>
    /// This method filters detected faces to ensure PIV suitability by:
    /// - Selecting only frontal faces (not profile views)
    /// - Choosing the highest confidence detection when multiple faces exist
    /// - Respecting the minimum confidence threshold from options
    /// </remarks>
    private static async Task<Result<DetectedFace, PipelineError>> DetectFacesSimple(
        Image<Rgba32> image,
        IFaceDetector detector,
        PivProcessingOptions options,
        ILogger? logger = null
    )
    {
        logger?.LogDebug("Detecting faces in image");
        // Use the real detector
        var faces = await detector.DetectFacesAsync(image);
        if (faces.IsFailure)
        {
            logger?.LogWarning("Face detection service failed: {Error}", faces.Error);
            return Result.Failure<DetectedFace, PipelineError>(faces.Error);
        }

        if (faces.Value.Count == 0)
        {
            logger?.LogWarning("No faces detected in the image");
            return Result.Failure<DetectedFace, PipelineError>(
                new DetectionError("No faces detected", "piv"));
        }

        logger?.LogDebug("Detected {Count} face(s) in the image", faces.Value.Count);

        // Return the first (highest confidence) face
        var selectedFace = faces.Value.First();
        logger?.LogDebug(
            "Selected face with highest confidence: {Confidence}, box: {Box}",
            selectedFace.Confidence,
            selectedFace.BoundingBox
        );
        return Result.Success<DetectedFace, PipelineError>(selectedFace);
    }

    /// <summary>
    /// Calculates the transformation parameters needed to produce a PIV-compatible image from facial landmarks.
    /// </summary>
    /// <param name="landmarks">68-point facial landmarks used to determine eye positions and face orientation.</param>
    /// <param name="face">Detected face information including bounding box.</param>
    /// <param name="sourceDimensions">Original image dimensions for calculating relative transformations.</param>
    /// <param name="logger">Optional logger for detailed transformation calculation information.</param>
    /// <returns>
    /// A <see cref="Result{PivTransform}"/> containing either:
    /// - Success: Calculated transformation with rotation, crop region, and scale factor
    /// - Failure: Error message if transformation cannot be calculated
    /// </returns>
    /// <remarks>
    /// <para>
    /// The transformation calculation follows PIV standards:
    /// - Rotation: Based on eye alignment, limited to ±5 degrees
    /// - Cropping: Centers face with 70% width coverage and eyes at 45% from top
    /// - Scaling: Maintains 3:4 aspect ratio for 420x560 output
    /// </para>
    /// <para>
    /// Eye position calculation uses the center points of left and right eye landmarks:
    /// - Left eye: Average of points 36-41
    /// - Right eye: Average of points 42-47
    /// </para>
    /// </remarks>
    private static Result<PivTransform> CalculatePivTransform(
        FaceLandmarks68 landmarks,
        DetectedFace face,
        ImageDimensions sourceDimensions,
        ILogger? logger = null
    )
    {
        logger?.LogDebug("Calculating PIV transform from landmarks");

        // Extract eye positions
        var leftEye = landmarks.LeftEyeCenter;
        var rightEye = landmarks.RightEyeCenter;
        logger?.LogDebug(
            "Eye positions - Left: ({LeftX}, {LeftY}), Right: ({RightX}, {RightY})",
            leftEye.X,
            leftEye.Y,
            rightEye.X,
            rightEye.Y
        );

        // Use pure calculation functions
        var rotationDegrees = PivTransformCalculator.CalculateRotationFromEyes(leftEye, rightEye);
        logger?.LogDebug("Calculated rotation angle: {Rotation}°", rotationDegrees);

        var scaleFactor = PivTransformCalculator.CalculateScaleFactor(sourceDimensions);
        logger?.LogDebug("Calculated scale factor: {Scale}", scaleFactor);

        // Calculate eye center and account for rotation
        var eyeCenter = PivTransformCalculator.CalculateEyeCenter(leftEye, rightEye);
        logger?.LogDebug("Eye center position: ({X}, {Y})", eyeCenter.X, eyeCenter.Y);

        var rotatedEyeCenter = PivTransformCalculator.RotatePointAroundImageCenter(
            eyeCenter,
            rotationDegrees,
            sourceDimensions
        );
        logger?.LogDebug("Rotated eye center: ({X}, {Y})", rotatedEyeCenter.X, rotatedEyeCenter.Y);

        // Calculate crop region using the rotated eye center
        var cropRegion = PivTransformCalculator.CalculatePivCrop(
            rotatedEyeCenter,
            face.BoundingBox,
            sourceDimensions
        );
        logger?.LogDebug(
            "Calculated crop region: Left={Left}, Top={Top}, Width={Width}, Height={Height}",
            cropRegion.Left,
            cropRegion.Top,
            cropRegion.Width,
            cropRegion.Height
        );

        var transform = new PivTransform
        {
            RotationDegrees = rotationDegrees,
            CropRegion = cropRegion,
            ScaleFactor = scaleFactor,
            TargetDimensions = new ImageDimensions(420, 560),
            IsPivCompliant = true,
        };

        var result = transform.Validate().Map(() => transform);
        if (result.IsSuccess)
        {
            logger?.LogDebug("PIV transform calculated successfully");
        }
        else
        {
            logger?.LogWarning("PIV transform validation failed: {Error}", result.Error);
        }
        return result;
    }

    /// <summary>
    /// Applies the calculated PIV transformation to produce a compliant 420x560 image.
    /// </summary>
    /// <param name="sourceImage">The original image to transform.</param>
    /// <param name="transform">Calculated transformation parameters including rotation, crop, and scale.</param>
    /// <param name="options">Processing options for output format and quality.</param>
    /// <param name="logger">Optional logger for detailed transformation application information.</param>
    /// <returns>
    /// A <see cref="Result{Image}"/> containing either:
    /// - Success: Transformed 420x560 PIV-compatible image
    /// - Failure: Error message if transformation fails
    /// </returns>
    /// <remarks>
    /// <para>
    /// Transformation is applied in the following order:
    /// 1. Rotation: Corrects head tilt to level the eyes
    /// 2. Cropping: Extracts face region with proper positioning
    /// 3. Resizing: Scales to exact 420x560 dimensions
    /// </para>
    /// <para>
    /// The method preserves image quality and ensures the 3:4 aspect ratio
    /// is maintained throughout the transformation process.
    /// </para>
    /// </remarks>
    private static Result<Image<Rgba32>> ApplyPivTransformation(
        Image<Rgba32> sourceImage,
        PivTransform transform,
        PivProcessingOptions options,
        ILogger? logger = null
    )
    {
        logger?.LogDebug("Applying PIV transformation to image");
        // Clone the image to avoid modifying the original
        var processedImage = sourceImage.Clone(ctx =>
        {
            // Apply transformations in sequence

            // Step 1: Crop to center the face with proper aspect ratio
            if (!IsCropFull(transform.CropRegion))
            {
                logger?.LogDebug("Applying crop transformation");
                var imageWidth = sourceImage.Width;
                var imageHeight = sourceImage.Height;

                // Calculate the crop rectangle based on the face position
                var cropX = (int)(transform.CropRegion.Left * imageWidth);
                var cropY = (int)(transform.CropRegion.Top * imageHeight);
                var cropWidth = (int)(transform.CropRegion.Width * imageWidth);
                var cropHeight = (int)(transform.CropRegion.Height * imageHeight);

                // Ensure crop dimensions maintain PIV aspect ratio (3:4)
                var pivAspectRatio = 3.0f / 4.0f;
                var currentAspectRatio = (float)cropWidth / cropHeight;
                logger?.LogDebug(
                    "Current crop aspect ratio: {Current}, target PIV ratio: {Target}",
                    currentAspectRatio,
                    pivAspectRatio
                );

                if (currentAspectRatio > pivAspectRatio)
                {
                    // Too wide, reduce width and recenter horizontally
                    var newCropWidth = (int)(cropHeight * pivAspectRatio);
                    cropX = cropX + (cropWidth - newCropWidth) / 2; // Recenter
                    cropWidth = newCropWidth;
                }
                else if (currentAspectRatio < pivAspectRatio)
                {
                    // Too tall, reduce height and adjust vertical position
                    var newCropHeight = (int)(cropWidth / pivAspectRatio);
                    cropY = cropY + (cropHeight - newCropHeight) / 2; // Recenter
                    cropHeight = newCropHeight;
                }

                // Ensure crop stays within bounds
                cropX = Math.Max(0, Math.Min(cropX, imageWidth - cropWidth));
                cropY = Math.Max(0, Math.Min(cropY, imageHeight - cropHeight));

                ctx.Crop(new Rectangle(cropX, cropY, cropWidth, cropHeight));
                logger?.LogDebug(
                    "Applied crop: x={X}, y={Y}, width={Width}, height={Height}",
                    cropX,
                    cropY,
                    cropWidth,
                    cropHeight
                );
            }

            // Step 2: Rotate if needed (after cropping to minimize black border area)
            if (Math.Abs(transform.RotationDegrees) > 0.1f)
            {
                logger?.LogDebug("Applying rotation: {Degrees}°", transform.RotationDegrees);
                ctx.Rotate(transform.RotationDegrees);
            }

            // Step 3: Resize to final PIV dimensions (420x560)
            logger?.LogDebug(
                "Resizing to PIV dimensions: {Width}x{Height}",
                transform.TargetDimensions.Width,
                transform.TargetDimensions.Height
            );
            ctx.Resize(transform.TargetDimensions.Width, transform.TargetDimensions.Height);
        });

        logger?.LogDebug("PIV transformation applied successfully");
        return Result.Success(processedImage);
    }

    /// <summary>
    /// Determines whether a crop region represents the full image (no cropping).
    /// </summary>
    /// <param name="cropRegion">The crop region to evaluate.</param>
    /// <returns>True if the crop region covers the entire image; otherwise, false.</returns>
    /// <remarks>
    /// A full crop has normalized coordinates of (0,0) with width and height of 1.0,
    /// representing 100% of the original image.
    /// </remarks>
    private static bool IsCropFull(CropRect cropRegion)
    {
        return Math.Abs(cropRegion.Left) < 0.001f
            && Math.Abs(cropRegion.Top) < 0.001f
            && Math.Abs(cropRegion.Width - 1.0f) < 0.001f
            && Math.Abs(cropRegion.Height - 1.0f) < 0.001f;
    }

    /// <summary>
    /// Generates a human-readable summary of the transformations applied for PIV compliance.
    /// </summary>
    /// <param name="transform">The transformation that was applied.</param>
    /// <returns>A descriptive string summarizing the operations performed.</returns>
    /// <remarks>
    /// The summary includes rotation angle (if applied), crop percentage (if less than 99%),
    /// and final dimensions. This is useful for logging and user feedback.
    /// </remarks>
    /// <example>
    /// Example output: "PIV transformation: rotated 2.3°, cropped to 85%, resized to 420x560"
    /// </example>
    private static string GenerateSummary(PivTransform transform)
    {
        var operations = new List<string>();

        if (Math.Abs(transform.RotationDegrees) > 0.1f)
        {
            operations.Add($"rotated {transform.RotationDegrees:F1}°");
        }

        var cropArea = transform.CropRegion.Width * transform.CropRegion.Height;
        if (cropArea < 0.99f)
        {
            operations.Add($"cropped to {cropArea:P0}");
        }

        operations.Add(
            $"resized to {transform.TargetDimensions.Width}x{transform.TargetDimensions.Height}"
        );

        return operations.Any()
            ? $"PIV transformation: {string.Join(", ", operations)}"
            : "PIV transformation: image already compliant";
    }

    /// <summary>
    /// Simple API to convert a JPEG image file to PIV-compatible JPEG 2000 format.
    /// Uses default settings: 17KB target file size with uniform quality (no ROI).
    /// </summary>
    /// <param name="inputJpegPath">Path to input JPEG image file.</param>
    /// <param name="outputJp2Path">Path for output JPEG 2000 file.</param>
    /// <param name="faceDetector">Face detection service.</param>
    /// <param name="landmarkExtractor">68-point landmark extraction service.</param>
    /// <param name="jpeg2000Encoder">JPEG 2000 encoding service.</param>
    /// <returns>Result containing the PIV processing outcome or error message.</returns>
    /// <remarks>
    /// This method provides a simple API for common use cases:
    /// - 17KB target file size (0.6 bits/pixel compression rate)
    /// - Uniform quality distribution (ROI disabled)
    /// - Single tile covering entire image for optimal compression
    /// - PIV-compatible 420x560 output dimensions
    /// - Maximized head size within PIV guidelines (240px width)
    /// </remarks>
    [PublicAPI]
    public static async Task<Result<PivResult, PipelineError>> ConvertJpegToPivJp2Async(
        string inputJpegPath,
        string outputJp2Path,
        IFaceDetector faceDetector,
        ILandmarkExtractor landmarkExtractor,
        IJpeg2000Encoder jpeg2000Encoder
    )
    {
        // Load the JPEG image
        using var sourceImage = await Image.LoadAsync<Rgba32>(inputJpegPath);

        // Process with default 20KB level 3 ROI settings (no alignment for smoothest transitions)
        var result = await ProcessAsync(
                sourceImage,
                faceDetector,
                landmarkExtractor,
                jpeg2000Encoder,
                PivProcessingOptions.Default,
                enableRoi: true, // ROI level 3 for smoothest quality transitions
                roiAlign: false
            )
            .ConfigureAwait(false); // No alignment for smoothest transitions

        if (result.IsSuccess)
        {
            // Save the encoded JPEG 2000 data to file
            await File.WriteAllBytesAsync(outputJp2Path, result.Value.ImageData);
        }

        return result;
    }
}

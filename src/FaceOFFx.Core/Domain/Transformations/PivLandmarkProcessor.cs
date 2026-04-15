using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Standards;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>
/// Processor that creates PIV-compatible images with accurate facial landmarks.
/// Extracts landmarks from the original face region (what the model expects) then
/// transforms both the image and landmarks to PIV format.
/// </summary>
public static class PivLandmarkProcessor
{
    /// <summary>
    /// Processes a face to produce both a PIV-compatible image and accurate landmarks.
    /// </summary>
    public static async Task<Result<PivLandmarkResult>> ProcessAsync(
        Image<Rgba32> sourceImage,
        DetectedFace detectedFace,
        ILandmarkExtractor landmarkExtractor,
        PivProcessingOptions options,
        ILogger? logger = null
    )
    {
        logger?.LogDebug(
            "Starting PIV landmark processing for face: {Face}",
            detectedFace.BoundingBox
        );
        // Step 1: Build canonical face geometry using the coarse chip plus one fine solve.
        logger?.LogDebug("Step 1: Building canonical face geometry from normalized chip");
        var geometryResult = await CanonicalFaceGeometryPipeline
            .ExtractAsync(sourceImage, detectedFace, landmarkExtractor)
            .ConfigureAwait(false);
        if (geometryResult.IsFailure)
        {
            logger?.LogWarning("Canonical face geometry failed: {Error}", geometryResult.Error);
            return Result.Failure<PivLandmarkResult>(
                $"Canonical face geometry failed: {geometryResult.Error}"
            );
        }

        return ProcessAsync(sourceImage, geometryResult.Value, options, logger);
    }

    /// <summary>
    /// Processes a PIV portrait from canonical original-space face geometry without performing any additional landmark inference.
    /// </summary>
    public static Result<PivLandmarkResult> ProcessAsync(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        PivProcessingOptions options,
        ILogger? logger = null)
    {
        var originalLandmarks = geometry.SourceLandmarks;
        logger?.LogDebug(
            "Successfully extracted {Count} landmarks",
            originalLandmarks.Points.Count
        );

        // Step 2: Calculate rotation from landmarks
        logger?.LogDebug("Step 2: Calculating rotation from landmarks");
        var rotationDegrees = CalculateRotationFromLandmarks(
            originalLandmarks,
            options.MaxRotationDegrees,
            logger
        );
        logger?.LogDebug("Calculated rotation: {Rotation}°", rotationDegrees);

        // Step 3: Transform landmarks to account for rotation
        logger?.LogDebug("Step 3: Rotating landmarks to match rotated image");
        var rotatedLandmarks = RotateLandmarks(
            originalLandmarks,
            rotationDegrees,
            sourceImage.Width,
            sourceImage.Height,
            logger
        );

        // Step 4: Rotate the entire source image FIRST (no black borders!)
        logger?.LogDebug("Step 4: Rotating source image");
        Image<Rgba32> rotatedImage;
        if (Math.Abs(rotationDegrees) > 0.1f)
        {
            logger?.LogDebug("Applying rotation of {Degrees}° to image", rotationDegrees);
            rotatedImage = sourceImage.Clone(ctx => ctx.Rotate(rotationDegrees));
        }
        else
        {
            logger?.LogDebug("Rotation angle too small, cloning image without rotation");
            rotatedImage = sourceImage.Clone();
        }

        // Step 5: Calculate face crop region based on rotated landmarks (not face detection box)
        logger?.LogDebug("Step 5: Calculating face crop region from rotated landmarks");
        var (_, isFailure, faceCrop, error) = CalculateFaceCrop(
            rotatedLandmarks,
            rotatedImage.Width,
            rotatedImage.Height,
            options,
            logger
        );
        if (isFailure)
        {
            logger?.LogWarning("Face crop calculation failed: {Error}", error);
            rotatedImage.Dispose();
            return Result.Failure<PivLandmarkResult>($"Face crop calculation failed: {error}");
        }

        logger?.LogDebug(
            "Face crop region: x={X}, y={Y}, width={Width}, height={Height}",
            faceCrop.X,
            faceCrop.Y,
            faceCrop.Width,
            faceCrop.Height
        );

        // Step 6: Crop and resize to PIV dimensions
        logger?.LogDebug("Step 6: Cropping and resizing to PIV dimensions (420x560)");
        var pivImage = rotatedImage.Clone(ctx =>
        {
            ctx.Crop(new Rectangle(faceCrop.X, faceCrop.Y, faceCrop.Width, faceCrop.Height));
            ctx.Resize(
                new ResizeOptions
                {
                    Size = new Size(420, 560),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center,
                }
            );
        });

        rotatedImage.Dispose();

        // Step 7: Transform landmarks to match the PIV image transformations
        logger?.LogDebug("Step 7: Transforming landmarks to PIV coordinate space");
        var pivLandmarks = TransformLandmarksToPivSpace(
            rotatedLandmarks,
            faceCrop,
            pivImage.Width,
            pivImage.Height,
            logger
        );

        // Step 8: Calculate PIV compliance lines and validation
        logger?.LogDebug("Step 8: Calculating PIV compliance lines and validation");
        var pivLines = pivLandmarks.CalculatePivLines();
        var complianceValidation = PivComplianceValidation.Validate(
            pivLines,
            pivImage.Width,
            pivImage.Height
        );

        logger?.LogDebug(
            "PIV Lines calculated - AA: {AA:F1}, BB: {BB:F1}, CC: {CC:F1}",
            pivLines.LineAA_X,
            pivLines.LineBB_Y,
            pivLines.LineCC_Width
        );
        logger?.LogInformation("PIV Compliance: {Status}", complianceValidation.Summary);

        if (!complianceValidation.IsFullyCompliant)
        {
            logger?.LogWarning("PIV compliance issues detected:");
            foreach (var issue in complianceValidation.Issues)
            {
                logger?.LogWarning("  - {Issue}", issue);
            }
            foreach (var rec in complianceValidation.Recommendations)
            {
                logger?.LogDebug("  Recommendation: {Recommendation}", rec);
            }
        }

        // Step 9: Calculate ROI Inner Region using Appendix C.6 approach
        logger?.LogDebug("Step 9: Calculating Appendix C.6 ROI Inner Region");
        var roiResult = StandardRoiIncits385(logger);
        if (roiResult.IsFailure)
        {
            logger?.LogWarning("ROI calculation failed: {Error}", roiResult.Error);
            return Result.Failure<PivLandmarkResult>($"ROI calculation failed: {roiResult.Error}");
        }

        var roiSet = roiResult.Value;
        logger?.LogDebug("Successfully calculated ROI Inner Region");

        // Step 10: Create result with PIV compliance information
        var result = new PivLandmarkResult(
            pivImage,
            pivLandmarks,
            roiSet,
            rotationDegrees,
            faceCrop,
            pivLines,
            complianceValidation
        );

        logger?.LogInformation(
            "PIV landmark processing completed. Summary: {Summary}",
            result.ProcessingSummary
        );
        return Result.Success(result);
    }

    /// <summary>
    /// Calculates rotation angle from 68-point landmarks using eye positions.
    /// </summary>
    private static float CalculateRotationFromLandmarks(
        FaceLandmarks68 landmarks,
        float maxRotationDegrees,
        ILogger? logger = null
    )
    {
        var leftEye = landmarks.LeftEyeCenter;
        var rightEye = landmarks.RightEyeCenter;
        logger?.LogDebug(
            "Eye centers - Left: ({LeftX}, {LeftY}), Right: ({RightX}, {RightY})",
            leftEye.X,
            leftEye.Y,
            rightEye.X,
            rightEye.Y
        );

        var deltaY = rightEye.Y - leftEye.Y;
        var deltaX = rightEye.X - leftEye.X;

        // Calculate rotation to level eyes (negative to correct clockwise tilt)
        var rotationDegrees = -(float)(Math.Atan2(deltaY, deltaX) * 180 / Math.PI);
        logger?.LogDebug("Calculated raw rotation: {Rotation}°", rotationDegrees);

        // Limit rotation to configured maximum for PIV compliance
        var limitedRotation = Math.Max(
            -maxRotationDegrees,
            Math.Min(maxRotationDegrees, rotationDegrees)
        );
        if (Math.Abs(limitedRotation - rotationDegrees) > 0.01f)
        {
            logger?.LogDebug(
                "Rotation limited from {Original}° to {Limited}° (max: ±{Max}°)",
                rotationDegrees,
                limitedRotation,
                maxRotationDegrees
            );
        }
        return limitedRotation;
    }

    /// <summary>
    /// Rotates landmarks to match the rotated image space.
    /// </summary>
    private static FaceLandmarks68 RotateLandmarks(
        FaceLandmarks68 landmarks,
        float rotationDegrees,
        int imageWidth,
        int imageHeight,
        ILogger? logger = null
    )
    {
        // If no rotation, return original landmarks
        if (Math.Abs(rotationDegrees) <= 0.1f)
        {
            logger?.LogDebug("No rotation needed, returning original landmarks");
            return landmarks;
        }
        logger?.LogDebug(
            "Rotating {Count} landmarks by {Degrees}°",
            landmarks.Points.Count,
            rotationDegrees
        );

        var angle = rotationDegrees * Math.PI / 180;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);

        // Original image center
        var oldCenterX = imageWidth / 2.0;
        var oldCenterY = imageHeight / 2.0;

        // Rotated image dimensions (same calculation as ImageSharp uses)
        var newWidth = (int)Math.Ceiling(Math.Abs(imageWidth * cos) + Math.Abs(imageHeight * sin));
        var newHeight = (int)Math.Ceiling(Math.Abs(imageWidth * sin) + Math.Abs(imageHeight * cos));
        var newCenterX = newWidth / 2.0;
        var newCenterY = newHeight / 2.0;
        logger?.LogDebug(
            "Image dimensions after rotation: {OldWidth}x{OldHeight} -> {NewWidth}x{NewHeight}",
            imageWidth,
            imageHeight,
            newWidth,
            newHeight
        );

        return FaceGeometryTransformations.RotateLandmarks(
            landmarks,
            rotationDegrees,
            new ImageDimensions(imageWidth, imageHeight));
    }

    /// <summary>
    /// Calculates the face crop region based on PIV compliance lines for proper positioning.
    /// Uses PIV lines AA, BB, CC to ensure compliance with INCITS 385-2004 Section 8.
    /// </summary>
    private static Result<Rectangle> CalculateFaceCrop(
        FaceLandmarks68 landmarks,
        int imageWidth,
        int imageHeight,
        PivProcessingOptions options,
        ILogger? logger = null
    )
    {
        logger?.LogDebug(
            "Starting PIV-compatible face crop calculation with image dimensions: {Width}x{Height}",
            imageWidth,
            imageHeight
        );

        if (!landmarks.IsValid)
        {
            logger?.LogWarning("Invalid landmarks provided for crop calculation");
            return Result.Failure<Rectangle>("Invalid landmarks for crop calculation");
        }

        // Step 1: Calculate PIV compliance lines
        var pivLines = landmarks.CalculatePivLines();
        logger?.LogDebug(
            "PIV Lines - AA (center): {AA:F1}, BB (eyes): {BB:F1}, CC (width): {CC:F1}",
            pivLines.LineAA_X,
            pivLines.LineBB_Y,
            pivLines.LineCC_Width
        );

        // Step 2: Target dimensions for final PIV image (420x560)
        const int finalWidth = PivConstants.Width; // 420
        const int finalHeight = PivConstants.Height; // 560

        // Step 3: Calculate target head width per INCITS 385-2004 B.2.1
        // "Image Width : Head Width ratio should be between 7:4 and 2:1"
        // For 420px image width:
        // - Minimum ratio 7:4 (1.75): head width ≤ 420/1.75 = 240px
        // - Maximum ratio 2:1 (2.0): head width ≥ 420/2.0 = 210px
        // Valid range: 210px ≤ head width ≤ 240px

        var minHeadWidth = finalWidth / 2.0f; // 210px (2:1 ratio)
        var maxHeadWidth = finalWidth * 4.0f / 7.0f; // 240px (7:4 ratio)

        // Target slightly below maximum to avoid floating-point precision issues
        var initialTargetHeadWidth = 235f; // 235px (5px margin from 240px max)
        var targetHeadWidthInFinal = initialTargetHeadWidth;
        logger?.LogDebug(
            "Initial target head width in final image: {TargetWidth:F1}px (range: {MinWidth:F1}px - {MaxWidth:F1}px)",
            targetHeadWidthInFinal,
            minHeadWidth,
            maxHeadWidth
        );

        // Step 4: Calculate scale factor from current head width to target
        var scaleToTarget = targetHeadWidthInFinal / pivLines.LineCC_Width;
        logger?.LogDebug(
            "Scale factor from current head ({Current:F1}px) to target ({Target:F1}px): {Scale:F3}",
            pivLines.LineCC_Width,
            targetHeadWidthInFinal,
            scaleToTarget
        );

        // Step 5: Calculate crop dimensions (what we need before resizing to 420x560)
        var cropWidth = pivLines.LineCC_Width * (finalWidth / targetHeadWidthInFinal);
        var cropHeight = cropWidth * (finalHeight / (float)finalWidth); // Maintain 420:560 aspect ratio
        logger?.LogDebug(
            "Calculated crop dimensions: {Width:F1}x{Height:F1} (aspect: {Aspect:F3})",
            cropWidth,
            cropHeight,
            cropWidth / cropHeight
        );

        // Step 6: Position crop to achieve proper eye positioning
        // Eyes should be at 60% from bottom = 40% from top in final image
        var targetEyeFromTopRatio = 1.0f - PivComplianceValidation.Thresholds.OptimalEyeFromBottom; // 0.4 (40% from top)
        var targetEyeYInCrop = cropHeight * targetEyeFromTopRatio;

        // Calculate crop center based on desired eye position
        var cropCenterX = pivLines.LineAA_X; // Center horizontally on face center line
        var cropCenterY = pivLines.LineBB_Y - targetEyeYInCrop + (cropHeight / 2.0f);
        logger?.LogDebug(
            "Crop positioning - Center: ({X:F1}, {Y:F1}), Target eye Y in crop: {EyeY:F1}",
            cropCenterX,
            cropCenterY,
            targetEyeYInCrop
        );

        // Step 7: Calculate crop bounds with image boundary constraints
        var cropX = Math.Max(0, (int)(cropCenterX - cropWidth / 2f));
        var cropY = Math.Max(0, (int)(cropCenterY - cropHeight / 2f));
        var cropMaxX = Math.Min(imageWidth, (int)(cropCenterX + cropWidth / 2f));
        var cropMaxY = Math.Min(imageHeight, (int)(cropCenterY + cropHeight / 2f));

        var finalCropWidth = cropMaxX - cropX;
        var finalCropHeight = cropMaxY - cropY;
        logger?.LogDebug(
            "Final crop bounds - X: {X}-{MaxX} ({Width}px), Y: {Y}-{MaxY} ({Height}px)",
            cropX,
            cropMaxX,
            finalCropWidth,
            cropY,
            cropMaxY,
            finalCropHeight
        );

        // Step 8: Validate crop meets minimum requirements
        var minCropWidth = 300; // Minimum for reasonable quality
        var minCropHeight = 400; // Minimum for 4:3 aspect ratio

        if (finalCropWidth < minCropWidth || finalCropHeight < minCropHeight)
        {
            logger?.LogWarning(
                "Calculated crop region too small: {Width}x{Height} (minimum: {MinWidth}x{MinHeight})",
                finalCropWidth,
                finalCropHeight,
                minCropWidth,
                minCropHeight
            );
            return Result.Failure<Rectangle>(
                $"Calculated crop region too small for PIV compliance: {finalCropWidth}x{finalCropHeight}"
            );
        }

        // Step 9: Check if we hit boundary constraints and apply adaptive fallback if enabled
        bool hitBoundary = cropX == 0 || cropY == 0 || cropMaxX == imageWidth || cropMaxY == imageHeight;
        
        if (hitBoundary && options.FallbackMode != CropFallbackMode.Strict)
        {
            logger?.LogDebug("Crop hit image boundaries, attempting adaptive fallback");
            
            // Try progressively smaller head widths
            var headWidthCandidates = new[] { 235f, 225f, 215f, minHeadWidth };
            Rectangle? bestCrop = null;
            float bestHeadWidth = 0;
            
            foreach (var candidateWidth in headWidthCandidates)
            {
                if (candidateWidth < targetHeadWidthInFinal)
                {
                    logger?.LogDebug("Trying reduced head width: {Width}px", candidateWidth);
                    
                    // Recalculate with smaller head width
                    var candidateScale = candidateWidth / pivLines.LineCC_Width;
                    var candidateCropWidth = pivLines.LineCC_Width * (finalWidth / candidateWidth);
                    var candidateCropHeight = candidateCropWidth * (finalHeight / (float)finalWidth);
                    
                    var candidateTargetEyeY = candidateCropHeight * targetEyeFromTopRatio;
                    var candidateCropCenterY = pivLines.LineBB_Y - candidateTargetEyeY + (candidateCropHeight / 2.0f);
                    
                    // Check margins
                    var candidateCropX = Math.Max(0, (int)(cropCenterX - candidateCropWidth / 2f));
                    var candidateCropY = Math.Max(0, (int)(candidateCropCenterY - candidateCropHeight / 2f));
                    var candidateCropMaxX = Math.Min(imageWidth, (int)(cropCenterX + candidateCropWidth / 2f));
                    var candidateCropMaxY = Math.Min(imageHeight, (int)(candidateCropCenterY + candidateCropHeight / 2f));
                    
                    var candidateFinalWidth = candidateCropMaxX - candidateCropX;
                    var candidateFinalHeight = candidateCropMaxY - candidateCropY;
                    
                    // Check if this gives us enough margins
                    var topMargin = pivLines.LineBB_Y - candidateCropY;
                    var leftMargin = pivLines.LineAA_X - pivLines.LineCC_Width / 2 - candidateCropX;
                    var rightMargin = candidateCropMaxX - (pivLines.LineAA_X + pivLines.LineCC_Width / 2);
                    
                    if (topMargin >= options.MinimumTopMargin && 
                        leftMargin >= options.MinimumSideMargin && 
                        rightMargin >= options.MinimumSideMargin &&
                        candidateFinalWidth >= minCropWidth &&
                        candidateFinalHeight >= minCropHeight)
                    {
                        bestCrop = new Rectangle(candidateCropX, candidateCropY, candidateFinalWidth, candidateFinalHeight);
                        bestHeadWidth = candidateWidth;
                        logger?.LogDebug("Found acceptable crop with head width {Width}px", candidateWidth);
                        break;
                    }
                }
            }
            
            if (bestCrop.HasValue)
            {
                cropX = bestCrop.Value.X;
                cropY = bestCrop.Value.Y;
                finalCropWidth = bestCrop.Value.Width;
                finalCropHeight = bestCrop.Value.Height;
                targetHeadWidthInFinal = bestHeadWidth;
                
                logger?.LogInformation(
                    "Applied adaptive crop with reduced head width: {Width}px (was {Original}px)",
                    bestHeadWidth,
                    initialTargetHeadWidth
                );
            }
            else if (options.FallbackMode == CropFallbackMode.BestEffort)
            {
                logger?.LogWarning("Could not find acceptable adaptive crop, using best effort");
                // Keep the constrained crop as-is
            }
            else
            {
                logger?.LogWarning("Adaptive fallback failed to find acceptable crop");
                return Result.Failure<Rectangle>(
                    "Could not calculate acceptable crop within image boundaries"
                );
            }
        }
        else if (hitBoundary)
        {
            logger?.LogWarning(
                "Crop was constrained by image boundaries - this may affect PIV compliance"
            );
            logger?.LogDebug(
                "Boundary constraints - Left: {Left}, Top: {Top}, Right: {Right}, Bottom: {Bottom}",
                cropX == 0,
                cropY == 0,
                cropMaxX == imageWidth,
                cropMaxY == imageHeight
            );
        }

        var cropRectangle = new Rectangle(cropX, cropY, finalCropWidth, finalCropHeight);
        logger?.LogDebug("PIV-compatible crop region calculated: {Rectangle}", cropRectangle);

        return Result.Success(cropRectangle);
    }

    // REMOVED: ApplyPivTransformations method is no longer needed
    // We now handle all transformations inline in ProcessAsync

    /// <summary>
    /// Transforms landmarks from rotated image space to PIV space.
    /// </summary>
    private static FaceLandmarks68 TransformLandmarksToPivSpace(
        FaceLandmarks68 rotatedLandmarks,
        Rectangle faceCrop,
        int pivWidth,
        int pivHeight,
        ILogger? logger = null
    )
    {
        logger?.LogDebug("Transforming landmarks from crop space to PIV space");

        // Calculate scale from crop to PIV
        // Using ResizeMode.Crop, it scales to fill and crops excess
        var scaleX = (float)pivWidth / faceCrop.Width;
        var scaleY = (float)pivHeight / faceCrop.Height;
        var scale = Math.Max(scaleX, scaleY); // Use larger scale to fill
        logger?.LogDebug(
            "Scale factors - X: {ScaleX}, Y: {ScaleY}, Final: {Scale}",
            scaleX,
            scaleY,
            scale
        );

        // Calculate offset for centering after scale
        var scaledWidth = faceCrop.Width * scale;
        var scaledHeight = faceCrop.Height * scale;
        var offsetX = (pivWidth - scaledWidth) / 2f;
        var offsetY = (pivHeight - scaledHeight) / 2f;

        return new FaceLandmarks68((
            from point in rotatedLandmarks.Points
            let cropX = point.X - faceCrop.X
            let cropY = point.Y - faceCrop.Y
            let finalX = cropX * scale + offsetX
            let finalY = cropY * scale + offsetY
            select new Point2D(finalX, finalY)
        ).ToList());
    }

    /// <summary>
    /// Returns ROI Inner Region using ANSI INCITS 385-2004 B.4 approach.
    /// </summary>
    private static Result<FacialRoiSet> StandardRoiIncits385(ILogger? logger = null)
    {
        logger?.LogDebug("Calculating Appendix C.6 compliant ROI Inner Region");

        // PIV image dimensions from constants
        const int pivWidth = PivConstants.Width; // 420
        const int pivHeight = PivConstants.Height; // 560

        // Use the standardized Appendix C.6 method
        var result = FacialRoiSet.CreateAppendixC6(pivWidth, pivHeight);

        if (result.IsSuccess)
        {
            logger?.LogDebug("Appendix C.6 ROI calculation successful using standardized method");
        }
        else
        {
            logger?.LogWarning("Appendix C.6 ROI validation failed: {Error}", result.Error);
        }
        return result;
    }
}

/// <summary>
/// Result of PIV landmark processing containing all outputs including compliance validation.
/// </summary>
public record PivLandmarkResult(
    Image<Rgba32> PivImage,
    FaceLandmarks68 Landmarks,
    FacialRoiSet RoiSet,
    float AppliedRotation,
    Rectangle FaceCrop,
    PivComplianceLines PivLines,
    PivComplianceValidation ComplianceValidation
)
{
    /// <summary>
    /// Gets the PIV image dimensions (always 420x560).
    /// </summary>
    public ImageDimensions Dimensions
    {
        get { return new ImageDimensions(PivImage.Width, PivImage.Height); }
    }

    /// <summary>
    /// Gets processing summary for logging.
    /// </summary>
    public string ProcessingSummary
    {
        get
        {
            return $"PIV processing: rotated {AppliedRotation:F1}°, crop {FaceCrop.Width}x{FaceCrop.Height}, final {Dimensions.Width}x{Dimensions.Height}, compliance: {ComplianceValidation.Summary}";
        }
    }

    /// <summary>
    /// Gets whether the result meets PIV compliance requirements.
    /// </summary>
    public bool IsCompliant
    {
        get { return ComplianceValidation.IsFullyCompliant; }
    }

    /// <summary>
    /// Gets the PIV compliance severity level.
    /// </summary>
    public ComplianceSeverity ComplianceSeverity
    {
        get { return ComplianceValidation.Severity; }
    }
}

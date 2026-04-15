using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Transformations;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Shared standard portrait processing service used by CLI dataset workflows and output-mode validation.
/// </summary>
public sealed class StandardPortraitProcessorService(
    IFacialProcessingServiceFactory processingServiceFactory,
    IJpeg2000Encoder jpeg2000Encoder,
    ILogger<StandardPortraitProcessorService> logger)
{
    private readonly IFacialProcessingServiceFactory _processingServiceFactory = processingServiceFactory;
    private readonly IJpeg2000Encoder _jpeg2000Encoder = jpeg2000Encoder;
    private readonly ILogger<StandardPortraitProcessorService> _logger = logger;

    internal StandardPortraitProcessorService(
        IFaceDetector faceDetector,
        ILandmarkExtractor landmarkExtractor,
        IJpeg2000Encoder jpeg2000Encoder,
        ILogger<StandardPortraitProcessorService> logger)
        : this(
            new SharedFacialProcessingServiceFactory(
                FacialProcessingServices.FromExisting(faceDetector, landmarkExtractor, jpeg2000Encoder)),
            jpeg2000Encoder,
            logger)
    {
    }

    /// <summary>
    /// Aligns, crops, and resizes a portrait to the requested standard without encoding it.
    /// </summary>
    public async Task<Result<StandardPortraitAlignmentResult, PipelineError>> AlignAsync(
        Image<Rgba32> sourceImage,
        string standardName,
        float minConfidence,
        int minFaceSize,
        CancellationToken cancellationToken = default)
    {
        var servicesResult = _processingServiceFactory.GetServices();
        if (servicesResult.IsFailure)
        {
            return Result.Failure<StandardPortraitAlignmentResult, PipelineError>(servicesResult.Error);
        }

        var processingServices = servicesResult.Value;
        var standard = QualityAssessmentOptions.ForStandard(standardName).Standard;
        var profile = StandardPortraitProfile.ForStandard(standardName);

        var detection = await processingServices.Detector.DetectFacesAsync(sourceImage).ConfigureAwait(false);
        if (detection.IsFailure)
        {
            return Result.Failure<StandardPortraitAlignmentResult, PipelineError>(detection.Error);
        }

        var faces = detection.Value
            .Where(face => face.Confidence >= minConfidence)
            .Where(face => face.BoundingBox.Width >= minFaceSize && face.BoundingBox.Height >= minFaceSize)
            .OrderByDescending(face => face.Confidence)
            .ToArray();

        if (faces.Length == 0)
        {
            return Result.Failure<StandardPortraitAlignmentResult, PipelineError>(
                new DetectionError("No suitable faces found", standardName));
        }

        if (profile.RequireSingleFace && faces.Length > 1)
        {
            return Result.Failure<StandardPortraitAlignmentResult, PipelineError>(
                new DetectionError(
                    $"Multiple faces detected ({faces.Length}), {profile.StandardName} requires single face",
                    standardName));
        }

        var sourceFace = faces[0];
        var sourceLandmarksResult = await processingServices.LandmarkExtractor
            .ExtractLandmarksAsync(sourceImage, sourceFace.BoundingBox)
            .ConfigureAwait(false);
        if (sourceLandmarksResult.IsFailure)
        {
            return Result.Failure<StandardPortraitAlignmentResult, PipelineError>(sourceLandmarksResult.Error);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var rotation = CalculateRotation(
            sourceLandmarksResult.Value.LeftEyeCenter,
            sourceLandmarksResult.Value.RightEyeCenter);

        using var rotatedImage = Math.Abs(rotation) > 0.1f
            ? sourceImage.Clone(ctx => ctx.Rotate(rotation))
            : sourceImage.Clone();

        var rotatedDetection = await processingServices.Detector
            .DetectFacesAsync(rotatedImage)
            .ConfigureAwait(false);
        if (rotatedDetection.IsFailure || rotatedDetection.Value.Count == 0)
        {
            return rotatedDetection.IsFailure
                ? Result.Failure<StandardPortraitAlignmentResult, PipelineError>(rotatedDetection.Error)
                : Result.Failure<StandardPortraitAlignmentResult, PipelineError>(
                    new DetectionError("Failed to detect face after rotation", standardName));
        }

        var rotatedFace = rotatedDetection.Value
            .Where(face => face.Confidence >= minConfidence)
            .Where(face => face.BoundingBox.Width >= minFaceSize && face.BoundingBox.Height >= minFaceSize)
            .OrderByDescending(face => face.Confidence)
            .FirstOrDefault();
        if (rotatedFace == null)
        {
            return Result.Failure<StandardPortraitAlignmentResult, PipelineError>(
                new DetectionError("No suitable rotated face found", standardName));
        }

        var rotatedLandmarksResult = await processingServices.LandmarkExtractor
            .ExtractLandmarksAsync(rotatedImage, rotatedFace.BoundingBox)
            .ConfigureAwait(false);
        if (rotatedLandmarksResult.IsFailure)
        {
            return Result.Failure<StandardPortraitAlignmentResult, PipelineError>(rotatedLandmarksResult.Error);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var cropRectangle = CalculateCropRectangle(
            rotatedFace.BoundingBox,
            rotatedLandmarksResult.Value,
            rotatedImage.Width,
            rotatedImage.Height,
            standard.ExpectedDimensions);

        var processedImage = rotatedImage.Clone(ctx =>
        {
            ctx.Crop(cropRectangle);
            ctx.Resize(standard.ExpectedDimensions.Width, standard.ExpectedDimensions.Height);
        });

        var processedLandmarks = TransformLandmarksToOutputSpace(
            rotatedLandmarksResult.Value,
            cropRectangle,
            standard.ExpectedDimensions);

        _logger.LogDebug(
            "Aligned {Standard} portrait to {Width}x{Height} with rotation {Rotation:F2}",
            profile.StandardName,
            standard.ExpectedDimensions.Width,
            standard.ExpectedDimensions.Height,
            rotation);

        return Result.Success<StandardPortraitAlignmentResult, PipelineError>(new StandardPortraitAlignmentResult(
            processedImage,
            processedLandmarks,
            standard.ExpectedDimensions,
            rotation,
            sourceFace.Confidence,
            profile.StandardName,
            cropRectangle));
    }

    /// <summary>
    /// Aligns, crops, resizes, and JPEG 2000 encodes a portrait to the requested standard.
    /// </summary>
    public async Task<Result<StandardPortraitResult, PipelineError>> ProcessAsync(
        Image<Rgba32> sourceImage,
        string standardName,
        float minConfidence,
        int minFaceSize,
        CancellationToken cancellationToken = default)
    {
        var profile = StandardPortraitProfile.ForStandard(standardName);
        var alignmentResult = await AlignAsync(
            sourceImage,
            standardName,
            minConfidence,
            minFaceSize,
            cancellationToken).ConfigureAwait(false);
        if (alignmentResult.IsFailure)
        {
            return Result.Failure<StandardPortraitResult, PipelineError>(alignmentResult.Error);
        }

        using var alignment = alignmentResult.Value;
        var roiSet = FacialRoiSet.CalculateRoiForDimensions(
            alignment.OutputDimensions.Width,
            alignment.OutputDimensions.Height);

        using var imageForEncoding = alignment.ProcessedImage.Clone();
        var encoded = _jpeg2000Encoder.EncodeWithRoi(
            imageForEncoding,
            roiSet,
            baseRate: profile.BaseRate,
            roiStartLevel: profile.RoiStartLevel,
            enableRoi: profile.EnableRoi,
            roiAlign: false);

        if (encoded.IsFailure)
        {
            return Result.Failure<StandardPortraitResult, PipelineError>(encoded.Error);
        }

        return Result.Success<StandardPortraitResult, PipelineError>(new StandardPortraitResult(
            alignment.ProcessedImage.Clone(),
            encoded.Value,
            alignment.ProcessedLandmarks,
            alignment.OutputDimensions,
            alignment.RotationDegrees,
            alignment.FaceConfidence,
            alignment.StandardName,
            alignment.CropRectangle));
    }

    internal static float CalculateRotation(Point2D leftEye, Point2D rightEye)
    {
        var deltaY = rightEye.Y - leftEye.Y;
        var deltaX = rightEye.X - leftEye.X;
        return -(float)(Math.Atan2(deltaY, deltaX) * 180.0 / Math.PI);
    }

    internal static Rectangle CalculateCropRectangle(
        FaceBox faceBox,
        FaceLandmarks68 landmarks,
        int imageWidth,
        int imageHeight,
        ImageDimensions targetDimensions)
    {
        const float targetFaceWidthRatio = 0.70f;
        var desiredWidth = faceBox.Width / targetFaceWidthRatio;
        var desiredHeight = desiredWidth * targetDimensions.Height / targetDimensions.Width;

        if (desiredWidth > imageWidth)
        {
            desiredWidth = imageWidth;
            desiredHeight = desiredWidth * targetDimensions.Height / targetDimensions.Width;
        }

        if (desiredHeight > imageHeight)
        {
            desiredHeight = imageHeight;
            desiredWidth = desiredHeight * targetDimensions.Width / targetDimensions.Height;
        }

        var eyeCenterX = (landmarks.LeftEyeCenter.X + landmarks.RightEyeCenter.X) / 2f;
        var eyeCenterY = (landmarks.LeftEyeCenter.Y + landmarks.RightEyeCenter.Y) / 2f;

        var cropX = eyeCenterX - desiredWidth / 2f;
        var cropY = eyeCenterY - desiredHeight * 0.45f;

        cropX = Math.Clamp(cropX, 0f, Math.Max(0f, imageWidth - desiredWidth));
        cropY = Math.Clamp(cropY, 0f, Math.Max(0f, imageHeight - desiredHeight));

        var width = Math.Min((int)Math.Round(desiredWidth), imageWidth);
        var height = Math.Min((int)Math.Round(desiredHeight), imageHeight);
        var x = Math.Clamp((int)Math.Round(cropX), 0, Math.Max(0, imageWidth - width));
        var y = Math.Clamp((int)Math.Round(cropY), 0, Math.Max(0, imageHeight - height));

        return new Rectangle(x, y, width, height);
    }

    private static FaceLandmarks68 TransformLandmarksToOutputSpace(
        FaceLandmarks68 rotatedLandmarks,
        Rectangle cropRectangle,
        ImageDimensions targetDimensions)
    {
        var scaleX = (float)targetDimensions.Width / cropRectangle.Width;
        var scaleY = (float)targetDimensions.Height / cropRectangle.Height;

        var transformedPoints = rotatedLandmarks.Points
            .Select(point => new Point2D(
                (point.X - cropRectangle.X) * scaleX,
                (point.Y - cropRectangle.Y) * scaleY))
            .ToList();

        return new FaceLandmarks68(transformedPoints);
    }
}

/// <summary>
/// Holds the aligned portrait image and landmarks before encoding.
/// </summary>
public sealed class StandardPortraitAlignmentResult : IDisposable
{
    /// <summary>
    /// Initializes a new alignment result.
    /// </summary>
    public StandardPortraitAlignmentResult(
        Image<Rgba32> processedImage,
        FaceLandmarks68 processedLandmarks,
        ImageDimensions outputDimensions,
        float rotationDegrees,
        float faceConfidence,
        string standardName,
        Rectangle cropRectangle)
    {
        ProcessedImage = processedImage;
        ProcessedLandmarks = processedLandmarks;
        OutputDimensions = outputDimensions;
        RotationDegrees = rotationDegrees;
        FaceConfidence = faceConfidence;
        StandardName = standardName;
        CropRectangle = cropRectangle;
    }

    /// <summary>The aligned portrait image.</summary>
    public Image<Rgba32> ProcessedImage { get; }

    /// <summary>The landmarks transformed into aligned portrait coordinates.</summary>
    public FaceLandmarks68 ProcessedLandmarks { get; }

    /// <summary>The aligned portrait dimensions.</summary>
    public ImageDimensions OutputDimensions { get; }

    /// <summary>The applied rotation in degrees.</summary>
    public float RotationDegrees { get; }

    /// <summary>The confidence of the selected face detection.</summary>
    public float FaceConfidence { get; }

    /// <summary>The normalized standard name.</summary>
    public string StandardName { get; }

    /// <summary>The crop rectangle used in the rotated image space.</summary>
    public Rectangle CropRectangle { get; }

    /// <summary>
     /// Disposes the aligned portrait image.
     /// </summary>
    public void Dispose()
    {
        ProcessedImage.Dispose();
    }
}

/// <summary>
/// Holds the fully processed portrait image, transformed landmarks, and encoded payload.
/// </summary>
public sealed class StandardPortraitResult : IDisposable
{
    /// <summary>
    /// Initializes a new processed portrait result.
    /// </summary>
    public StandardPortraitResult(
        Image<Rgba32> processedImage,
        byte[] encodedImageData,
        FaceLandmarks68 processedLandmarks,
        ImageDimensions outputDimensions,
        float rotationDegrees,
        float faceConfidence,
        string standardName,
        Rectangle cropRectangle)
    {
        ProcessedImage = processedImage;
        EncodedImageData = encodedImageData;
        ProcessedLandmarks = processedLandmarks;
        OutputDimensions = outputDimensions;
        RotationDegrees = rotationDegrees;
        FaceConfidence = faceConfidence;
        StandardName = standardName;
        CropRectangle = cropRectangle;
    }

    /// <summary>The aligned portrait image.</summary>
    public Image<Rgba32> ProcessedImage { get; }

    /// <summary>The encoded JPEG 2000 payload.</summary>
    public byte[] EncodedImageData { get; }

    /// <summary>The landmarks transformed into aligned portrait coordinates.</summary>
    public FaceLandmarks68 ProcessedLandmarks { get; }

    /// <summary>The final portrait dimensions.</summary>
    public ImageDimensions OutputDimensions { get; }

    /// <summary>The applied rotation in degrees.</summary>
    public float RotationDegrees { get; }

    /// <summary>The confidence of the selected face detection.</summary>
    public float FaceConfidence { get; }

    /// <summary>The normalized standard name.</summary>
    public string StandardName { get; }

    /// <summary>The crop rectangle used in the rotated image space.</summary>
    public Rectangle CropRectangle { get; }

    /// <summary>
     /// Disposes the processed portrait image.
     /// </summary>
    public void Dispose()
    {
        ProcessedImage.Dispose();
    }
}

internal sealed record StandardPortraitProfile(
    string StandardName,
    float BaseRate,
    int RoiStartLevel,
    bool EnableRoi,
    bool RequireSingleFace)
{
    public static StandardPortraitProfile ForStandard(string standardName) =>
        standardName.ToUpperInvariant() switch
        {
            "TWIC" => new StandardPortraitProfile("TWIC", 0.5f, 3, true, true),
            "ICAO" => new StandardPortraitProfile("ICAO", 2.0f, 2, false, false),
            "CAC" => new StandardPortraitProfile("CAC", 0.7f, 3, true, false),
            _ => new StandardPortraitProfile("PIV", 0.7f, 3, true, true)
        };
}

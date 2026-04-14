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

namespace FaceOFFx.Cli.Services;

internal sealed class StandardPortraitProcessor(
    IFaceDetector faceDetector,
    ILandmarkExtractor landmarkExtractor,
    IJpeg2000Encoder jpeg2000Encoder,
    ILogger<StandardPortraitProcessor> logger)
{
    private readonly IFaceDetector _faceDetector = faceDetector;
    private readonly ILandmarkExtractor _landmarkExtractor = landmarkExtractor;
    private readonly IJpeg2000Encoder _jpeg2000Encoder = jpeg2000Encoder;
    private readonly ILogger<StandardPortraitProcessor> _logger = logger;

    public async Task<Result<StandardPortraitResult>> ProcessAsync(
        Image<Rgba32> sourceImage,
        string standardName,
        float minConfidence,
        int minFaceSize,
        CancellationToken cancellationToken = default)
    {
        var standard = QualityAssessmentOptions.ForStandard(standardName).Standard;
        var profile = StandardPortraitProfile.ForStandard(standardName);

        var detection = await _faceDetector.DetectFacesAsync(sourceImage).ConfigureAwait(false);
        if (detection.IsFailure)
        {
            return Result.Failure<StandardPortraitResult>(
                $"Face detection failed: {detection.Error}");
        }

        var faces = detection.Value
            .Where(face => face.Confidence >= minConfidence)
            .Where(face => face.BoundingBox.Width >= minFaceSize && face.BoundingBox.Height >= minFaceSize)
            .OrderByDescending(face => face.Confidence)
            .ToArray();

        if (faces.Length == 0)
        {
            return Result.Failure<StandardPortraitResult>("No suitable faces found");
        }

        if (profile.RequireSingleFace && faces.Length > 1)
        {
            return Result.Failure<StandardPortraitResult>(
                $"Multiple faces detected ({faces.Length}), {profile.StandardName} requires single face");
        }

        var sourceFace = faces[0];
        var sourceLandmarksResult = await _landmarkExtractor
            .ExtractLandmarksAsync(sourceImage, sourceFace.BoundingBox)
            .ConfigureAwait(false);
        if (sourceLandmarksResult.IsFailure)
        {
            return Result.Failure<StandardPortraitResult>(
                $"Landmark extraction failed: {sourceLandmarksResult.Error}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var rotation = CalculateRotation(sourceLandmarksResult.Value.LeftEyeCenter, sourceLandmarksResult.Value.RightEyeCenter);
        using var rotatedImage = Math.Abs(rotation) > 0.1f
            ? sourceImage.Clone(ctx => ctx.Rotate(rotation))
            : sourceImage.Clone();

        var rotatedDetection = await _faceDetector.DetectFacesAsync(rotatedImage).ConfigureAwait(false);
        if (rotatedDetection.IsFailure || rotatedDetection.Value.Count == 0)
        {
            return Result.Failure<StandardPortraitResult>(
                "Failed to detect face after rotation");
        }

        var rotatedFace = rotatedDetection.Value
            .Where(face => face.Confidence >= minConfidence)
            .Where(face => face.BoundingBox.Width >= minFaceSize && face.BoundingBox.Height >= minFaceSize)
            .OrderByDescending(face => face.Confidence)
            .FirstOrDefault();
        if (rotatedFace == null)
        {
            return Result.Failure<StandardPortraitResult>("No suitable rotated face found");
        }

        var rotatedLandmarksResult = await _landmarkExtractor
            .ExtractLandmarksAsync(rotatedImage, rotatedFace.BoundingBox)
            .ConfigureAwait(false);
        if (rotatedLandmarksResult.IsFailure)
        {
            return Result.Failure<StandardPortraitResult>(
                $"Landmark extraction failed after rotation: {rotatedLandmarksResult.Error}");
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

        var roiSet = FacialRoiSet.CalculateRoiForDimensions(
            standard.ExpectedDimensions.Width,
            standard.ExpectedDimensions.Height);
        using var imageForEncoding = processedImage.Clone();
        var encoded = _jpeg2000Encoder.EncodeWithRoi(
            imageForEncoding,
            roiSet,
            baseRate: profile.BaseRate,
            roiStartLevel: profile.RoiStartLevel,
            enableRoi: profile.EnableRoi,
            roiAlign: false);

        if (encoded.IsFailure)
        {
            processedImage.Dispose();
            return Result.Failure<StandardPortraitResult>(
                $"JPEG 2000 encoding failed: {encoded.Error}");
        }

        _logger.LogDebug(
            "Processed {Standard} portrait to {Width}x{Height} with rotation {Rotation:F2}",
            profile.StandardName,
            standard.ExpectedDimensions.Width,
            standard.ExpectedDimensions.Height,
            rotation);

        return Result.Success(new StandardPortraitResult(
            processedImage,
            encoded.Value,
            standard.ExpectedDimensions,
            rotation,
            sourceFace.Confidence,
            profile.StandardName));
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

        cropX = Math.Clamp(cropX, 0, imageWidth - desiredWidth);
        cropY = Math.Clamp(cropY, 0, imageHeight - desiredHeight);

        var width = Math.Min((int)Math.Round(desiredWidth), imageWidth);
        var height = Math.Min((int)Math.Round(desiredHeight), imageHeight);
        var x = Math.Clamp((int)Math.Round(cropX), 0, Math.Max(0, imageWidth - width));
        var y = Math.Clamp((int)Math.Round(cropY), 0, Math.Max(0, imageHeight - height));

        return new Rectangle(x, y, width, height);
    }
}

internal sealed record StandardPortraitResult(
    Image<Rgba32> ProcessedImage,
    byte[] EncodedImageData,
    ImageDimensions OutputDimensions,
    float RotationDegrees,
    float FaceConfidence,
    string StandardName);

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

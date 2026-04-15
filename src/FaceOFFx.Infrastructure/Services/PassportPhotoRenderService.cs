using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Core.Domain.Transformations;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Produces the exact portrait render geometry used by document workflows.
/// </summary>
public sealed class PassportPhotoRenderService(
    IFaceDetector faceDetector,
    ILandmarkExtractor landmarkExtractor,
    ILogger<PassportPhotoRenderService> logger)
{
    private readonly IFaceDetector _faceDetector = faceDetector;
    private readonly ILandmarkExtractor _landmarkExtractor = landmarkExtractor;
    private readonly ILogger<PassportPhotoRenderService> _logger = logger;

    /// <summary>
    /// Aligns, crops, and resizes a portrait according to the supplied document photo specification.
    /// </summary>
    public async Task<Result<PassportPhotoRenderResult>> AlignAsync(
        Image<Rgba32> sourceImage,
        PassportPhotoSpec spec,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Rendering portrait alignment for {Width}x{Height} -> {TargetWidth}x{TargetHeight}",
            sourceImage.Width,
            sourceImage.Height,
            spec.TargetWidth,
            spec.TargetHeight);

        var detection = await _faceDetector.DetectFacesAsync(sourceImage).ConfigureAwait(false);
        if (detection.IsFailure || detection.Value.Count == 0)
        {
            return Result.Failure<PassportPhotoRenderResult>("No suitable faces found for portrait rendering.");
        }

        var face = detection.Value
            .OrderByDescending(candidate => candidate.Confidence)
            .First();

        var geometryResult = await CanonicalFaceGeometryPipeline
            .ExtractAsync(sourceImage, face, _landmarkExtractor, cancellationToken)
            .ConfigureAwait(false);
        if (geometryResult.IsFailure)
        {
            return Result.Failure<PassportPhotoRenderResult>(geometryResult.Error);
        }

        return Align(sourceImage, geometryResult.Value, spec);
    }

    /// <summary>
    /// Aligns, crops, and resizes a portrait from canonical original-space landmarks.
    /// </summary>
    internal Result<PassportPhotoRenderResult> Align(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        PassportPhotoSpec spec)
    {
        var rotation = FaceGeometryTransformations.CalculateEyeRotation(
            geometry.SourceLandmarks.LeftEyeCenter,
            geometry.SourceLandmarks.RightEyeCenter);

        using var rotated = Math.Abs(rotation) > 0.1f
            ? sourceImage.Clone(ctx => ctx.Rotate(rotation))
            : sourceImage.Clone();

        var rotatedLandmarks = FaceGeometryTransformations.RotateLandmarks(
            geometry.SourceLandmarks,
            rotation,
            new ImageDimensions(sourceImage.Width, sourceImage.Height));

        var crop = CalculatePortraitCrop(
            rotatedLandmarks,
            rotated.Width,
            rotated.Height,
            spec.TargetWidth,
            spec.TargetHeight,
            spec.TargetHeadHeightRatio,
            spec.TargetEyeFromBottomRatio);

        var outputImage = rotated.Clone(ctx =>
        {
            ctx.Crop(crop);
            ctx.Resize(spec.TargetWidth, spec.TargetHeight);
        });

        var outputLandmarks = FaceGeometryTransformations.TransformLandmarksToOutputSpace(
            rotatedLandmarks,
            crop,
            new ImageDimensions(spec.TargetWidth, spec.TargetHeight));
        var transformMap = RenderTransformMapBuilder.CreateRotateCropResize(
            new ImageDimensions(sourceImage.Width, sourceImage.Height),
            rotation,
            new ImageDimensions(rotated.Width, rotated.Height),
            crop,
            new ImageDimensions(spec.TargetWidth, spec.TargetHeight));

        return Result.Success(new PassportPhotoRenderResult(
            outputImage,
            outputLandmarks,
            rotation,
            crop,
            transformMap));
    }

    private static Rectangle CalculatePortraitCrop(
        FaceLandmarks68 landmarks,
        int imageWidth,
        int imageHeight,
        int targetWidth,
        int targetHeight,
        float targetHeadHeightRatio,
        float targetEyeFromBottomRatio)
    {
        var imageAspect = (float)targetWidth / targetHeight;
        var browY = landmarks.Points.Take(17).Min(point => point.Y);
        var chinY = landmarks.Points[8].Y;
        var faceHeight = chinY - browY;
        var estimatedHeadTop = browY - (faceHeight * 0.3f);
        var headHeight = chinY - estimatedHeadTop;
        var desiredCropHeight = headHeight / targetHeadHeightRatio;
        var desiredCropWidth = desiredCropHeight * imageAspect;
        var eyeCenterY = (landmarks.LeftEyeCenter.Y + landmarks.RightEyeCenter.Y) / 2f;
        var desiredEyeFromTopRatio = 1f - targetEyeFromBottomRatio;
        var cropY = eyeCenterY - (desiredCropHeight * desiredEyeFromTopRatio);

        var faceCenterX = landmarks.Points.Average(point => point.X);
        var cropX = faceCenterX - (desiredCropWidth / 2f);

        desiredCropWidth = Math.Min(desiredCropWidth, imageWidth);
        desiredCropHeight = Math.Min(desiredCropHeight, imageHeight);
        cropX = Math.Clamp(cropX, 0f, Math.Max(0f, imageWidth - desiredCropWidth));
        cropY = Math.Clamp(cropY, 0f, Math.Max(0f, imageHeight - desiredCropHeight));

        var width = Math.Clamp((int)Math.Round(desiredCropWidth), 1, imageWidth);
        var height = Math.Clamp((int)Math.Round(desiredCropHeight), 1, imageHeight);
        var x = Math.Clamp((int)Math.Round(cropX), 0, Math.Max(0, imageWidth - width));
        var y = Math.Clamp((int)Math.Round(cropY), 0, Math.Max(0, imageHeight - height));

        return new Rectangle(x, y, width, height);
    }

}

/// <summary>
/// Result of rendering a passport-style portrait, including the exact transform map used.
/// </summary>
public sealed record PassportPhotoRenderResult(
    Image<Rgba32> Image,
    FaceLandmarks68 Landmarks,
    float RotationDegrees,
    Rectangle CropRectangle,
    RenderTransformMap TransformMap) : IDisposable
{
    /// <summary>
    /// Disposes the rendered image buffer.
    /// </summary>
    public void Dispose() => Image.Dispose();
}

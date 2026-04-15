using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using JetBrains.Annotations;
using SixLabors.ImageSharp;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>
/// Applies the standard portrait crop and resize transform to landmark geometry.
/// </summary>
[PublicAPI]
public static class StandardPortraitTransform
{
    private const float TargetFaceWidthRatio = 0.70f;

    /// <summary>
    /// Projects source landmarks into the final output portrait geometry for the requested standard.
    /// </summary>
    public static Result<StandardPortraitTransformResult, PipelineError> Transform(
        ImageDimensions sourceDimensions,
        FaceLandmarks68 sourceLandmarks,
        ImageDimensions targetDimensions,
        float maxRotationDegrees = 15f)
    {
        if (sourceLandmarks is null || !sourceLandmarks.IsValid)
        {
            return Result.Failure<StandardPortraitTransformResult, PipelineError>(
                new GeometryError(
                    "Valid facial landmarks are required for output portrait transformation",
                    "standard-portrait"));
        }

        if (sourceDimensions.Width <= 0 || sourceDimensions.Height <= 0)
        {
            return Result.Failure<StandardPortraitTransformResult, PipelineError>(
                new ValidationError(
                    $"Invalid source dimensions {sourceDimensions.Width}x{sourceDimensions.Height}",
                    "standard-portrait"));
        }

        var rotationDegrees = CalculateRotation(
            sourceLandmarks.LeftEyeCenter,
            sourceLandmarks.RightEyeCenter,
            maxRotationDegrees);
        var rotatedLandmarks = RotateLandmarks(
            sourceLandmarks,
            rotationDegrees,
            sourceDimensions.Width,
            sourceDimensions.Height);

        var rotatedDimensions = CalculateRotatedDimensions(
            sourceDimensions.Width,
            sourceDimensions.Height,
            rotationDegrees);

        var cropRectangleResult = CalculateCropRectangle(
            rotatedLandmarks,
            rotatedDimensions.Width,
            rotatedDimensions.Height,
            targetDimensions);
        if (cropRectangleResult.IsFailure)
        {
            return Result.Failure<StandardPortraitTransformResult, PipelineError>(cropRectangleResult.Error);
        }

        var transformedLandmarks = TransformLandmarksToOutputSpace(
            rotatedLandmarks,
            cropRectangleResult.Value,
            targetDimensions);

        return Result.Success<StandardPortraitTransformResult, PipelineError>(
            new StandardPortraitTransformResult(
                transformedLandmarks,
                targetDimensions,
                rotationDegrees,
                cropRectangleResult.Value));
    }

    internal static float CalculateRotation(Point2D leftEye, Point2D rightEye, float maxRotationDegrees)
    {
        var deltaY = rightEye.Y - leftEye.Y;
        var deltaX = rightEye.X - leftEye.X;
        var rotationDegrees = -(float)(Math.Atan2(deltaY, deltaX) * 180.0 / Math.PI);
        return Math.Clamp(rotationDegrees, -maxRotationDegrees, maxRotationDegrees);
    }

    internal static Result<Rectangle, PipelineError> CalculateCropRectangle(
        FaceLandmarks68 landmarks,
        int imageWidth,
        int imageHeight,
        ImageDimensions targetDimensions)
    {
        var faceContour = landmarks.Points.Take(17).ToArray();
        var faceWidth = faceContour.Max(point => point.X) - faceContour.Min(point => point.X);
        if (faceWidth <= 0f)
        {
            return Result.Failure<Rectangle, PipelineError>(
                new GeometryError(
                    "Unable to calculate portrait crop from zero-width landmarks",
                    "standard-portrait"));
        }

        var desiredWidth = faceWidth / TargetFaceWidthRatio;
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

        return Result.Success<Rectangle, PipelineError>(new Rectangle(x, y, width, height));
    }

    private static FaceLandmarks68 RotateLandmarks(
        FaceLandmarks68 landmarks,
        float rotationDegrees,
        int imageWidth,
        int imageHeight)
    {
        if (Math.Abs(rotationDegrees) <= 0.1f)
        {
            return landmarks;
        }

        var angle = rotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);

        var oldCenterX = imageWidth / 2.0;
        var oldCenterY = imageHeight / 2.0;

        var rotatedDimensions = CalculateRotatedDimensions(imageWidth, imageHeight, rotationDegrees);
        var newCenterX = rotatedDimensions.Width / 2.0;
        var newCenterY = rotatedDimensions.Height / 2.0;

        var rotatedPoints = landmarks.Points
            .Select(point =>
            {
                var x = point.X - oldCenterX;
                var y = point.Y - oldCenterY;
                var rotatedX = x * cos - y * sin;
                var rotatedY = x * sin + y * cos;
                return new Point2D((float)(rotatedX + newCenterX), (float)(rotatedY + newCenterY));
            })
            .ToList();

        return new FaceLandmarks68(rotatedPoints);
    }

    private static ImageDimensions CalculateRotatedDimensions(int imageWidth, int imageHeight, float rotationDegrees)
    {
        if (Math.Abs(rotationDegrees) <= 0.1f)
        {
            return new ImageDimensions(imageWidth, imageHeight);
        }

        var angle = rotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var newWidth = (int)Math.Ceiling(Math.Abs(imageWidth * cos) + Math.Abs(imageHeight * sin));
        var newHeight = (int)Math.Ceiling(Math.Abs(imageWidth * sin) + Math.Abs(imageHeight * cos));
        return new ImageDimensions(newWidth, newHeight);
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
/// Result of projecting landmarks into the final output portrait geometry.
/// </summary>
[PublicAPI]
public sealed record StandardPortraitTransformResult(
    FaceLandmarks68 Landmarks,
    ImageDimensions OutputDimensions,
    float RotationDegrees,
    Rectangle CropRectangle);

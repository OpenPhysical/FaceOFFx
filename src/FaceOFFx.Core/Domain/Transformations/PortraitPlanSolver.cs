using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using JetBrains.Annotations;
using SixLabors.ImageSharp;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>
/// Solves crop and transform plans for built-in profile portraits.
/// </summary>
[PublicAPI]
public static class PortraitPlanSolver
{
    /// <summary>
    /// Solves a compliant portrait crop and transform plan from source-space landmarks.
    /// </summary>
    public static Result<PortraitPlan, PipelineError> Solve(
        ImageDimensions sourceDimensions,
        FaceLandmarks68 sourceLandmarks,
        PortraitSpecification specification)
    {
        if (!sourceLandmarks.IsValid)
        {
            return Result.Failure<PortraitPlan, PipelineError>(
                new GeometryError("Valid 68-point landmarks are required for portrait solving.", "profile-portrait"));
        }

        var interPupillaryDistance = Distance(sourceLandmarks.LeftEyeCenter, sourceLandmarks.RightEyeCenter);
        if (interPupillaryDistance < specification.MinimumInterPupillaryDistance)
        {
            return Result.Failure<PortraitPlan, PipelineError>(
                new ValidationError(
                    $"Inter-pupillary distance {interPupillaryDistance:F1}px is below the required minimum {specification.MinimumInterPupillaryDistance}px.",
                    "profile-portrait"));
        }

        var rotationDegrees = StandardPortraitTransform.CalculateRotation(
            sourceLandmarks.LeftEyeCenter,
            sourceLandmarks.RightEyeCenter,
            specification.MaximumRotationDegrees);
        var rotatedDimensions = RenderTransformMapBuilder.ComputeExpandedRotationDimensions(sourceDimensions, rotationDegrees);
        var rotatedLandmarks = RotateLandmarks(sourceLandmarks, rotationDegrees, sourceDimensions, rotatedDimensions);

        var traces = new List<CandidateTrace>();
        foreach (var requestedHeadWidthRatio in specification.HeadWidthCandidateRatios)
        {
            var candidateResult = TrySolveCandidate(rotatedDimensions, rotatedLandmarks, specification, requestedHeadWidthRatio);
            if (candidateResult.IsFailure)
            {
                traces.Add(new CandidateTrace(requestedHeadWidthRatio, Maybe<float>.None, candidateResult.Error.Message));
                continue;
            }

            var candidate = candidateResult.Value;
            var transformMapResult = RenderTransformMapBuilder.CreateRotateCropResize(
                sourceDimensions,
                rotationDegrees,
                rotatedDimensions,
                candidate.CropRectangle,
                specification.OutputDimensions);
            if (transformMapResult.IsFailure)
            {
                traces.Add(new CandidateTrace(requestedHeadWidthRatio, Maybe<float>.None, transformMapResult.Error.Message));
                continue;
            }

            traces.Add(new CandidateTrace(
                requestedHeadWidthRatio,
                Maybe<float>.From(candidate.AcceptedHeadWidthRatio),
                $"Accepted crop {candidate.CropRectangle.Width}x{candidate.CropRectangle.Height}."));

            return Result.Success<PortraitPlan, PipelineError>(new PortraitPlan(
                specification.OutputDimensions,
                rotatedDimensions,
                candidate.CropRectangle,
                rotationDegrees,
                candidate.AcceptedHeadWidthRatio,
                candidate.AcceptedEyeLineFromTopRatio,
                transformMapResult.Value,
                traces));
        }

        return Result.Failure<PortraitPlan, PipelineError>(
            new GeometryError(
                $"No portrait crop satisfied the profile requirements. {string.Join(" ", traces.Select(trace => trace.Summary))}",
                "profile-portrait"));
    }

    private static Result<(Rectangle CropRectangle, float AcceptedHeadWidthRatio, float AcceptedEyeLineFromTopRatio), PipelineError> TrySolveCandidate(
        ImageDimensions rotatedDimensions,
        FaceLandmarks68 rotatedLandmarks,
        PortraitSpecification specification,
        float requestedHeadWidthRatio)
    {
        var faceContour = rotatedLandmarks.Points.Take(17).ToArray();
        var minFaceX = faceContour.Min(point => point.X);
        var maxFaceX = faceContour.Max(point => point.X);
        var minFaceY = faceContour.Min(point => point.Y);
        var currentHeadWidth = maxFaceX - minFaceX;
        if (currentHeadWidth <= 0f)
        {
            return Result.Failure<(Rectangle CropRectangle, float AcceptedHeadWidthRatio, float AcceptedEyeLineFromTopRatio), PipelineError>(
                new GeometryError("Cannot solve portrait crop from zero-width face landmarks.", "profile-portrait"));
        }

        var desiredCropWidth = currentHeadWidth / requestedHeadWidthRatio;
        var desiredCropHeight = desiredCropWidth * specification.OutputDimensions.Height / specification.OutputDimensions.Width;

        desiredCropWidth = Math.Min(desiredCropWidth, rotatedDimensions.Width);
        desiredCropHeight = Math.Min(desiredCropHeight, rotatedDimensions.Height);

        var eyeCenterX = (rotatedLandmarks.LeftEyeCenter.X + rotatedLandmarks.RightEyeCenter.X) / 2f;
        var eyeCenterY = (rotatedLandmarks.LeftEyeCenter.Y + rotatedLandmarks.RightEyeCenter.Y) / 2f;

        var cropX = eyeCenterX - desiredCropWidth / 2f;
        var cropY = eyeCenterY - (desiredCropHeight * specification.TargetEyeLineFromTopRatio);

        cropX = Math.Clamp(cropX, 0f, Math.Max(0f, rotatedDimensions.Width - desiredCropWidth));
        cropY = Math.Clamp(cropY, 0f, Math.Max(0f, rotatedDimensions.Height - desiredCropHeight));

        var cropWidth = Math.Min((int)Math.Round(desiredCropWidth), rotatedDimensions.Width);
        var cropHeight = Math.Min((int)Math.Round(desiredCropHeight), rotatedDimensions.Height);
        var cropRectangle = new Rectangle(
            Math.Clamp((int)Math.Round(cropX), 0, Math.Max(0, rotatedDimensions.Width - cropWidth)),
            Math.Clamp((int)Math.Round(cropY), 0, Math.Max(0, rotatedDimensions.Height - cropHeight)),
            cropWidth,
            cropHeight);

        var leftMargin = minFaceX - cropRectangle.X;
        var rightMargin = (cropRectangle.X + cropRectangle.Width) - maxFaceX;
        var topMargin = minFaceY - cropRectangle.Y;
        if (leftMargin < specification.MinimumSideMargin
            || rightMargin < specification.MinimumSideMargin
            || topMargin < specification.MinimumTopMargin)
        {
            return Result.Failure<(Rectangle CropRectangle, float AcceptedHeadWidthRatio, float AcceptedEyeLineFromTopRatio), PipelineError>(
                new GeometryError("Crop margins are smaller than the required profile margins.", "profile-portrait"));
        }

        var transformedLandmarks = TransformLandmarks(rotatedLandmarks, cropRectangle, specification.OutputDimensions);
        var transformedFaceContour = transformedLandmarks.Points.Take(17).ToArray();
        var acceptedHeadWidthRatio =
            (transformedFaceContour.Max(point => point.X) - transformedFaceContour.Min(point => point.X))
            / specification.OutputDimensions.Width;
        var acceptedEyeLineFromTopRatio =
            ((transformedLandmarks.LeftEyeCenter.Y + transformedLandmarks.RightEyeCenter.Y) / 2f)
            / specification.OutputDimensions.Height;

        if (acceptedHeadWidthRatio < specification.MinimumHeadWidthRatio
            || acceptedHeadWidthRatio > specification.MaximumHeadWidthRatio)
        {
            return Result.Failure<(Rectangle CropRectangle, float AcceptedHeadWidthRatio, float AcceptedEyeLineFromTopRatio), PipelineError>(
                new ValidationError(
                    $"Solved head width ratio {acceptedHeadWidthRatio:F3} is outside the allowed range {specification.MinimumHeadWidthRatio:F3}-{specification.MaximumHeadWidthRatio:F3}.",
                    "profile-portrait"));
        }

        if (Math.Abs(acceptedEyeLineFromTopRatio - specification.TargetEyeLineFromTopRatio) > 0.03f)
        {
            return Result.Failure<(Rectangle CropRectangle, float AcceptedHeadWidthRatio, float AcceptedEyeLineFromTopRatio), PipelineError>(
                new ValidationError(
                    $"Solved eye line ratio {acceptedEyeLineFromTopRatio:F3} does not satisfy target {specification.TargetEyeLineFromTopRatio:F3}.",
                    "profile-portrait"));
        }

        return Result.Success<(Rectangle CropRectangle, float AcceptedHeadWidthRatio, float AcceptedEyeLineFromTopRatio), PipelineError>(
            (cropRectangle, acceptedHeadWidthRatio, acceptedEyeLineFromTopRatio));
    }

    internal static FaceLandmarks68 RotateLandmarks(
        FaceLandmarks68 landmarks,
        float rotationDegrees,
        ImageDimensions sourceDimensions,
        ImageDimensions rotatedDimensions)
    {
        if (Math.Abs(rotationDegrees) <= 0.1f)
        {
            return landmarks;
        }

        var angle = rotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var oldCenterX = sourceDimensions.Width / 2.0;
        var oldCenterY = sourceDimensions.Height / 2.0;
        var newCenterX = rotatedDimensions.Width / 2.0;
        var newCenterY = rotatedDimensions.Height / 2.0;

        return new FaceLandmarks68(landmarks.Points
            .Select(point =>
            {
                var x = point.X - oldCenterX;
                var y = point.Y - oldCenterY;
                var rotatedX = x * cos - y * sin;
                var rotatedY = x * sin + y * cos;
                return new Point2D((float)(rotatedX + newCenterX), (float)(rotatedY + newCenterY));
            })
            .ToList());
    }

    internal static FaceLandmarks68 TransformLandmarks(
        FaceLandmarks68 rotatedLandmarks,
        Rectangle cropRectangle,
        ImageDimensions outputDimensions)
    {
        var scaleX = (float)outputDimensions.Width / cropRectangle.Width;
        var scaleY = (float)outputDimensions.Height / cropRectangle.Height;

        return new FaceLandmarks68(rotatedLandmarks.Points
            .Select(point => new Point2D(
                (point.X - cropRectangle.X) * scaleX,
                (point.Y - cropRectangle.Y) * scaleY))
            .ToList());
    }

    private static float Distance(Point2D left, Point2D right)
    {
        var dx = right.X - left.X;
        var dy = right.Y - left.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }
}

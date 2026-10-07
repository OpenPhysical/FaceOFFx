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
    /// Solves a source-supported portrait plan and records its geometry evidence.
    /// </summary>
    public static Result<PortraitPlan, PipelineError> Solve(
        ImageDimensions sourceDimensions,
        FaceLandmarks68 sourceLandmarks,
        PortraitSpecification specification,
        AnatomicalGeometry? sourceAnatomy = null)
    {
        if (sourceLandmarks?.Points is null || !sourceLandmarks.IsValid || sourceLandmarks.Points.Any(point =>
                point is null || !float.IsFinite(point.X) || !float.IsFinite(point.Y)))
        {
            return Result.Failure<PortraitPlan, PipelineError>(
                new GeometryError("Valid 68-point landmarks are required for portrait solving.", "profile-portrait"));
        }

        if (sourceDimensions is null || specification?.OutputDimensions is null ||
            specification.HeadWidthCandidateRatios is null ||
            sourceDimensions.Width <= 0 || sourceDimensions.Height <= 0 ||
            specification.OutputDimensions.Width <= 0 || specification.OutputDimensions.Height <= 0 ||
            (long)sourceDimensions.Width * sourceDimensions.Height > int.MaxValue ||
            (long)specification.OutputDimensions.Width * specification.OutputDimensions.Height > int.MaxValue ||
            specification.HeadWidthCandidateRatios.Count == 0 ||
            !Enum.IsDefined(specification.FaceRegion) ||
            specification.HeadWidthCandidateRatios.Any(ratio => !float.IsFinite(ratio) || ratio <= 0 || ratio > 1) ||
            !float.IsFinite(specification.MinimumHeadWidthRatio) || specification.MinimumHeadWidthRatio <= 0 ||
            !float.IsFinite(specification.MaximumHeadWidthRatio) || specification.MaximumHeadWidthRatio > 1 ||
            specification.MinimumHeadWidthRatio > specification.MaximumHeadWidthRatio ||
            !float.IsFinite(specification.TargetEyeLineFromTopRatio) || specification.TargetEyeLineFromTopRatio is < 0 or > 1 ||
            !float.IsFinite(specification.MaximumRotationDegrees) || specification.MaximumRotationDegrees is < 0 or > 180 ||
            specification.MinimumInterPupillaryDistance < 0 || specification.MinimumTopMargin < 0 || specification.MinimumSideMargin < 0)
            return Failure("Positive dimensions and finite portrait candidate controls are required.");
        if (sourceLandmarks.Points.Any(point => point.X < 0 || point.Y < 0 ||
                point.X > sourceDimensions.Width || point.Y > sourceDimensions.Height))
            return Failure("Facial landmarks must be supported by the original source image.");
        if (sourceAnatomy != null &&
            ((sourceAnatomy.LeftEarAttachment is null) != (sourceAnatomy.RightEarAttachment is null) ||
             (sourceAnatomy.LeftEarAttachment != null || sourceAnatomy.Crown != null) && string.IsNullOrWhiteSpace(sourceAnatomy.MeasurementSource) ||
             new[] { sourceAnatomy.LeftEarAttachment, sourceAnatomy.RightEarAttachment, sourceAnatomy.Crown }
                 .Any(point => point != null && (!float.IsFinite(point.X) || !float.IsFinite(point.Y) ||
                     point.X < 0 || point.Y < 0 || point.X > sourceDimensions.Width || point.Y > sourceDimensions.Height))))
            return Failure("Anatomical measurements require paired ear attachments, source-supported points, and measurement provenance.");
        if (specification.EnforcePivGeometry && specification.OutputDimensions.Width <= 420)
            return Failure("A PIV candidate requires an output image width greater than 420 pixels.");

        var interPupillaryDistance = Distance(sourceLandmarks.LeftEyeCenter, sourceLandmarks.RightEyeCenter);
        if (interPupillaryDistance < specification.MinimumInterPupillaryDistance)
        {
            return Result.Failure<PortraitPlan, PipelineError>(
                new ValidationError(
                    $"Inter-pupillary distance {interPupillaryDistance:F1}px is below the required minimum {specification.MinimumInterPupillaryDistance}px.",
                    "profile-portrait"));
        }

        var sourceRollDegrees = (float)(Math.Atan2(sourceLandmarks.RightEyeCenter.Y - sourceLandmarks.LeftEyeCenter.Y,
            sourceLandmarks.RightEyeCenter.X - sourceLandmarks.LeftEyeCenter.X) * 180 / Math.PI);
        if (specification.EnforcePivGeometry && Math.Abs(sourceRollDegrees) > specification.MaximumRotationDegrees)
            return Failure($"Estimated source roll {sourceRollDegrees:F2} degrees exceeds the profile capture limit {specification.MaximumRotationDegrees:F2} degrees.");
        var rotationDegrees = Math.Clamp(-sourceRollDegrees,
            -specification.MaximumRotationDegrees, specification.MaximumRotationDegrees);
        var rotatedDimensions = RenderTransformMapBuilder.ComputeExpandedRotationDimensions(sourceDimensions, rotationDegrees);
        if (rotatedDimensions.Width <= 0 || rotatedDimensions.Height <= 0 ||
            (long)rotatedDimensions.Width * rotatedDimensions.Height > int.MaxValue)
            return Failure("The rotated source canvas exceeds the supported pixel dimensions.");
        var rotatedLandmarks = RotateLandmarks(sourceLandmarks, rotationDegrees, sourceDimensions, rotatedDimensions);
        var rotationMap = RenderTransformMapBuilder.CreateRotateCropResize(sourceDimensions, rotationDegrees, rotatedDimensions,
            new Rectangle(0, 0, rotatedDimensions.Width, rotatedDimensions.Height), rotatedDimensions);
        if (rotationMap.IsFailure) return Result.Failure<PortraitPlan, PipelineError>(rotationMap.Error);
        var rotatedAnatomy = sourceAnatomy is null ? null : sourceAnatomy with
        {
            LeftEarAttachment = sourceAnatomy.LeftEarAttachment is null ? null : rotationMap.Value.MapSourceToOutput(sourceAnatomy.LeftEarAttachment),
            RightEarAttachment = sourceAnatomy.RightEarAttachment is null ? null : rotationMap.Value.MapSourceToOutput(sourceAnatomy.RightEarAttachment),
            Crown = sourceAnatomy.Crown is null ? null : rotationMap.Value.MapSourceToOutput(sourceAnatomy.Crown)
        };

        var traces = new List<CandidateTrace>();
        foreach (var requestedHeadWidthRatio in specification.HeadWidthCandidateRatios)
        {
            var candidateResult = TrySolveCandidate(rotatedDimensions, rotatedLandmarks, specification, requestedHeadWidthRatio, rotatedAnatomy, rotationMap.Value);
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

            var sourceSupported = transformMapResult.Value.OutputBoundsOnSource().All(point =>
                point.X >= -0.001f && point.Y >= -0.001f &&
                point.X <= sourceDimensions.Width + 0.001f && point.Y <= sourceDimensions.Height + 0.001f);
            if (specification.EnforcePivGeometry && !sourceSupported)
            {
                traces.Add(new CandidateTrace(requestedHeadWidthRatio, Maybe<float>.None,
                    "The inverse crop polygon requires pixels beyond the original source boundary."));
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
                traces)
            {
                FaceRegion = specification.FaceRegion,
                GeometryEvidence = specification.EnforcePivGeometry
                    ? BuildEvidence(rotatedLandmarks, rotatedAnatomy, candidate.CropRectangle, specification,
                        sourceRollDegrees, sourceSupported)
                    : null
            });
        }

        return Result.Failure<PortraitPlan, PipelineError>(
            new GeometryError(
                $"No portrait crop satisfied the profile requirements. {string.Join(" ", traces.Select(trace => trace.Summary))}",
                "profile-portrait"));

        static Result<PortraitPlan, PipelineError> Failure(string message) =>
            Result.Failure<PortraitPlan, PipelineError>(new GeometryError(message, "profile-portrait"));
    }

    private static Result<(Rectangle CropRectangle, float AcceptedHeadWidthRatio, float AcceptedEyeLineFromTopRatio), PipelineError> TrySolveCandidate(
        ImageDimensions rotatedDimensions,
        FaceLandmarks68 rotatedLandmarks,
        PortraitSpecification specification,
        float requestedHeadWidthRatio,
        AnatomicalGeometry? anatomy,
        RenderTransformMap rotationMap)
    {
        var faceContour = rotatedLandmarks.Points.Take(17).ToArray();
        var minFaceX = faceContour.Min(point => point.X);
        var maxFaceX = faceContour.Max(point => point.X);
        var minFaceY = faceContour.Min(point => point.Y);
        var jawWidth = maxFaceX - minFaceX;
        var currentHeadWidth = anatomy?.LeftEarAttachment is null ? jawWidth :
            Math.Abs(anatomy.RightEarAttachment!.X - anatomy.LeftEarAttachment.X);
        if (currentHeadWidth <= 0f || !float.IsFinite(currentHeadWidth))
        {
            return Result.Failure<(Rectangle CropRectangle, float AcceptedHeadWidthRatio, float AcceptedEyeLineFromTopRatio), PipelineError>(
                new GeometryError("Cannot solve portrait crop from zero-width face landmarks.", "profile-portrait"));
        }

        var desiredCropWidth = currentHeadWidth / requestedHeadWidthRatio;
        var desiredCropHeight = desiredCropWidth * specification.OutputDimensions.Height / specification.OutputDimensions.Width;

        if (specification.EnforcePivGeometry)
        {
            var divisor = GreatestCommonDivisor(specification.OutputDimensions.Width, specification.OutputDimensions.Height);
            var widthUnit = specification.OutputDimensions.Width / divisor;
            var heightUnit = specification.OutputDimensions.Height / divisor;
            var units = MathF.Floor(desiredCropWidth / widthUnit);
            desiredCropWidth = units * widthUnit;
            desiredCropHeight = units * heightUnit;
            if (desiredCropWidth < specification.OutputDimensions.Width || desiredCropHeight < specification.OutputDimensions.Height)
                return CandidateFailure("The candidate requires digital enlargement; a higher-resolution source or smaller supported canvas is required.");
            if (desiredCropWidth > rotatedDimensions.Width || desiredCropHeight > rotatedDimensions.Height)
                return CandidateFailure("The complete portrait canvas requires more original source area.");
        }
        else
        {
            desiredCropWidth = Math.Min(desiredCropWidth, rotatedDimensions.Width);
            desiredCropHeight = Math.Min(desiredCropHeight, rotatedDimensions.Height);
        }

        var eyeCenterX = (rotatedLandmarks.LeftEyeCenter.X + rotatedLandmarks.RightEyeCenter.X) / 2f;
        var eyeCenterY = (rotatedLandmarks.LeftEyeCenter.Y + rotatedLandmarks.RightEyeCenter.Y) / 2f;

        var horizontalCenter = specification.EnforcePivGeometry
            ? (rotatedLandmarks.Points.Skip(27).Take(4).Average(point => point.X)
                + new[] { 48, 51, 54, 57 }.Average(index => rotatedLandmarks.Points[index].X)) / 2
            : eyeCenterX;
        var cropX = horizontalCenter - desiredCropWidth / 2f;
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

        var scale = (float)specification.OutputDimensions.Width / cropRectangle.Width;
        var crown = EstimateCrown(rotatedLandmarks, anatomy);
        var headLeft = specification.EnforcePivGeometry ? minFaceX - jawWidth * 0.1f : minFaceX;
        var headRight = specification.EnforcePivGeometry ? maxFaceX + jawWidth * 0.1f : maxFaceX;
        if (anatomy?.LeftEarAttachment != null)
        {
            headLeft = Math.Min(headLeft, Math.Min(anatomy.LeftEarAttachment.X, anatomy.RightEarAttachment!.X));
            headRight = Math.Max(headRight, Math.Max(anatomy.LeftEarAttachment.X, anatomy.RightEarAttachment!.X));
        }
        if (specification.EnforcePivGeometry)
        {
            var supported = SourceSupportedCrop.Find(cropRectangle, rotationMap, headLeft, headRight, crown.Y,
                rotatedLandmarks.Points[8].Y, eyeCenterY, horizontalCenter, scale,
                specification.MinimumSideMargin, specification.MinimumTopMargin);
            if (supported.HasValue) cropRectangle = supported.Value;
        }
        var leftMargin = (headLeft - cropRectangle.X) * (specification.EnforcePivGeometry ? scale : 1);
        var rightMargin = (cropRectangle.Right - headRight) * (specification.EnforcePivGeometry ? scale : 1);
        var topMargin = ((specification.EnforcePivGeometry ? crown.Y : minFaceY) - cropRectangle.Y)
            * (specification.EnforcePivGeometry ? scale : 1);
        if (leftMargin < specification.MinimumSideMargin
            || rightMargin < specification.MinimumSideMargin
            || topMargin < specification.MinimumTopMargin)
        {
            return Result.Failure<(Rectangle CropRectangle, float AcceptedHeadWidthRatio, float AcceptedEyeLineFromTopRatio), PipelineError>(
                new GeometryError($"Crop margins in output pixels: left {leftMargin:F1}, right {rightMargin:F1}, top {topMargin:F1}; required sides {specification.MinimumSideMargin}, top {specification.MinimumTopMargin}. Crown basis: {(anatomy?.Crown is null ? "estimated" : "measured")}, crown Y {crown.Y:F1}, crop {cropRectangle.X},{cropRectangle.Y},{cropRectangle.Width},{cropRectangle.Height}.", "profile-portrait"));
        }

        var transformedLandmarks = TransformLandmarks(rotatedLandmarks, cropRectangle, specification.OutputDimensions);
        var acceptedHeadWidthRatio = currentHeadWidth / cropRectangle.Width;
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

        if (!specification.EnforcePivGeometry && Math.Abs(acceptedEyeLineFromTopRatio - specification.TargetEyeLineFromTopRatio) > 0.03f)
        {
            return Result.Failure<(Rectangle CropRectangle, float AcceptedHeadWidthRatio, float AcceptedEyeLineFromTopRatio), PipelineError>(
                new ValidationError(
                    $"Solved eye line ratio {acceptedEyeLineFromTopRatio:F3} does not satisfy target {specification.TargetEyeLineFromTopRatio:F3}.",
                    "profile-portrait"));
        }

        if (specification.EnforcePivGeometry)
        {
            var outputHeadWidth = currentHeadWidth * scale;
            var headHeight = (rotatedLandmarks.Points[8].Y - crown.Y) * scale;
            if (currentHeadWidth < 240 || outputHeadWidth < 240 || acceptedHeadWidthRatio > 4f / 7f)
                return CandidateFailure("The PIV candidate requires source and output anatomical-width evidence of at least 240 pixels and image-to-head width of at least 7:4.");
            if (headHeight <= 0 || headHeight > specification.OutputDimensions.Height * 0.8f ||
                transformedLandmarks.Points[8].Y > specification.OutputDimensions.Height - specification.MinimumSideMargin)
                return CandidateFailure("The complete crown-to-chin estimate requires a frame with head height at most 80% and a retained chin margin.");
            if (acceptedEyeLineFromTopRatio is < 0.3f or > 0.5f)
                return CandidateFailure("The eye line must be between 30% and 50% down from the top of the PIV candidate.");
            var noseX = transformedLandmarks.Points.Skip(27).Take(4).Average(point => point.X);
            var mouthX = new[] { 48, 51, 54, 57 }.Average(index => transformedLandmarks.Points[index].X);
            if (Math.Abs(noseX - mouthX) > 8 || Math.Abs((noseX + mouthX) / 2 - specification.OutputDimensions.Width / 2f) > 10)
                return CandidateFailure("The approximate nose and mouth centerline requires a more frontal, centered source photograph.");
        }

        return Result.Success<(Rectangle CropRectangle, float AcceptedHeadWidthRatio, float AcceptedEyeLineFromTopRatio), PipelineError>(
            (cropRectangle, acceptedHeadWidthRatio, acceptedEyeLineFromTopRatio));

        static Result<(Rectangle CropRectangle, float AcceptedHeadWidthRatio, float AcceptedEyeLineFromTopRatio), PipelineError> CandidateFailure(string message) =>
            Result.Failure<(Rectangle, float, float), PipelineError>(new GeometryError(message, "profile-portrait"));
    }

    private static Point2D EstimateCrown(FaceLandmarks68 landmarks, AnatomicalGeometry? anatomy)
    {
        if (anatomy?.Crown != null) return anatomy.Crown;
        var browTop = landmarks.Points.Skip(17).Take(10).Min(point => point.Y);
        var browToChin = landmarks.Points[8].Y - browTop;
        return new Point2D((landmarks.Points[0].X + landmarks.Points[16].X) / 2, browTop - browToChin * 0.8f);
    }

    private static PivGeometryEvidence BuildEvidence(FaceLandmarks68 landmarks, AnatomicalGeometry? anatomy,
        Rectangle crop, PortraitSpecification specification, float sourceRoll, bool sourceSupported)
    {
        var jawWidth = landmarks.Points.Take(17).Max(point => point.X) - landmarks.Points.Take(17).Min(point => point.X);
        var scale = (float)specification.OutputDimensions.Width / crop.Width;
        var headWidth = anatomy?.LeftEarAttachment is null ? jawWidth : Math.Abs(anatomy.RightEarAttachment!.X - anatomy.LeftEarAttachment.X);
        var headHeight = landmarks.Points[8].Y - EstimateCrown(landmarks, anatomy).Y;
        var requirements = new List<string>
        {
            "Verify the acquisition calibration, optical resolution, camera distance, and square-pixel source geometry.",
            "Verify frontal yaw and pitch, neutral expression, illumination, focus, complete head and shoulder coverage, and original-capture history."
        };
        if (anatomy?.LeftEarAttachment is null)
            requirements.Add("Measure CC at the ear-to-head attachments in source and output; the current width is a jaw-contour estimate.");
        if (anatomy?.Crown is null)
            requirements.Add("Verify the true hair crown and full head outline; the current crown-to-chin height is a landmark-based estimate.");
        return new PivGeometryEvidence(jawWidth, jawWidth * scale,
            new AnatomicalDimensionEvidence(headWidth, headWidth * scale,
                anatomy?.LeftEarAttachment is null ? GeometryEvidenceStatus.Estimated : GeometryEvidenceStatus.Measured,
                anatomy?.LeftEarAttachment is null ? "Jaw contour landmarks 0-16" : anatomy.MeasurementSource!),
            new AnatomicalDimensionEvidence(headHeight, headHeight * scale,
                anatomy?.Crown is null ? GeometryEvidenceStatus.Estimated : GeometryEvidenceStatus.Measured,
                anatomy?.Crown is null ? "Eyebrow-to-chin extrapolation, crown offset 0.8" : anatomy.MeasurementSource!),
            sourceRoll, ((landmarks.LeftEyeCenter.Y + landmarks.RightEyeCenter.Y) / 2 - crop.Y) * scale / specification.OutputDimensions.Height,
            scale, sourceSupported, requirements.AsReadOnly());
    }

    private static int GreatestCommonDivisor(int left, int right)
    {
        while (right != 0) { var remainder = left % right; left = right; right = remainder; }
        return left;
    }

    internal static FaceLandmarks68 RotateLandmarks(
        FaceLandmarks68 landmarks,
        float rotationDegrees,
        ImageDimensions sourceDimensions,
        ImageDimensions rotatedDimensions)
    {
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

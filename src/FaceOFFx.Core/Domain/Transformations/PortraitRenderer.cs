using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>
/// Renders solved portrait plans into output images and landmarks.
/// </summary>
[PublicAPI]
public static class PortraitRenderer
{
    /// <summary>
    /// Renders a solved portrait plan into an output image, landmarks, and ROI set.
    /// </summary>
    public static Result<RenderedPortrait, PipelineError> Render(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        PortraitPlan plan)
    {
        using var rotatedImage = Math.Abs(plan.RotationDegrees) > 0.1f
            ? sourceImage.Clone(ctx => ctx.Rotate(plan.RotationDegrees))
            : sourceImage.Clone();

        var rotatedLandmarks = PortraitPlanSolver.RotateLandmarks(
            geometry.SourceLandmarks,
            plan.RotationDegrees,
            new ImageDimensions(sourceImage.Width, sourceImage.Height),
            plan.RotatedSourceDimensions);

        var renderedImage = rotatedImage.Clone(ctx =>
        {
            ctx.Crop(plan.CropRectangle);
            ctx.Resize(plan.OutputDimensions.Width, plan.OutputDimensions.Height);
        });

        var outputLandmarks = PortraitPlanSolver.TransformLandmarks(
            rotatedLandmarks,
            plan.CropRectangle,
            plan.OutputDimensions);

        var roiResult = outputLandmarks
            .CalculateRoiSet(plan.OutputDimensions.Width, plan.OutputDimensions.Height)
            .ToPipelineResult(error => new GeometryError(error, "profile-roi"));
        if (roiResult.IsFailure)
        {
            renderedImage.Dispose();
            return Result.Failure<RenderedPortrait, PipelineError>(roiResult.Error);
        }

        return Result.Success<RenderedPortrait, PipelineError>(new RenderedPortrait(
            renderedImage,
            outputLandmarks,
            roiResult.Value,
            plan));
    }
}

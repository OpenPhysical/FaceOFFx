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
        // One resampling pass preserves detail and uses the same coordinates as source-support verification.
        var renderedImage = sourceImage.Clone(ctx => ctx.Transform(
            sourceImage.Bounds,
            plan.TransformMap.SourceToOutput.ToMatrix(),
            new Size(plan.OutputDimensions.Width, plan.OutputDimensions.Height),
            KnownResamplers.Lanczos3));

        var outputLandmarks = new FaceLandmarks68(geometry.SourceLandmarks.Points
            .Select(plan.TransformMap.MapSourceToOutput).ToArray());

        var roiResult = AnatomicalFaceRoi
            .Create(outputLandmarks, plan.OutputDimensions.Width, plan.OutputDimensions.Height, plan.FaceRegion)
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

using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Diagnostics.Cli.Services;

internal static class OverlayRenderer
{
    private const int ChipReviewScale = 4;

    public static Image<Rgba32> RenderGross(
        Image<Rgba32> sourceImage,
        DetectionAnalysisResult detection,
        bool showBoundingBox)
    {
        return sourceImage.Clone(ctx =>
        {
            if (showBoundingBox)
            {
                foreach (var face in detection.Faces)
                {
                    ctx.Draw(Color.Yellow, 3f, new RectangleF(face.X, face.Y, face.Width, face.Height));
                }
            }

            foreach (var point in detection.CoarseLandmarks)
            {
                ctx.Fill(Color.Orange, new EllipsePolygon(point.X, point.Y, 3f));
            }

            if (detection.ChipPolygon.Count >= 4)
            {
                ctx.DrawPolygon(
                    Color.Cyan,
                    3f,
                    detection.ChipPolygon.Select(point => new PointF(point.X, point.Y)).ToArray());
            }
        });
    }

    public static Image<Rgba32> RenderFull(
        Image<Rgba32> sourceImage,
        DetectionAnalysisResult detection,
        OverlayAnalysisResult? overlay,
        bool showBoundingBox,
        bool showGuides)
    {
        using var gross = RenderGross(sourceImage, detection, showBoundingBox);
        return gross.Clone(ctx =>
        {
            foreach (var point in detection.RawLandmarks)
            {
                ctx.Fill(Color.DeepSkyBlue, new EllipsePolygon(point.X, point.Y, 2f));
            }

            if (overlay is null)
            {
                return;
            }

            foreach (var point in overlay.ProjectedLandmarks)
            {
                ctx.Fill(Color.Red, new EllipsePolygon(point.X, point.Y, 1.5f));
            }

            if (overlay.CropPolygon.Count >= 4)
            {
                ctx.DrawPolygon(
                    Color.LimeGreen,
                    3f,
                    overlay.CropPolygon.Select(point => new PointF(point.X, point.Y)).ToArray());
            }

            if (showGuides && string.Equals(overlay.ProfileId, "piv", StringComparison.OrdinalIgnoreCase))
            {
                var centerX = sourceImage.Width / 2f;
                ctx.DrawLine(Color.MediumPurple, 2f, new PointF(centerX, 0), new PointF(centerX, sourceImage.Height));
            }
        });
    }

    public static Image<Rgba32> RenderChipReview(
        Image<Rgba32> chipImage,
        FaceLandmarks68 chipLandmarks)
    {
        var reviewWidth = chipImage.Width * ChipReviewScale;
        var reviewHeight = chipImage.Height * ChipReviewScale;
        return chipImage.Clone(ctx =>
        {
            ctx.Resize(new ResizeOptions
            {
                Size = new Size(reviewWidth, reviewHeight),
                Sampler = KnownResamplers.NearestNeighbor
            });

            foreach (var point in chipLandmarks.Points)
            {
                ctx.Fill(
                    Color.DeepSkyBlue,
                    new EllipsePolygon(
                        point.X * ChipReviewScale,
                        point.Y * ChipReviewScale,
                        3f));
            }
        });
    }
}

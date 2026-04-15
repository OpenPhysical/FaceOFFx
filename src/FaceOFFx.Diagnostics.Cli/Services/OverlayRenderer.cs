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
    private const int ReviewMaxLongEdge = 1654;

    public static Image<Rgba32> RenderGross(
        Image<Rgba32> sourceImage,
        DetectionAnalysisResult detection,
        bool showBoundingBox)
    {
        var (reviewImage, scale) = CreateReviewCanvas(sourceImage);
        return reviewImage.Clone(ctx =>
        {
            if (showBoundingBox)
            {
                foreach (var face in detection.Faces)
                {
                    ctx.Draw(
                        Color.Yellow,
                        3f,
                        new RectangleF(
                            face.X * scale,
                            face.Y * scale,
                            face.Width * scale,
                            face.Height * scale));
                }
            }

            foreach (var point in detection.CoarseLandmarks)
            {
                ctx.Fill(Color.Orange, new EllipsePolygon(point.X * scale, point.Y * scale, 3f));
            }

            if (detection.ChipPolygon.Count >= 4)
            {
                ctx.DrawPolygon(
                    Color.Cyan,
                    3f,
                    detection.ChipPolygon.Select(point => new PointF(point.X * scale, point.Y * scale)).ToArray());
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
            var scale = gross.Width / (float)sourceImage.Width;

            foreach (var point in detection.RawLandmarks)
            {
                ctx.Fill(Color.DeepSkyBlue, new EllipsePolygon(point.X * scale, point.Y * scale, 3f));
            }

            if (overlay is null)
            {
                return;
            }

            foreach (var point in overlay.ProjectedLandmarks)
            {
                ctx.Fill(Color.Red, new EllipsePolygon(point.X * scale, point.Y * scale, 2.5f));
            }

            if (overlay.CropPolygon.Count >= 4)
            {
                ctx.DrawPolygon(
                    Color.LimeGreen,
                    3f,
                    overlay.CropPolygon.Select(point => new PointF(point.X * scale, point.Y * scale)).ToArray());
            }

            if (showGuides && string.Equals(overlay.ProfileId, "piv", StringComparison.OrdinalIgnoreCase))
            {
                var centerX = gross.Width / 2f;
                ctx.DrawLine(Color.MediumPurple, 2f, new PointF(centerX, 0), new PointF(centerX, gross.Height));
            }
        });
    }

    public static Image<Rgba32> RenderFineOnly(
        Image<Rgba32> sourceImage,
        DetectionAnalysisResult detection)
    {
        var (reviewImage, scale) = CreateReviewCanvas(sourceImage);
        return reviewImage.Clone(ctx =>
        {
            foreach (var point in detection.RawLandmarks)
            {
                ctx.Fill(Color.DeepSkyBlue, new EllipsePolygon(point.X * scale, point.Y * scale, 3f));
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

    private static (Image<Rgba32> Image, float Scale) CreateReviewCanvas(Image<Rgba32> sourceImage)
    {
        var longEdge = Math.Max(sourceImage.Width, sourceImage.Height);
        var scale = Math.Min(1f, ReviewMaxLongEdge / (float)longEdge);
        var reviewWidth = Math.Max(1, (int)MathF.Round(sourceImage.Width * scale));
        var reviewHeight = Math.Max(1, (int)MathF.Round(sourceImage.Height * scale));
        var review = sourceImage.Clone(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(reviewWidth, reviewHeight),
            Sampler = KnownResamplers.Bicubic,
            Mode = ResizeMode.Stretch
        }));
        return (review, scale);
    }
}

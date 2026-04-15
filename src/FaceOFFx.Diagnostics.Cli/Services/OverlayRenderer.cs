using FaceOFFx.Core.Domain.Detection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Diagnostics.Cli.Services;

internal static class OverlayRenderer
{
    private const int ReviewMaxLongEdge = 1654;
    private const int FineChipScale = 4;

    public static Image<Rgba32> RenderOriginal(Image<Rgba32> sourceImage)
    {
        var (reviewImage, _) = CreateReviewCanvas(sourceImage);
        return reviewImage;
    }

    public static Image<Rgba32> RenderCoarse(
        Image<Rgba32> sourceImage,
        DetectionAnalysisResult detection)
    {
        var (reviewImage, scale) = CreateReviewCanvas(sourceImage);
        return reviewImage.Clone(ctx =>
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

            foreach (var point in detection.CoarseLandmarks)
            {
                ctx.Fill(Color.Orange, new EllipsePolygon(point.X * scale, point.Y * scale, 3f));
            }
        });
    }

    public static Image<Rgba32> RenderChipLocate(
        Image<Rgba32> sourceImage,
        DetectionAnalysisResult detection)
    {
        var (reviewImage, scale) = CreateReviewCanvas(sourceImage);
        return reviewImage.Clone(ctx =>
        {
            if (detection.ChipPolygon.Count >= 4)
            {
                ctx.DrawPolygon(
                    Color.Cyan,
                    3f,
                    detection.ChipPolygon
                        .Select(point => new PointF(point.X * scale, point.Y * scale))
                        .ToArray());
            }
        });
    }

    public static Image<Rgba32> RenderFineChip(
        Image<Rgba32> chipImage,
        FaceLandmarks68 chipLandmarks)
    {
        var reviewWidth = chipImage.Width * FineChipScale;
        var reviewHeight = chipImage.Height * FineChipScale;
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
                        point.X * FineChipScale,
                        point.Y * FineChipScale,
                        3f));
            }
        });
    }

    public static Image<Rgba32> RenderFineSource(
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

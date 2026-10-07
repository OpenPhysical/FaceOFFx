using FaceOFFx.Core.Domain.Common;

namespace FaceOFFx.Core.Domain.Detection;

/// <summary>Builds one fixed face-centered region from detected features and the selected anatomical estimate policy.</summary>
public static class AnatomicalFaceRoi
{
    /// <summary>Fixed forehead extension above the eyebrows, relative to brow-to-chin height.</summary>
    public const float ForeheadExtensionRatio = 0.65f;
    /// <summary>Fixed lateral ear extension relative to jaw width.</summary>
    public const float EarExtensionRatio = 0.12f;
    /// <summary>Lower ear-envelope extent relative to eye-to-chin height.</summary>
    public const float EarLowerExtentRatio = 0.55f;
    /// <summary>Fixed localization margin relative to jaw width.</summary>
    public const float PaddingRatio = 0.03f;

    /// <summary>Constructs a facial-region estimate independently of the encoding budget.</summary>
    /// <remarks>Each policy has a fixed review scope. Full-head framing and source capture use separate geometry evidence.</remarks>
    public static Result<FacialRoiSet> Create(FaceLandmarks68 landmarks, int width, int height,
        PivFaceRegion faceRegion = PivFaceRegion.LandmarkFace)
    {
        if (!Enum.IsDefined(faceRegion))
            return Result.Failure<FacialRoiSet>("Select the LandmarkFace or ExtendedFace region policy.");
        if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue)
            return Result.Failure<FacialRoiSet>("Positive, supported image dimensions are required for the facial mask.");
        if (landmarks?.Points is null || !landmarks.IsValid || landmarks.Points.Any(point =>
                point is null || !float.IsFinite(point.X) || !float.IsFinite(point.Y)))
            return Result.Failure<FacialRoiSet>("A finite 68-point landmark set is required for the facial mask.");

        var eye = (landmarks.LeftEyeCenter + landmarks.RightEyeCenter) * 0.5f;
        var axis = landmarks.RightEyeCenter - landmarks.LeftEyeCenter;
        var eyeDistance = axis.DistanceTo(Point2D.Zero);
        if (!float.IsFinite(eyeDistance) || eyeDistance <= 0)
            return Result.Failure<FacialRoiSet>("Distinct eye centers are required for the facial mask.");
        var ux = axis.X / eyeDistance;
        var uy = axis.Y / eyeDistance;
        Point2D Local(Point2D point) => new((point.X - eye.X) * ux + (point.Y - eye.Y) * uy,
            -(point.X - eye.X) * uy + (point.Y - eye.Y) * ux);
        Point2D ImagePoint(Point2D point) => new(eye.X + point.X * ux - point.Y * uy,
            eye.Y + point.X * uy + point.Y * ux);

        var local = landmarks.Points.Select(Local).ToArray();
        var jaw = local.Take(17).ToArray();
        var left = jaw.Min(point => point.X);
        var right = jaw.Max(point => point.X);
        var jawWidth = right - left;
        var browTop = local.Skip(17).Take(10).Min(point => point.Y);
        var browToChin = local[8].Y - browTop;
        if (jawWidth <= 0 || browToChin <= 0 || local[8].Y <= 0)
            return Result.Failure<FacialRoiSet>("A positive jaw width and upright brow-to-chin geometry are required for the facial mask.");

        var boundary = new List<Point2D>(landmarks.Points);
        if (faceRegion == PivFaceRegion.ExtendedFace)
        {
            var foreheadTop = browTop - ForeheadExtensionRatio * browToChin;
            var templeLevel = Math.Min(local[0].Y, local[16].Y);
            for (var step = 0; step <= 32; step++)
            {
                var fraction = -1f + step / 16f;
                var capHeight = MathF.Sqrt(MathF.Max(0, 1 - fraction * fraction));
                boundary.Add(ImagePoint(new Point2D((left + right) / 2 + fraction * jawWidth / 2,
                    templeLevel - (templeLevel - foreheadTop) * capHeight)));
            }

            // Localized ear envelopes preserve lateral anatomy without extending the chin margin.
            var earTop = -0.08f * browToChin;
            var earBottom = EarLowerExtentRatio * local[8].Y;
            for (var step = 0; step <= 16; step++)
            {
                var fraction = step / 16f;
                var earY = earTop + fraction * (earBottom - earTop);
                var extension = MathF.Sin(fraction * MathF.PI) * jawWidth * EarExtensionRatio;
                boundary.Add(ImagePoint(new Point2D(left - extension, earY)));
                boundary.Add(ImagePoint(new Point2D(right + extension, earY)));
            }
        }

        // The hull retains every detected facial feature; a fixed margin covers small localization errors.
        var hull = ConvexHull(boundary);
        var padding = MathF.Ceiling(jawWidth * PaddingRatio);
        var minX = hull.Min(point => point.X) - padding;
        var minY = hull.Min(point => point.Y) - padding;
        var maxX = hull.Max(point => point.X) + padding;
        var maxY = hull.Max(point => point.Y) + padding;
        if (minX < 0 || minY < 0 || maxX > width || maxY > height)
            return Result.Failure<FacialRoiSet>("The complete estimated facial boundary and its fixed margin require more source-supported crop space.");

        var pixels = new byte[checked(width * height)];
        var x0 = Math.Max(0, (int)MathF.Floor(minX));
        var y0 = Math.Max(0, (int)MathF.Floor(minY));
        var x1 = Math.Min(width, (int)MathF.Ceiling(maxX));
        var y1 = Math.Min(height, (int)MathF.Ceiling(maxY));
        for (var y = y0; y < y1; y++)
        for (var x = x0; x < x1; x++)
            if (WithinBoundary(hull, x + 0.5f, y + 0.5f, padding)) pixels[y * width + x] = 1;

        var mask = new FacialRoiMask(width, height, pixels);
        if (mask.PixelCount == 0 || landmarks.Points.Any(point => !mask.Contains((int)point.X, (int)point.Y)))
            return Result.Failure<FacialRoiSet>("The facial mask must cover every detected facial feature.");
        var extended = faceRegion == PivFaceRegion.ExtendedFace;
        var coverage = new FacialRoiCoverage(extended ? "jaw-forehead-ear-hull-v2" : "landmark-face-hull-v1",
            GeometryEvidenceStatus.Estimated, extended ? ForeheadExtensionRatio : 0, PaddingRatio,
            Array.AsReadOnly(hull.ToArray()), Array.AsReadOnly(new[] { extended
                ? "Review the natural hairline, complete forehead, cheeks, and full ear outlines against the source photograph; the extended boundary is a fixed anatomical estimate."
                : "Review the detected brows, eyes, nose, mouth, and complete jaw contour against the source; the innermost region is the 68-feature hull with a fixed localization margin." }))
        { FaceRegion = faceRegion };
        return Result.Success(new FacialRoiSet(new RoiRegion(extended ? "Extended facial estimate" : "Landmark facial region", 3,
            new RoiBoundingBox(x0, y0, x1 - x0, y1 - y0), Enumerable.Range(0, 68).ToArray()))
        { Mask = mask, Coverage = coverage });
    }

    private static List<Point2D> ConvexHull(IEnumerable<Point2D> points)
    {
        var sorted = points.Distinct().OrderBy(point => point.X).ThenBy(point => point.Y).ToArray();
        var lower = new List<Point2D>();
        foreach (var point in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], point) <= 0) lower.RemoveAt(lower.Count - 1);
            lower.Add(point);
        }
        var upper = new List<Point2D>();
        for (var index = sorted.Length - 1; index >= 0; index--)
        {
            var point = sorted[index];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], point) <= 0) upper.RemoveAt(upper.Count - 1);
            upper.Add(point);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static float Cross(Point2D a, Point2D b, Point2D c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool WithinBoundary(IReadOnlyList<Point2D> hull, float x, float y, float margin)
    {
        var inside = true;
        var minimumDistance = float.PositiveInfinity;
        for (var index = 0; index < hull.Count; index++)
        {
            var a = hull[index];
            var b = hull[(index + 1) % hull.Count];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            if (dx * (y - a.Y) - dy * (x - a.X) < 0) inside = false;
            var fraction = Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
            var distanceX = x - a.X - fraction * dx;
            var distanceY = y - a.Y - fraction * dy;
            minimumDistance = MathF.Min(minimumDistance, distanceX * distanceX + distanceY * distanceY);
        }
        return inside || minimumDistance <= margin * margin;
    }
}

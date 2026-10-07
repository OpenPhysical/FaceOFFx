using FaceOFFx.Core.Domain.Common;
using SixLabors.ImageSharp;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>Finds the nearest integer crop translation inside all original-pixel and portrait constraints.</summary>
internal static class SourceSupportedCrop
{
    private readonly record struct Plane(double X, double Y, double Maximum);

    internal static Maybe<Rectangle> Find(Rectangle preferred, RenderTransformMap rotationMap,
        float headLeft, float headRight, float crownY, float chinY, float eyeY, float centerX,
        float scale, int sideMargin, int topMargin)
    {
        var width = preferred.Width;
        var height = preferred.Height;
        var planes = new List<Plane>
        {
            new(-1, 0, 0), new(0, -1, 0),
            new(1, 0, rotationMap.OutputDimensions.Width - width),
            new(0, 1, rotationMap.OutputDimensions.Height - height),
            new(1, 0, headLeft - sideMargin / scale),
            new(-1, 0, width - headRight - sideMargin / scale),
            new(0, 1, crownY - topMargin / scale),
            new(0, -1, height - chinY - sideMargin / scale),
            new(0, 1, eyeY - 0.3f * height),
            new(0, -1, 0.5f * height - eyeY),
            new(1, 0, centerX - width / 2f + 10 / scale),
            new(-1, 0, width / 2f - centerX + 10 / scale)
        };
        var inverse = rotationMap.OutputToSource.ToMatrix();
        foreach (var corner in new[] { new Point2D(0, 0), new Point2D(width, 0), new Point2D(width, height), new Point2D(0, height) })
        {
            var xOffset = inverse.M11 * corner.X + inverse.M21 * corner.Y + inverse.M31;
            var yOffset = inverse.M12 * corner.X + inverse.M22 * corner.Y + inverse.M32;
            planes.Add(new Plane(-inverse.M11, -inverse.M21, xOffset));
            planes.Add(new Plane(inverse.M11, inverse.M21, rotationMap.SourceDimensions.Width - xOffset));
            planes.Add(new Plane(-inverse.M12, -inverse.M22, yOffset));
            planes.Add(new Plane(inverse.M12, inverse.M22, rotationMap.SourceDimensions.Height - yOffset));
        }
        if (Fits(preferred.X, preferred.Y, planes)) return Maybe<Rectangle>.From(preferred);

        var polygon = new List<Point2D>
        {
            new(0, 0), new(rotationMap.OutputDimensions.Width - width, 0),
            new(rotationMap.OutputDimensions.Width - width, rotationMap.OutputDimensions.Height - height),
            new(0, rotationMap.OutputDimensions.Height - height)
        };
        foreach (var plane in planes)
        {
            polygon = Clip(polygon, plane);
            if (polygon.Count == 0) return Maybe<Rectangle>.None;
        }

        // Integer crop origins preserve the renderer's pixel grid; inspect neighbors of closest boundary points.
        var proposals = new List<Point2D>(polygon);
        var preferredPoint = new Point2D(preferred.X, preferred.Y);
        for (var index = 0; index < polygon.Count; index++)
        {
            var a = polygon[index];
            var b = polygon[(index + 1) % polygon.Count];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var length = dx * dx + dy * dy;
            var fraction = length == 0 ? 0 : Math.Clamp(((preferredPoint.X - a.X) * dx + (preferredPoint.Y - a.Y) * dy) / length, 0, 1);
            proposals.Add(new Point2D(a.X + fraction * dx, a.Y + fraction * dy));
        }
        Rectangle? selected = null;
        double bestDistance = double.PositiveInfinity;
        foreach (var proposal in proposals)
        for (var y = (int)MathF.Floor(proposal.Y) - 1; y <= (int)MathF.Ceiling(proposal.Y) + 1; y++)
        for (var x = (int)MathF.Floor(proposal.X) - 1; x <= (int)MathF.Ceiling(proposal.X) + 1; x++)
        {
            if (!Fits(x, y, planes)) continue;
            var distance = (double)(x - preferred.X) * (x - preferred.X) + (double)(y - preferred.Y) * (y - preferred.Y);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            selected = new Rectangle(x, y, width, height);
        }
        return selected.HasValue ? Maybe<Rectangle>.From(selected.Value) : Maybe<Rectangle>.None;
    }

    private static bool Fits(int x, int y, IReadOnlyList<Plane> planes) =>
        planes.All(plane => plane.X * x + plane.Y * y <= plane.Maximum + 0.0001);

    private static List<Point2D> Clip(IReadOnlyList<Point2D> polygon, Plane plane)
    {
        var output = new List<Point2D>();
        var previous = polygon[^1];
        var previousValue = plane.X * previous.X + plane.Y * previous.Y - plane.Maximum;
        foreach (var current in polygon)
        {
            var currentValue = plane.X * current.X + plane.Y * current.Y - plane.Maximum;
            if ((currentValue <= 0) != (previousValue <= 0))
            {
                var fraction = previousValue / (previousValue - currentValue);
                output.Add(new Point2D((float)(previous.X + fraction * (current.X - previous.X)),
                    (float)(previous.Y + fraction * (current.Y - previous.Y))));
            }
            if (currentValue <= 0) output.Add(current);
            previous = current;
            previousValue = currentValue;
        }
        return output;
    }
}

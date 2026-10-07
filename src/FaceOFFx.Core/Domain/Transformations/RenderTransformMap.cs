using FaceOFFx.Core.Domain.Common;
using System.Numerics;
using SixLabors.ImageSharp;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>
/// Exact forward and inverse geometry mapping between the original source image and a rendered portrait output.
/// </summary>
public sealed record RenderTransformMap(
    ImageDimensions SourceDimensions,
    ImageDimensions RotatedDimensions,
    Rectangle RotatedCropRectangle,
    ImageDimensions OutputDimensions,
    TransformationMatrix SourceToOutput,
    TransformationMatrix OutputToSource)
{
    /// <summary>
    /// Maps a point from source-image coordinates into rendered output coordinates.
    /// </summary>
    public Point2D MapSourceToOutput(Point2D point) => SourceToOutput.TransformPoint(point);

    /// <summary>
    /// Maps a point from rendered output coordinates back into source-image coordinates.
    /// </summary>
    public Point2D MapOutputToSource(Point2D point) => OutputToSource.TransformPoint(point);

    /// <summary>
    /// Maps multiple rendered output points back into source-image coordinates.
    /// </summary>
    public IReadOnlyList<Point2D> MapOutputToSource(IEnumerable<Point2D> points) =>
        points.Select(MapOutputToSource).ToArray();

    /// <summary>
    /// Gets the rendered output rectangle projected back onto the source image.
    /// </summary>
    public IReadOnlyList<Point2D> OutputBoundsOnSource()
    {
        var corners = new[]
        {
            new Point2D(0, 0),
            new Point2D(OutputDimensions.Width, 0),
            new Point2D(OutputDimensions.Width, OutputDimensions.Height),
            new Point2D(0, OutputDimensions.Height)
        };

        return MapOutputToSource(corners);
    }
}

/// <summary>
/// Builds exact affine transform maps for rendered portrait workflows.
/// </summary>
public static class RenderTransformMapBuilder
{
    /// <summary>
    /// Creates an affine transform map for the exact sequence rotate expanded-canvas, crop, then resize.
    /// </summary>
    public static Result<RenderTransformMap, PipelineError> CreateRotateCropResize(
        ImageDimensions sourceDimensions,
        float rotationDegrees,
        ImageDimensions rotatedDimensions,
        Rectangle cropRectangle,
        ImageDimensions outputDimensions)
    {
        var rotate = BuildExpandedRotationTransform(sourceDimensions, rotatedDimensions, rotationDegrees);
        // Matrix3x2 transforms row vectors, so the operations follow their application order.
        var cropAndResize = Matrix3x2.CreateTranslation(-cropRectangle.X, -cropRectangle.Y)
            * Matrix3x2.CreateScale((float)outputDimensions.Width / cropRectangle.Width,
                (float)outputDimensions.Height / cropRectangle.Height);
        var sourceToOutput = TransformationMatrix.FromMatrix(rotate.ToMatrix() * cropAndResize);
        return sourceToOutput.GetInverse()
            .ToPipelineResult(new GeometryError("Render transform is not invertible.", "render-transform"))
            .Map(inverse => new RenderTransformMap(
                sourceDimensions,
                rotatedDimensions,
                cropRectangle,
                outputDimensions,
                sourceToOutput,
                inverse));
    }

    /// <summary>
    /// Computes the canvas size produced by rotating an image with canvas expansion enabled.
    /// </summary>
    public static ImageDimensions ComputeExpandedRotationDimensions(
        ImageDimensions sourceDimensions,
        float rotationDegrees)
    {
        var angle = rotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);

        var newWidth = (int)Math.Ceiling(
            Math.Abs(sourceDimensions.Width * cos) + Math.Abs(sourceDimensions.Height * sin));
        var newHeight = (int)Math.Ceiling(
            Math.Abs(sourceDimensions.Width * sin) + Math.Abs(sourceDimensions.Height * cos));

        return new ImageDimensions(newWidth, newHeight);
    }

    private static TransformationMatrix BuildExpandedRotationTransform(
        ImageDimensions sourceDimensions,
        ImageDimensions rotatedDimensions,
        float rotationDegrees)
    {
        var oldCenterX = sourceDimensions.Width / 2f;
        var oldCenterY = sourceDimensions.Height / 2f;
        var newCenterX = rotatedDimensions.Width / 2f;
        var newCenterY = rotatedDimensions.Height / 2f;

        return TransformationMatrix.FromMatrix(Matrix3x2.CreateTranslation(-oldCenterX, -oldCenterY)
            * Matrix3x2.CreateRotation(rotationDegrees * (float)(Math.PI / 180))
            * Matrix3x2.CreateTranslation(newCenterX, newCenterY));
    }
}

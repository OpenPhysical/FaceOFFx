using System.Numerics;
using FaceOFFx.Core.Domain.Common;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>
/// Represents a 2D transformation matrix for tracking image transformations
/// </summary>
public sealed class TransformationMatrix
{
    private Matrix3x2 _matrix;

    /// <summary>
    /// Creates an identity transformation matrix
    /// </summary>
    public TransformationMatrix()
    {
        _matrix = Matrix3x2.Identity;
    }

    /// <summary>
    /// Creates a transformation matrix from a Matrix3x2
    /// </summary>
    private TransformationMatrix(Matrix3x2 matrix)
    {
        _matrix = matrix;
    }

    /// <summary>
    /// Creates a transformation matrix from a raw <see cref="Matrix3x2"/>.
    /// </summary>
    internal static TransformationMatrix FromMatrix(Matrix3x2 matrix) => new(matrix);

    /// <summary>
    /// Gets the raw matrix value.
    /// </summary>
    internal Matrix3x2 ToMatrix() => _matrix;

    /// <summary>
    /// Gets the identity transformation (no transformation)
    /// </summary>
    public static TransformationMatrix Identity => new();

    /// <summary>
    /// Applies a rotation transformation
    /// </summary>
    /// <param name="degrees">Rotation angle in degrees</param>
    /// <param name="centerX">Center X coordinate</param>
    /// <param name="centerY">Center Y coordinate</param>
    public TransformationMatrix Rotate(float degrees, float centerX, float centerY)
    {
        var radians = degrees * (float)(Math.PI / 180.0);
        var rotation = Matrix3x2.CreateRotation(radians, new Vector2(centerX, centerY));
        return new TransformationMatrix(Matrix3x2.Multiply(rotation, _matrix));
    }

    /// <summary>
    /// Applies a translation transformation
    /// </summary>
    /// <param name="dx">X translation</param>
    /// <param name="dy">Y translation</param>
    public TransformationMatrix Translate(float dx, float dy)
    {
        var translation = Matrix3x2.CreateTranslation(dx, dy);
        return new TransformationMatrix(Matrix3x2.Multiply(translation, _matrix));
    }

    /// <summary>
    /// Applies a scale transformation
    /// </summary>
    /// <param name="scale">Uniform scale factor</param>
    /// <param name="centerX">Scale center X coordinate</param>
    /// <param name="centerY">Scale center Y coordinate</param>
    public TransformationMatrix Scale(float scale, float centerX = 0, float centerY = 0)
    {
        var scaleMatrix = Matrix3x2.CreateScale(scale, scale, new Vector2(centerX, centerY));
        return new TransformationMatrix(Matrix3x2.Multiply(scaleMatrix, _matrix));
    }

    /// <summary>
    /// Applies a non-uniform scale transformation.
    /// </summary>
    public TransformationMatrix Scale(float scaleX, float scaleY, float centerX = 0, float centerY = 0)
    {
        var scaleMatrix = Matrix3x2.CreateScale(scaleX, scaleY, new Vector2(centerX, centerY));
        return new TransformationMatrix(Matrix3x2.Multiply(scaleMatrix, _matrix));
    }

    /// <summary>
    /// Transforms a point using this transformation matrix
    /// </summary>
    public (float X, float Y) TransformPoint(float x, float y)
    {
        var point = new Vector2(x, y);
        var transformed = Vector2.Transform(point, _matrix);
        return (transformed.X, transformed.Y);
    }

    /// <summary>
    /// Transforms a domain point.
    /// </summary>
    public Point2D TransformPoint(Point2D point)
    {
        var transformed = TransformPoint(point.X, point.Y);
        return new Point2D(transformed.X, transformed.Y);
    }

    /// <summary>
    /// Transforms multiple points
    /// </summary>
    public IReadOnlyList<(float X, float Y)> TransformPoints(IEnumerable<(float X, float Y)> points)
    {
        return points.Select(p => TransformPoint(p.X, p.Y)).ToList();
    }

    /// <summary>
    /// Gets the inverse transformation matrix
    /// </summary>
    public Maybe<TransformationMatrix> GetInverse()
    {
        if (Matrix3x2.Invert(_matrix, out var inverse))
        {
            return Maybe<TransformationMatrix>.From(new TransformationMatrix(inverse));
        }
        return Maybe<TransformationMatrix>.None;
    }

    /// <summary>
    /// Combines two transformation matrices
    /// </summary>
    public static TransformationMatrix operator *(TransformationMatrix a, TransformationMatrix b)
    {
        return new TransformationMatrix(Matrix3x2.Multiply(a._matrix, b._matrix));
    }

    /// <summary>
    /// Gets the decomposed transformation components
    /// </summary>
    public (
        float TranslationX,
        float TranslationY,
        float Rotation,
        float ScaleX,
        float ScaleY
    ) Decompose()
    {
        var translation = _matrix.Translation;

        // Extract scale and rotation
        var scaleX = (float)Math.Sqrt(_matrix.M11 * _matrix.M11 + _matrix.M12 * _matrix.M12);
        var scaleY = (float)Math.Sqrt(_matrix.M21 * _matrix.M21 + _matrix.M22 * _matrix.M22);

        // Extract rotation (in radians)
        var rotation = (float)Math.Atan2(_matrix.M12, _matrix.M11);

        return (translation.X, translation.Y, rotation * (float)(180.0 / Math.PI), scaleX, scaleY);
    }
}

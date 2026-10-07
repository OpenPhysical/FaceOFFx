using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>Pixel dimensions used for source and rendered image geometry.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
[PublicAPI]
public sealed record ImageDimensions(int Width, int Height)
{
    /// <summary>Image width divided by its height.</summary>
    public float AspectRatio => (float)Width / Height;

    /// <summary>Creates dimensions with positive width and height.</summary>
    public static Result<ImageDimensions> Create(int width, int height) =>
        width <= 0 || height <= 0
            ? Result.Failure<ImageDimensions>("Image dimensions must be positive")
            : Result.Success(new ImageDimensions(width, height));
}

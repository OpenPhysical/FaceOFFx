using FaceOFFx.Core.Domain.Common;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Detection;

/// <summary>
/// Represents a Region of Interest (ROI) in facial landmark detection for JPEG 2000 encoding.
/// </summary>
/// <param name="Name">The descriptive name of this ROI region.</param>
/// <param name="Priority">The encoding priority (1=lowest, 3=highest) for JPEG 2000.</param>
/// <param name="BoundingBox">The rectangular bounds of this ROI in image coordinates.</param>
/// <param name="LandmarkIndices">The indices of the 68-point landmarks included in this ROI.</param>
/// <remarks>
/// ROI is used in JPEG 2000 encoding to allocate higher compression quality to the
/// facial region based on its importance for recognition and analysis tasks.
/// </remarks>
[PublicAPI]
public record RoiRegion(
    string Name,
    int Priority,
    RoiBoundingBox BoundingBox,
    IReadOnlyList<int> LandmarkIndices
)
{
    /// <summary>
    /// Validates that this ROI region has valid properties.
    /// </summary>
    /// <returns>A Result indicating success or containing validation errors.</returns>
    public Result Validate()
    {
        return string.IsNullOrWhiteSpace(Name) ? Result.Failure("ROI name cannot be empty")
            : Priority is < 1 or > 3
                ? Result.Failure($"ROI priority must be between 1 and 3, got {Priority}")
            : LandmarkIndices.Count == 0 ? Result.Failure("ROI must include at least one landmark")
            : LandmarkIndices.Any(static i => i is < 0 or > 67)
                ? Result.Failure("Landmark indices must be between 0 and 67")
            : BoundingBox.Validate();
    }
}

/// <summary>
/// Represents a bounding box for an ROI region with coordinate validation.
/// </summary>
/// <param name="X">The X coordinate of the left edge.</param>
/// <param name="Y">The Y coordinate of the top edge.</param>
/// <param name="Width">The width of the bounding box.</param>
/// <param name="Height">The height of the bounding box.</param>
[PublicAPI]
public record RoiBoundingBox(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// Gets the right-sided edge X coordinate.
    /// </summary>
    [PublicAPI]
    public int Right
    {
        get { return X + Width; }
    }

    /// <summary>
    /// Gets the Y coordinate of the bottom edge.
    /// </summary>
    [PublicAPI]
    public int Bottom
    {
        get { return Y + Height; }
    }

    /// <summary>
    /// Gets the point in the centre of the bounding box.
    /// </summary>
    [PublicAPI]
    public Point2D Center
    {
        get { return new Point2D(X + Width / 2f, Y + Height / 2f); }
    }

    /// <summary>
    /// Gets the area of the bounding box.
    /// </summary>
    public int Area
    {
        get { return Width * Height; }
    }

    /// <summary>
    /// Validates that this bounding box has valid dimensions.
    /// </summary>
    public Result Validate()
    {
        return X < 0 || Y < 0
                ? Result.Failure($"Bounding box coordinates must be non-negative: ({X}, {Y})")
            : Width <= 0 || Height <= 0
                ? Result.Failure($"Bounding box dimensions must be positive: {Width}x{Height}")
            : Result.Success();
    }

    /// <summary>
    /// Expands the bounding box by a percentage margin.
    /// </summary>
    /// <param name="marginRatio">The margin to add as a ratio (0.1 = 10% margin).</param>
    /// <param name="imageWidth">The image width to constrain the box.</param>
    /// <param name="imageHeight">The image height to constrain the box.</param>
    /// <returns>A new expanded bounding box constrained to image bounds.</returns>
    public RoiBoundingBox ExpandWithMargin(float marginRatio, int imageWidth, int imageHeight)
    {
        var marginX = (int)(Width * marginRatio);
        var marginY = (int)(Height * marginRatio);

        var newX = Math.Max(0, X - marginX);
        var newY = Math.Max(0, Y - marginY);
        var newRight = Math.Min(imageWidth, Right + marginX);
        var newBottom = Math.Min(imageHeight, Bottom + marginY);

        return new RoiBoundingBox(newX, newY, newRight - newX, newBottom - newY);
    }
}

/// <summary>
/// Represents one protected facial region for JPEG 2000 encoding.
/// </summary>
/// <param name="InnerRegion">The facial region bounds and landmark coverage for review.</param>
/// <remarks>
/// Fixed policies preserve every detected facial feature and jaw point with a localization margin.
/// The optional extended policy also includes estimated forehead and ear envelopes.
/// PIV Full Frontal geometry, source acquisition, and region coverage have separate verification evidence.
/// </remarks>
[PublicAPI]
public record FacialRoiSet(RoiRegion InnerRegion)
{
    /// <summary>One immutable face-centered mask, with anatomical coverage recorded separately.</summary>
    public FacialRoiMask? Mask { get; init; }
    /// <summary>Recorded construction method and visual verification requirements.</summary>
    public FacialRoiCoverage? Coverage { get; init; }
    /// <summary>Actual protected mask pixels, or the area of an explicitly rectangular ROI.</summary>
    public long PixelCount => Mask?.PixelCount ?? (long)InnerRegion.BoundingBox.Width * InnerRegion.BoundingBox.Height;
    /// <summary>
    /// Gets the single protected facial region.
    /// </summary>
    public IReadOnlyList<RoiRegion> AllRegions => [InnerRegion];

    /// <summary>
    /// Validates that the Inner Region is valid.
    /// </summary>
    public Result Validate()
    {
        var validation = InnerRegion.Validate();
        return validation.IsFailure
            ? Result.Failure($"Inner Region validation failed: {validation.Error}")
            : Result.Success();
    }

}

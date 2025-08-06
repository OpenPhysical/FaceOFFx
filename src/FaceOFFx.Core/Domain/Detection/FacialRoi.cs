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
/// Represents facial ROI for JPEG 2000 encoding following INCITS 385-2004 Appendix C.6.
/// </summary>
/// <param name="InnerRegion">The Inner Region as defined by Appendix C.6 - rectangular area for high-quality encoding.</param>
/// <remarks>
/// Implements the Appendix C.6 specification for PIV-compatible facial image compression:
/// - Inner Region: Rectangular area at (0.1×W-1, 0.1×W-1) to (0.9×W-1, 1.1×W-1)
/// - Outer Region: Everything outside Inner Region gets lower quality (handled by encoder)
/// This approach provides consistent, standards-compliant ROI behavior.
/// </remarks>
[PublicAPI]
public record FacialRoiSet(RoiRegion InnerRegion)
{
    /// <summary>
    /// Gets the ROI Inner Region (single region for Appendix C.6).
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

    /// <summary>
    /// Creates an Appendix C.6 compliant facial ROI set for PIV images.
    /// </summary>
    /// <param name="imageWidth">The width of the PIV image (should be 420 for standard PIV).</param>
    /// <param name="imageHeight">The height of the PIV image (should be 560 for standard PIV).</param>
    /// <returns>A Result containing the FacialRoiSet with Appendix C.6 Inner Region or an error.</returns>
    /// <remarks>
    /// Creates the Inner Region as specified in INCITS 385-2004 Appendix C.6:
    /// Formula: (0.1×W-1, 0.1×W-1) to (0.9×W-1, 1.1×W-1)
    /// For standard PIV (420×560): Inner Region at (41, 41) to (377, 461)
    /// The Outer Region (everything else) gets lower quality automatically by the encoder.
    /// </remarks>
    public static Result<FacialRoiSet> CreateAppendixC6(int imageWidth, int imageHeight)
    {
        try
        {
            // Appendix C.6: Inner Region formula
            // (0.1×W-1, 0.1×W-1) to (0.9×W-1, 1.1×W-1)
            var innerRegionX = (int)(0.1f * imageWidth - 1);
            var innerRegionY = (int)(0.1f * imageWidth - 1); // Use width for both dimensions as per spec
            var innerRegionMaxX = (int)(0.9f * imageWidth - 1);
            var innerRegionMaxY = (int)(1.1f * imageWidth - 1);
            var innerRegionWidth = innerRegionMaxX - innerRegionX + 1;
            var innerRegionHeight = Math.Min(
                innerRegionMaxY - innerRegionY + 1,
                imageHeight - innerRegionY
            );

            // Ensure Inner Region stays within image bounds
            innerRegionHeight = Math.Min(innerRegionHeight, imageHeight - innerRegionY);

            var innerRegionBox = new RoiBoundingBox(
                innerRegionX,
                innerRegionY,
                innerRegionWidth,
                innerRegionHeight
            );

            // All 68 landmark indices are included in the Inner Region for visualization
            var allLandmarkIndices = Enumerable.Range(0, 68).ToList();

            var innerRegion = new RoiRegion("Inner", 3, innerRegionBox, allLandmarkIndices);
            var roiSet = new FacialRoiSet(innerRegion);

            return roiSet.Validate().Map(() => roiSet);
        }
        catch (Exception ex)
        {
            return Result.Failure<FacialRoiSet>(
                $"Failed to create Appendix C.6 ROI set: {ex.Message}"
            );
        }
    }

    /// <summary>
    /// Creates an Appendix C.6 compliant facial ROI set for any image dimensions.
    /// This is a pure function that calculates ROI based on the formula regardless of image size.
    /// </summary>
    /// <param name="imageWidth">The width of the image in pixels.</param>
    /// <param name="imageHeight">The height of the image in pixels.</param>
    /// <returns>A FacialRoiSet with dynamically calculated Inner Region based on image dimensions.</returns>
    /// <remarks>
    /// This pure function applies the INCITS 385-2004 Appendix C.6 formula to any image size:
    /// Formula: (0.1×W-1, 0.1×W-1) to (0.9×W-1, 1.1×W-1)
    /// The formula uses the image width (W) for both X and Y calculations as per the standard.
    /// This allows ROI to be calculated for non-standard image sizes when using --no-resize.
    /// </remarks>
    [PublicAPI]
    public static FacialRoiSet CalculateRoiForDimensions(int imageWidth, int imageHeight)
    {
        // Apply Appendix C.6 formula using width for both dimensions as per spec
        var innerRegionX = Math.Max(0, (int)(0.1f * imageWidth - 1));
        var innerRegionY = Math.Max(0, (int)(0.1f * imageWidth - 1));
        var innerRegionMaxX = Math.Min(imageWidth - 1, (int)(0.9f * imageWidth - 1));
        var innerRegionMaxY = Math.Min(imageHeight - 1, (int)(1.1f * imageWidth - 1));

        // Calculate dimensions ensuring they stay within bounds
        var innerRegionWidth = innerRegionMaxX - innerRegionX + 1;
        var innerRegionHeight = innerRegionMaxY - innerRegionY + 1;

        // Create bounding box
        var innerRegionBox = new RoiBoundingBox(
            innerRegionX,
            innerRegionY,
            innerRegionWidth,
            innerRegionHeight
        );

        // All 68 landmark indices for consistency (even though no face detection is performed)
        var allLandmarkIndices = Enumerable.Range(0, 68).ToList();

        // Create and return the ROI set
        var innerRegion = new RoiRegion("Inner", 3, innerRegionBox, allLandmarkIndices);
        return new FacialRoiSet(innerRegion);
    }
}

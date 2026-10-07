using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Standards;

/// <summary>
/// Represents the three critical PIV compliance lines derived from facial landmarks.
/// These lines are used for precise face positioning according to INCITS 385-2004 Section 8.
/// </summary>
/// <param name="LineAA_X">Vertical center line X-coordinate - must pass through nose bridge and mouth center</param>
/// <param name="LineBB_Y">Horizontal eye line Y-coordinate - must be 50-70% from bottom edge</param>
/// <param name="LineCC_Width">Width value accompanied by its measured ear-attachment or estimated jaw basis.</param>
/// <param name="NoseCenter">Center point of nose bridge (landmarks 27-30)</param>
/// <param name="MouthCenter">Center point of mouth (landmarks 48, 51, 54, 57)</param>
/// <param name="LeftEyeCenter">Center of left eye (landmarks 36-41)</param>
/// <param name="RightEyeCenter">Center of right eye (landmarks 42-47)</param>
/// <param name="LeftEarPoint">Level-adjusted leftmost face contour point</param>
/// <param name="RightEarPoint">Level-adjusted rightmost face contour point</param>
[PublicAPI]
public record PivComplianceLines(
    float LineAA_X,
    float LineBB_Y,
    float LineCC_Width,
    Point2D NoseCenter,
    Point2D MouthCenter,
    Point2D LeftEyeCenter,
    Point2D RightEyeCenter,
    Point2D LeftEarPoint,
    Point2D RightEarPoint
)
{
    /// <summary>Reports whether CC comes from anatomical measurement or jaw-contour estimation.</summary>
    public GeometryEvidenceStatus HeadWidthStatus { get; init; } = GeometryEvidenceStatus.Estimated;
    /// <summary>Recorded source of a caller-supplied ear-attachment measurement.</summary>
    public string? HeadWidthMeasurementSource { get; init; }
    /// <summary>
    /// Gets the horizontal eye line as a geometric line for visualization.
    /// </summary>
    public Line HorizontalEyeLine => new(new Point2D(0, LineBB_Y), new Point2D(1000, LineBB_Y));

    /// <summary>
    /// Gets the vertical center line as a geometric line for visualization.
    /// </summary>
    public Line VerticalCenterLine => new(new Point2D(LineAA_X, 0), new Point2D(LineAA_X, 1000));

    /// <summary>
    /// Gets the head width line connecting the ear points.
    /// </summary>
    public Line HeadWidthLine => new(LeftEarPoint, RightEarPoint);

    /// <summary>
    /// Gets the inter-pupillary distance (eye-to-eye distance).
    /// </summary>
    public float InterPupillaryDistance =>
        (float)
            Math.Sqrt(
                Math.Pow(RightEyeCenter.X - LeftEyeCenter.X, 2)
                    + Math.Pow(RightEyeCenter.Y - LeftEyeCenter.Y, 2)
            );

    /// <summary>
    /// Checks if the nose and mouth centers are reasonably aligned (within tolerance).
    /// </summary>
    /// <param name="tolerance">Maximum allowed deviation in pixels (default: 5)</param>
    /// <returns>True if nose and mouth are aligned within tolerance</returns>
    public bool AreNoseAndMouthAligned(float tolerance = 5.0f)
    {
        return Math.Abs(NoseCenter.X - MouthCenter.X) <= tolerance;
    }

    /// <summary>
    /// Gets the deviation of the face center line from the image center.
    /// </summary>
    /// <param name="imageWidth">Width of the image</param>
    /// <returns>Deviation in pixels (positive = face center right of image center)</returns>
    public float GetCenterLineDeviation(int imageWidth)
    {
        var imageCenterX = imageWidth / 2.0f;
        return LineAA_X - imageCenterX;
    }

    /// <summary>
    /// Calculates what percentage from the bottom edge the eye line is positioned.
    /// </summary>
    /// <param name="imageHeight">Height of the image</param>
    /// <returns>Percentage from bottom (0.0 = bottom, 1.0 = top)</returns>
    public float GetEyeLinePercentageFromBottom(int imageHeight)
    {
        return (imageHeight - LineBB_Y) / imageHeight;
    }

    /// <summary>
    /// Calculates the current image width to head width ratio.
    /// </summary>
    /// <param name="imageWidth">Width of the image</param>
    /// <returns>The ratio as a decimal (e.g., 1.75 for 7:4 ratio)</returns>
    public float GetImageToHeadWidthRatio(int imageWidth)
    {
        return imageWidth / LineCC_Width;
    }
}

/// <summary>
/// Represents a geometric line defined by two points.
/// </summary>
/// <param name="Start">Starting point of the line</param>
/// <param name="End">Ending point of the line</param>
[PublicAPI]
public record Line(Point2D Start, Point2D End)
{
    /// <summary>
    /// Gets the length of the line.
    /// </summary>
    public float Length =>
        (float)Math.Sqrt(Math.Pow(End.X - Start.X, 2) + Math.Pow(End.Y - Start.Y, 2));

    /// <summary>
    /// Gets the midpoint of the line.
    /// </summary>
    public Point2D Midpoint => new((Start.X + End.X) / 2, (Start.Y + End.Y) / 2);

    /// <summary>
    /// Checks if the line is approximately horizontal (within angle tolerance).
    /// </summary>
    /// <param name="angleTolerance">Maximum allowed angle deviation in degrees (default: 5)</param>
    /// <returns>True if the line is approximately horizontal</returns>
    public bool IsHorizontal(float angleTolerance = 5.0f)
    {
        var angle = Math.Abs(Math.Atan2(End.Y - Start.Y, End.X - Start.X) * 180 / Math.PI);
        return angle <= angleTolerance || angle >= (180 - angleTolerance);
    }

    /// <summary>
    /// Checks if the line is approximately vertical (within angle tolerance).
    /// </summary>
    /// <param name="angleTolerance">Maximum allowed angle deviation in degrees (default: 5)</param>
    /// <returns>True if the line is approximately vertical</returns>
    public bool IsVertical(float angleTolerance = 5.0f)
    {
        var angle = Math.Abs(Math.Atan2(End.Y - Start.Y, End.X - Start.X) * 180 / Math.PI);
        return Math.Abs(angle - 90) <= angleTolerance;
    }
}

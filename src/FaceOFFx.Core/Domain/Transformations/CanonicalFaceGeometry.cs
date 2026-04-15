using System.Numerics;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>
/// Canonical two-stage face geometry, including coarse detection, normalized chip geometry,
/// and fine landmarks projected back into original-image coordinates.
/// </summary>
public sealed record CanonicalFaceGeometry
{
    /// <summary>
    /// Initializes a new canonical face geometry instance.
    /// </summary>
    public CanonicalFaceGeometry(
        DetectedFace coarseDetection,
        FaceLandmarks5 coarseLandmarks,
        ImageDimensions chipDimensions,
        FaceLandmarks68 chipLandmarks,
        FaceLandmarks68 sourceLandmarks,
        TransformationMatrix sourceToChip,
        TransformationMatrix chipToSource)
    {
        CoarseDetection = coarseDetection;
        CoarseLandmarks = coarseLandmarks;
        ChipDimensions = chipDimensions;
        ChipLandmarks = chipLandmarks;
        SourceLandmarks = sourceLandmarks;
        SourceToChip = sourceToChip;
        ChipToSource = chipToSource;
    }

    /// <summary>The coarse face detection selected from the original image.</summary>
    public DetectedFace CoarseDetection { get; }

    /// <summary>The RetinaFace 5-point landmarks from the original image.</summary>
    public FaceLandmarks5 CoarseLandmarks { get; }

    /// <summary>The normalized chip dimensions used for the fine landmark solve.</summary>
    public ImageDimensions ChipDimensions { get; }

    /// <summary>The fine 68-point landmarks in normalized chip coordinates.</summary>
    public FaceLandmarks68 ChipLandmarks { get; }

    /// <summary>The fine 68-point landmarks projected back into original-image coordinates.</summary>
    public FaceLandmarks68 SourceLandmarks { get; }

    /// <summary>The affine transform from original-image coordinates into normalized chip coordinates.</summary>
    public TransformationMatrix SourceToChip { get; }

    /// <summary>The affine transform from normalized chip coordinates back into original-image coordinates.</summary>
    public TransformationMatrix ChipToSource { get; }
}

/// <summary>
/// Builds canonical face geometry by normalizing a coarse face detection into a landmark chip
/// and running one fine landmark extraction pass on that chip.
/// </summary>
public static class CanonicalFaceGeometryPipeline
{
    private const int DefaultChipSize = 112;
    private const float ChipMarginRatio = 0.10f;

    /// <summary>
    /// Builds canonical geometry from a coarse face detection and one fine landmark extraction pass.
    /// </summary>
    /// <summary>
    /// Builds canonical geometry from a coarse face detection and returns typed pipeline errors.
    /// </summary>
    public static async Task<Result<CanonicalFaceGeometry, PipelineError>> ExtractAsync(
        Image<Rgba32> sourceImage,
        DetectedFace detectedFace,
        ILandmarkExtractor landmarkExtractor,
        CancellationToken cancellationToken = default)
    {
        var coarseLandmarksResult = detectedFace.Landmarks5.ToPipelineResult(
            new DetectionError("Coarse face detection did not provide 5-point landmarks required for normalization."));

        if (coarseLandmarksResult.IsFailure)
        {
            return Result.Failure<CanonicalFaceGeometry, PipelineError>(coarseLandmarksResult.Error);
        }

        var coarseLandmarks = coarseLandmarksResult.Value;
        var transformResult = BuildChipTransforms(detectedFace, coarseLandmarks, DefaultChipSize);
        if (transformResult.IsFailure)
        {
            return Result.Failure<CanonicalFaceGeometry, PipelineError>(transformResult.Error);
        }

        var chipFaceBoxResult = FaceBox.Create(0, 0, DefaultChipSize, DefaultChipSize)
            .ToPipelineResult(error => new GeometryError(error, "normalized-chip-face-box"));
        if (chipFaceBoxResult.IsFailure)
        {
            return Result.Failure<CanonicalFaceGeometry, PipelineError>(chipFaceBoxResult.Error);
        }

        using var chipImage = RenderNormalizedChip(
            sourceImage,
            transformResult.Value.ChipToSource,
            DefaultChipSize,
            DefaultChipSize);

        cancellationToken.ThrowIfCancellationRequested();

        var chipLandmarksResult = await landmarkExtractor
            .ExtractLandmarksAsync(chipImage, chipFaceBoxResult.Value, cancellationToken)
            .ConfigureAwait(false);
        if (chipLandmarksResult.IsFailure)
        {
            return Result.Failure<CanonicalFaceGeometry, PipelineError>(
                new DetectionError(
                    $"Fine landmark extraction failed on normalized chip: {chipLandmarksResult.Error.Message}",
                    "normalized-chip"));
        }

        return Result.Success<CanonicalFaceGeometry, PipelineError>(CreateCanonicalFaceGeometry(
            detectedFace,
            coarseLandmarks,
            transformResult.Value.SourceToChip,
            transformResult.Value.ChipToSource,
            chipLandmarksResult.Value));
    }

    private static CanonicalFaceGeometry CreateCanonicalFaceGeometry(
        DetectedFace detectedFace,
        FaceLandmarks5 coarseLandmarks,
        TransformationMatrix sourceToChip,
        TransformationMatrix chipToSource,
        FaceLandmarks68 chipLandmarks)
    {
        var sourceLandmarks = new FaceLandmarks68(
            chipLandmarks.Points
                .Select(chipToSource.TransformPoint)
                .ToList());

        return new CanonicalFaceGeometry(
            detectedFace,
            coarseLandmarks,
            new ImageDimensions(DefaultChipSize, DefaultChipSize),
            chipLandmarks,
            sourceLandmarks,
            sourceToChip,
            chipToSource);
    }

    private static Result<(TransformationMatrix SourceToChip, TransformationMatrix ChipToSource), PipelineError> BuildChipTransforms(
        DetectedFace detectedFace,
        FaceLandmarks5 coarseLandmarks,
        int chipSize)
    {
        var sourceToChipResult = BuildSourceToChipTransform(detectedFace, coarseLandmarks, chipSize);
        if (sourceToChipResult.IsFailure)
        {
            return Result.Failure<(TransformationMatrix SourceToChip, TransformationMatrix ChipToSource), PipelineError>(
                sourceToChipResult.Error);
        }

        var sourceToChip = sourceToChipResult.Value;
        return sourceToChip.GetInverse()
            .ToPipelineResult(new GeometryError("Source-to-chip transform is not invertible.", "chip-transform"))
            .Map(chipToSource => (sourceToChip, chipToSource));
    }

    private static Result<TransformationMatrix, PipelineError> BuildSourceToChipTransform(
        DetectedFace detectedFace,
        FaceLandmarks5 landmarks,
        int chipSize)
    {
        var horizontalAxisResult = CalculateHorizontalAxis(landmarks);
        if (horizontalAxisResult.IsFailure)
        {
            return Result.Failure<TransformationMatrix, PipelineError>(horizontalAxisResult.Error);
        }

        var horizontalAxis = horizontalAxisResult.Value;
        var verticalAxis = new Point2D(-horizontalAxis.Y, horizontalAxis.X);
        var chipSquareResult = BuildChipSquare(detectedFace.BoundingBox, landmarks.Nose, horizontalAxis, verticalAxis);
        if (chipSquareResult.IsFailure)
        {
            return Result.Failure<TransformationMatrix, PipelineError>(chipSquareResult.Error);
        }

        return Result.Success<TransformationMatrix, PipelineError>(
            BuildSquareToChipTransform(chipSquareResult.Value, horizontalAxis, verticalAxis, chipSize));
    }

    private static Result<ChipSquare, PipelineError> BuildChipSquare(
        FaceBox coarseFaceBox,
        Point2D nosePoint,
        Point2D horizontalAxis,
        Point2D verticalAxis)
    {
        var projectedCorners = BuildDetectorExtent(coarseFaceBox)
            .Select(corner => ProjectToAxisSpace(corner, nosePoint, horizontalAxis, verticalAxis))
            .ToArray();
        var minX = projectedCorners.Min(point => point.X);
        var maxX = projectedCorners.Max(point => point.X);
        var minY = projectedCorners.Min(point => point.Y);
        var maxY = projectedCorners.Max(point => point.Y);
        var width = maxX - minX;
        var height = maxY - minY;
        var sideLength = Math.Max(width, height) * (1f + ChipMarginRatio);
        if (sideLength <= 0.0001f)
        {
            return Result.Failure<ChipSquare, PipelineError>(
                new GeometryError("Cannot build chip square from degenerate coarse face bounds.", "chip-square"));
        }

        var centerInAxisSpace = new Point2D((minX + maxX) / 2f, (minY + maxY) / 2f);
        var sourceCenter = nosePoint
            + (horizontalAxis * centerInAxisSpace.X)
            + (verticalAxis * centerInAxisSpace.Y);
        return Result.Success<ChipSquare, PipelineError>(new ChipSquare(sourceCenter, sideLength));
    }

    private static IEnumerable<Point2D> BuildDetectorExtent(FaceBox coarseFaceBox)
    {
        return new[]
        {
            new Point2D(coarseFaceBox.Left, coarseFaceBox.Top),
            new Point2D(coarseFaceBox.Right, coarseFaceBox.Top),
            new Point2D(coarseFaceBox.Right, coarseFaceBox.Bottom),
            new Point2D(coarseFaceBox.Left, coarseFaceBox.Bottom)
        };
    }

    private static Result<Point2D, PipelineError> CalculateHorizontalAxis(FaceLandmarks5 landmarks)
    {
        var eyeVectorResult = Normalize(landmarks.RightEye - landmarks.LeftEye);
        if (eyeVectorResult.IsFailure)
        {
            return Result.Failure<Point2D, PipelineError>(eyeVectorResult.Error);
        }

        var mouthVectorResult = Normalize(landmarks.RightMouth - landmarks.LeftMouth);
        if (mouthVectorResult.IsFailure)
        {
            return Result.Failure<Point2D, PipelineError>(mouthVectorResult.Error);
        }

        var eyeVector = eyeVectorResult.Value;
        var mouthVector = mouthVectorResult.Value;
        var combined = eyeVector + mouthVector;
        if (combined.DistanceTo(Point2D.Zero) <= 0.0001f)
        {
            combined = eyeVector;
        }

        return Normalize(combined);
    }

    private static Result<Point2D, PipelineError> Normalize(Point2D vector)
    {
        var length = vector.DistanceTo(Point2D.Zero);
        if (length <= 0.0001f)
        {
            return Result.Failure<Point2D, PipelineError>(
                new GeometryError("Cannot normalize a zero-length axis vector.", "chip-axis"));
        }

        return Result.Success<Point2D, PipelineError>(new Point2D(vector.X / length, vector.Y / length));
    }

    private static Point2D ProjectToAxisSpace(
        Point2D point,
        Point2D origin,
        Point2D horizontalAxis,
        Point2D verticalAxis)
    {
        var offset = point - origin;
        return new Point2D(
            Dot(offset, horizontalAxis),
            Dot(offset, verticalAxis));
    }

    private static float Dot(Point2D left, Point2D right) =>
        (left.X * right.X) + (left.Y * right.Y);

    private static TransformationMatrix BuildSquareToChipTransform(
        ChipSquare chipSquare,
        Point2D horizontalAxis,
        Point2D verticalAxis,
        int chipSize)
    {
        var scale = chipSize / chipSquare.SideLength;
        var chipCenter = chipSize / 2f;
        var translationX = chipCenter - (scale * Dot(chipSquare.Center, horizontalAxis));
        var translationY = chipCenter - (scale * Dot(chipSquare.Center, verticalAxis));
        var matrix = new Matrix3x2(
            scale * horizontalAxis.X,
            scale * verticalAxis.X,
            scale * horizontalAxis.Y,
            scale * verticalAxis.Y,
            translationX,
            translationY);
        return TransformationMatrix.FromMatrix(matrix);
    }

    private sealed record ChipSquare(Point2D Center, float SideLength);

    private static Image<Rgba32> RenderNormalizedChip(
        Image<Rgba32> sourceImage,
        TransformationMatrix chipToSource,
        int chipWidth,
        int chipHeight)
    {
        var chip = new Image<Rgba32>(chipWidth, chipHeight, new Rgba32(128, 128, 128, 255));

        for (var y = 0; y < chipHeight; y++)
        {
            for (var x = 0; x < chipWidth; x++)
            {
                var sourcePoint = chipToSource.TransformPoint(new Point2D(x, y));
                chip[x, y] = SampleBicubic(sourceImage, sourcePoint);
            }
        }

        return chip;
    }

    /// <summary>
    /// Renders the actual normalized chip used for fine landmark inference from canonical geometry.
    /// </summary>
    public static Image<Rgba32> RenderChip(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(geometry);

        return RenderNormalizedChip(
            sourceImage,
            geometry.ChipToSource,
            geometry.ChipDimensions.Width,
            geometry.ChipDimensions.Height);
    }

    private static Rgba32 SampleBicubic(Image<Rgba32> image, Point2D point)
    {
        if (point.X < 0 || point.Y < 0 || point.X > image.Width - 1 || point.Y > image.Height - 1)
        {
            return new Rgba32(128, 128, 128, 255);
        }

        var baseX = (int)MathF.Floor(point.X);
        var baseY = (int)MathF.Floor(point.Y);
        var tx = point.X - baseX;
        var ty = point.Y - baseY;

        var red = 0f;
        var green = 0f;
        var blue = 0f;
        var alpha = 0f;
        var totalWeight = 0f;

        for (var yOffset = -1; yOffset <= 2; yOffset++)
        {
            var sampleY = baseY + yOffset;
            var weightY = CubicKernel(yOffset - ty);

            for (var xOffset = -1; xOffset <= 2; xOffset++)
            {
                var sampleX = baseX + xOffset;
                var weightX = CubicKernel(xOffset - tx);
                var weight = weightX * weightY;
                var sample = GetSample(image, sampleX, sampleY);

                red += sample.R * weight;
                green += sample.G * weight;
                blue += sample.B * weight;
                alpha += sample.A * weight;
                totalWeight += weight;
            }
        }

        if (MathF.Abs(totalWeight) <= 0.0001f)
        {
            return new Rgba32(128, 128, 128, 255);
        }

        return new Rgba32(
            ClampToByte(red / totalWeight),
            ClampToByte(green / totalWeight),
            ClampToByte(blue / totalWeight),
            ClampToByte(alpha / totalWeight));
    }

    private static Rgba32 GetSample(Image<Rgba32> image, int x, int y)
    {
        if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
        {
            return new Rgba32(128, 128, 128, 255);
        }

        return image[x, y];
    }

    private static float CubicKernel(float value)
    {
        const float a = -0.5f;
        var abs = MathF.Abs(value);
        if (abs <= 1f)
        {
            return ((a + 2f) * abs * abs * abs) - ((a + 3f) * abs * abs) + 1f;
        }

        if (abs < 2f)
        {
            return (a * abs * abs * abs) - (5f * a * abs * abs) + (8f * a * abs) - (4f * a);
        }

        return 0f;
    }

    private static byte ClampToByte(float value) =>
        (byte)Math.Clamp(MathF.Round(value), 0, 255);
}

/// <summary>
/// Shared deterministic landmark transforms used after the fine landmark solve.
/// </summary>
public static class FaceGeometryTransformations
{
    /// <summary>
    /// Calculates the rotation required to level the eye line.
    /// </summary>
    public static float CalculateEyeRotation(Point2D leftEye, Point2D rightEye)
    {
        var deltaY = rightEye.Y - leftEye.Y;
        var deltaX = rightEye.X - leftEye.X;
        return -(float)(Math.Atan2(deltaY, deltaX) * 180.0 / Math.PI);
    }

    /// <summary>
    /// Rotates landmarks into the expanded-canvas coordinate space used by the rotated image.
    /// </summary>
    public static FaceLandmarks68 RotateLandmarks(
        FaceLandmarks68 landmarks,
        float rotationDegrees,
        ImageDimensions sourceDimensions)
    {
        if (Math.Abs(rotationDegrees) <= 0.1f)
        {
            return landmarks;
        }

        var rotatedDimensions = RenderTransformMapBuilder.ComputeExpandedRotationDimensions(
            sourceDimensions,
            rotationDegrees);

        var transform = TransformationMatrix.Identity
            .Translate(-(sourceDimensions.Width / 2f), -(sourceDimensions.Height / 2f))
            .Rotate(rotationDegrees, 0, 0)
            .Translate(rotatedDimensions.Width / 2f, rotatedDimensions.Height / 2f);

        return new FaceLandmarks68(landmarks.Points.Select(transform.TransformPoint).ToList());
    }

    /// <summary>
    /// Transforms rotated landmarks into final output-image coordinates after crop and resize.
    /// </summary>
    public static FaceLandmarks68 TransformLandmarksToOutputSpace(
        FaceLandmarks68 landmarks,
        Rectangle cropRectangle,
        ImageDimensions targetDimensions)
    {
        var scaleX = (float)targetDimensions.Width / cropRectangle.Width;
        var scaleY = (float)targetDimensions.Height / cropRectangle.Height;

        return new FaceLandmarks68(landmarks.Points
            .Select(point => new Point2D(
                (point.X - cropRectangle.X) * scaleX,
                (point.Y - cropRectangle.Y) * scaleY))
            .ToList());
    }
}

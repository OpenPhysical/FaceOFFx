using FaceOFFx.Core.Domain.Common;

namespace FaceOFFx.Core.Domain.Detection;

/// <summary>Immutable image-sized binary mask for one protected facial region.</summary>
public sealed class FacialRoiMask
{
    private readonly byte[] _pixels;

    internal FacialRoiMask(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0 || (long)width * height != pixels.Length)
            throw new ArgumentException("Mask dimensions must match its pixel storage.");
        Width = width;
        Height = height;
        _pixels = (byte[])pixels.Clone();
        PixelCount = _pixels.LongCount(pixel => pixel != 0);
    }

    /// <summary>Width of the full output canvas.</summary>
    public int Width { get; }
    /// <summary>Height of the full output canvas.</summary>
    public int Height { get; }
    /// <summary>Number of protected spatial pixels.</summary>
    public long PixelCount { get; }
    /// <summary>Read-only binary pixels in row-major order.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;
    /// <summary>Imports a reviewed mask with independent storage and normalized binary pixels.</summary>
    public static FacialRoiMask FromBytes(int width, int height, ReadOnlySpan<byte> pixels)
    {
        if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue || (long)width * height != pixels.Length)
            throw new ArgumentException("Positive mask dimensions must match the provided pixel storage.");
        var normalized = pixels.ToArray();
        for (var index = 0; index < normalized.Length; index++) normalized[index] = normalized[index] == 0 ? (byte)0 : (byte)1;
        var mask = new FacialRoiMask(width, height, normalized);
        if (mask.PixelCount == 0) throw new ArgumentException("A reviewed facial mask must contain protected pixels.", nameof(pixels));
        return mask;
    }
    /// <summary>Returns independent pixel storage for callers that need an array.</summary>
    public byte[] ToArray() => (byte[])_pixels.Clone();
    /// <summary>Reports whether a canvas pixel belongs to the facial region.</summary>
    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && _pixels[y * Width + x] != 0;
}

/// <summary>How an anatomical dimension was obtained.</summary>
public enum GeometryEvidenceStatus
{
    /// <summary>An anatomical estimate derived from available facial landmarks.</summary>
    Estimated,
    /// <summary>An explicit anatomical measurement with recorded provenance.</summary>
    Measured
}

/// <summary>Recorded anatomical construction and its remaining visual verification.</summary>
public sealed record FacialRoiCoverage(
    string Algorithm,
    GeometryEvidenceStatus Status,
    float ForeheadExtensionRatio,
    float PaddingRatio,
    IReadOnlyList<Point2D> Boundary,
    IReadOnlyList<string> VerificationRequirements)
{
    /// <summary>Fixed region definition selected before allocation and regional byte measurement.</summary>
    public PivFaceRegion FaceRegion { get; init; } = PivFaceRegion.LandmarkFace;
}

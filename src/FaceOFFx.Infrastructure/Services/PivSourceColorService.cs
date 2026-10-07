using System.Security.Cryptography;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Transformations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>Prepares decoded eight-bit RGB samples for the PIV sRGB output profile.</summary>
public static class PivSourceColorService
{
    private const int MaximumProfileBytes = 1_048_576;
    internal const long MaximumSourcePixels = 64_000_000;

    /// <summary>Interprets untagged samples as sRGB or converts a supported embedded RGB matrix-shaper profile.</summary>
    /// <param name="image">Caller-owned source with exclusive access during preparation.</param>
    /// <returns>Color preparation evidence, or an input error preserving all samples and metadata.</returns>
    /// <remarks>
    /// The caller owns the image. Successful conversion replaces RGB samples and declares sRGB in EXIF;
    /// alpha samples are preserved. Validation and conversion finish in staging storage before writes.
    /// Failures preserve pixels and profile metadata. Callers must provide exclusive access during preparation.
    /// Sources are bounded to 64 million pixels. Conversion stages at most 256 million RGBA bytes;
    /// decoded-source and caller-isolation storage contribute additional peak memory.
    /// Untagged samples retain their values and carry an sRGB-assumption record for capture review.
    /// </remarks>
    public static Result<PivSourceColorEvidence, PipelineError> Prepare(Image<Rgba32> image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var count = checked((long)image.Width * image.Height);
        if (count > MaximumSourcePixels)
            return Failure($"PIV color preparation supports at most {MaximumSourcePixels} source pixels.");
        if (image.Frames.Count != 1) return Failure("PIV color preparation requires a single-frame source image.");
        try
        {
            var profile = image.Metadata.IccProfile;
            if (profile is null)
            {
                // Read a clone so lazy EXIF parsing also preserves the caller's metadata on failure.
                var exif = image.Metadata.ExifProfile?.DeepClone();
                if (exif?.TryGetValue(ExifTag.ColorSpace, out var colorSpace) == true && colorSpace.Value == 1)
                    return Success(new PivSourceColorEvidence(PivSourceColorStatus.ExifDeclaredSrgb));
                return Success(new PivSourceColorEvidence(PivSourceColorStatus.AssumedSrgb));
            }

            // ImageSharp exposes a bounded header before it parses the variable-length tag collection.
            if (profile.Header.Size is < 132 or > MaximumProfileBytes)
                return Failure($"Source ICC profile must contain 132..{MaximumProfileBytes} bytes.");
            var bytes = profile.ToByteArray();
            var transform = PivRgbMatrixProfile.Parse(bytes, MaximumProfileBytes);
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (transform.DescribesSrgb)
                return Success(new PivSourceColorEvidence(PivSourceColorStatus.EmbeddedSrgb, digest));
            var converted = new Rgba32[checked((int)count)];
            var replacementExif = image.Metadata.ExifProfile?.DeepClone() ?? new ExifProfile();
            replacementExif.SetValue(ExifTag.ColorSpace, (ushort)1);
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++) converted[y * image.Width + x] = transform.Convert(row[x]);
                }
            });
            var evidence = new PivSourceColorEvidence(PivSourceColorStatus.ConvertedMatrixRgb, digest);
            image.ProcessPixelRows(accessor =>
            {
                for (var y = 0; y < accessor.Height; y++)
                    converted.AsSpan(y * image.Width, image.Width).CopyTo(accessor.GetRowSpan(y));
            });
            image.Metadata.IccProfile = null;
            image.Metadata.ExifProfile = replacementExif;
            return Success(evidence);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or
                                          OverflowException or IndexOutOfRangeException or InvalidIccProfileException)
        {
            return Failure($"Source color profile requires correction or a supported conversion: {exception.Message}");
        }
    }

    private static Result<PivSourceColorEvidence, PipelineError> Success(PivSourceColorEvidence evidence) =>
        Result.Success<PivSourceColorEvidence, PipelineError>(evidence);

    private static Result<PivSourceColorEvidence, PipelineError> Failure(string message) =>
        Result.Failure<PivSourceColorEvidence, PipelineError>(new InputError(message, "source-color"));
}

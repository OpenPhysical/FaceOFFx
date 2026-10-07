using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Transformations;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Abstractions;

/// <summary>
/// Encodes RGB facial images with typed rate or byte-budget requirements.
/// </summary>
[PublicAPI]
public interface IJpeg2000Encoder
{
    /// <summary>
    /// Encodes a caller-owned image and returns measured byte and ROI diagnostics.
    /// </summary>
    Result<Jpeg2000EncodingResult, PipelineError> Encode(
        Image<Rgba32> image,
        FacialRoiSet roiSet,
        Jpeg2000EncodingOptions options
    );
}

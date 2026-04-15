using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Common;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Abstractions;

/// <summary>
/// Interface for JPEG 2000 encoding with ROI support.
/// </summary>
[PublicAPI]
public interface IJpeg2000Encoder
{
    /// <summary>
    /// Encodes an image to JPEG 2000 format with ROI Inner Region using maxshift method.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="roiSet">The ROI Inner Region for prioritized compression.</param>
    /// <param name="baseRate">Base compression rate in bits per pixel (default 1.0).</param>
    /// <param name="roiStartLevel">ROI resolution level priority (0=aggressive, 1-2=balanced).</param>
    /// <param name="enableRoi">Enable ROI encoding for facial region priority.</param>
    /// <param name="roiAlign">Align the ROI with the blocks</param>
    /// <returns>Result containing the encoded byte data or error.</returns>
    Result<byte[], PipelineError> EncodeWithRoi(
        Image<Rgba32> image,
        FacialRoiSet roiSet,
        float baseRate = 1.0f,
        int roiStartLevel = 1,
        bool enableRoi = false,
        bool roiAlign = true
    );
}

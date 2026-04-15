using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Common;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Abstractions;

/// <summary>
/// Simple interface for face detection services
/// </summary>
[PublicAPI]
public interface IFaceDetector
{
    /// <summary>
    /// Detects faces in an image
    /// </summary>
    /// <param name="image">Image as ImageSharp Image</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of detected faces</returns>
    Task<Result<IReadOnlyList<DetectedFace>, PipelineError>> DetectFacesAsync(
        Image<Rgba32> image,
        CancellationToken cancellationToken = default
    );
}

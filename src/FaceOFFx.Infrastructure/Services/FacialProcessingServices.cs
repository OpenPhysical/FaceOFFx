using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Shared runtime services for face detection, landmarks, and JPEG 2000 encoding.
/// </summary>
public sealed class FacialProcessingServices : IDisposable
{
    /// <summary>
    /// Gets the shared face detector instance.
    /// </summary>
    public IFaceDetector Detector { get; }

    /// <summary>
    /// Gets the shared landmark extractor instance.
    /// </summary>
    public ILandmarkExtractor LandmarkExtractor { get; }

    /// <summary>
    /// Gets the shared JPEG 2000 encoder instance.
    /// </summary>
    public IJpeg2000Encoder Encoder { get; }

    private FacialProcessingServices(
        IFaceDetector detector,
        ILandmarkExtractor landmarkExtractor,
        IJpeg2000Encoder encoder)
    {
        Detector = detector;
        LandmarkExtractor = landmarkExtractor;
        Encoder = encoder;
    }

    /// <summary>
    /// Creates the ONNX-backed facial processing services using the supplied logger factory.
    /// </summary>
    public static Result<FacialProcessingServices, PipelineError> Create(ILoggerFactory loggerFactory)
    {
        var detectorResult = RetinaFaceDetector.Create(loggerFactory.CreateLogger<RetinaFaceDetector>());
        if (detectorResult.IsFailure)
        {
            return Result.Failure<FacialProcessingServices, PipelineError>(detectorResult.Error);
        }

        var extractorResult = OnnxLandmarkExtractor.Create(
            loggerFactory.CreateLogger<OnnxLandmarkExtractor>());
        if (extractorResult.IsFailure)
        {
            detectorResult.Value.Dispose();
            return Result.Failure<FacialProcessingServices, PipelineError>(extractorResult.Error);
        }

        var encoder = new Jpeg2000EncoderService(loggerFactory.CreateLogger<Jpeg2000EncoderService>());
        return Result.Success<FacialProcessingServices, PipelineError>(
            new FacialProcessingServices(detectorResult.Value, extractorResult.Value, encoder));
    }

    /// <summary>
    /// Creates the ONNX-backed facial processing services using null-loggers.
    /// </summary>
    public static Result<FacialProcessingServices, PipelineError> Create(ILogger logger) =>
        Create(NullLoggerFactory.Instance);

    internal static FacialProcessingServices FromExisting(
        IFaceDetector detector,
        ILandmarkExtractor landmarkExtractor,
        IJpeg2000Encoder encoder) =>
        new(detector, landmarkExtractor, encoder);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Detector is IDisposable detector)
        {
            detector.Dispose();
        }

        if (LandmarkExtractor is IDisposable landmarkExtractor)
        {
            landmarkExtractor.Dispose();
        }
    }
}

using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Core.Domain.Transformations;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Public two-stage face geometry pipeline used by document rendering and diagnostics.
/// </summary>
public sealed class FaceGeometryPipeline(
    IFacialProcessingServiceFactory processingServiceFactory,
    ILogger<FaceGeometryPipeline> logger)
{
    private readonly IFacialProcessingServiceFactory _processingServiceFactory = processingServiceFactory;
    private readonly ILogger<FaceGeometryPipeline> _logger = logger;

    /// <summary>
    /// Detects all faces in an image and returns them ordered by descending confidence.
    /// </summary>
    /// <summary>
    /// Detects all faces in an image and returns them ordered by descending confidence.
    /// </summary>
    public async Task<Result<IReadOnlyList<DetectedFace>, PipelineError>> DetectFacesAsync(
        Image<Rgba32> image,
        CancellationToken cancellationToken = default)
    {
        var servicesResult = _processingServiceFactory.GetServices();
        if (servicesResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<DetectedFace>, PipelineError>(servicesResult.Error);
        }

        return (await servicesResult.Value.Detector
                .DetectFacesAsync(image, cancellationToken)
                .ConfigureAwait(false))
            .Map(faces => (IReadOnlyList<DetectedFace>)faces
                .OrderByDescending(face => face.Confidence)
                .ToArray());
    }

    /// <summary>
    /// Selects a single face that satisfies the supplied input profile.
    /// </summary>
    /// <summary>
    /// Selects one face for the supplied input profile and returns typed pipeline errors.
    /// </summary>
    public async Task<Result<DetectedFace, PipelineError>> SelectSingleFaceAsync(
        Image<Rgba32> image,
        InputProfileDefinition profile,
        CancellationToken cancellationToken = default)
    {
        var facesResult = await DetectFacesAsync(image, cancellationToken).ConfigureAwait(false);
        if (facesResult.IsFailure)
        {
            return Result.Failure<DetectedFace, PipelineError>(
                new DetectionError($"Input analysis failed: {facesResult.Error.Message}", profile.Id));
        }

        var faces = facesResult.Value
            .Where(face => face.Confidence >= profile.MinimumFaceConfidence)
            .ToArray();

        return faces.Length switch
        {
            0 => Result.Failure<DetectedFace, PipelineError>(
                new InputError($"{profile.DisplayName} rejected: no suitable face was detected.", profile.Id)),
            > 1 => Result.Failure<DetectedFace, PipelineError>(
                new InputError($"{profile.DisplayName} rejected: multiple faces were detected.", profile.Id)),
            _ => Result.Success<DetectedFace, PipelineError>(faces[0])
        };
    }

    /// <summary>
    /// Builds canonical original-space landmarks from a previously selected coarse detection.
    /// </summary>
    /// <summary>
    /// Builds canonical face geometry from a previously selected coarse detection.
    /// </summary>
    public async Task<Result<CanonicalFaceGeometry, PipelineError>> AnalyzeAsync(
        Image<Rgba32> sourceImage,
        DetectedFace detectedFace,
        CancellationToken cancellationToken = default)
    {
        var servicesResult = _processingServiceFactory.GetServices();
        if (servicesResult.IsFailure)
        {
            return Result.Failure<CanonicalFaceGeometry, PipelineError>(servicesResult.Error);
        }

        _logger.LogDebug(
            "Building canonical face geometry from coarse detection {BoundingBox}",
            detectedFace.BoundingBox);

        return await CanonicalFaceGeometryPipeline
            .ExtractAsync(
                sourceImage,
                detectedFace,
                servicesResult.Value.LandmarkExtractor,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Selects a single face using the supplied profile and builds canonical original-space landmarks.
    /// </summary>
    /// <summary>
    /// Selects a single face using the supplied profile and builds canonical original-space landmarks.
    /// </summary>
    public async Task<Result<CanonicalFaceGeometry, PipelineError>> AnalyzeSingleFaceAsync(
        Image<Rgba32> sourceImage,
        InputProfileDefinition profile,
        CancellationToken cancellationToken = default)
    {
        var faceResult = await SelectSingleFaceAsync(sourceImage, profile, cancellationToken)
            .ConfigureAwait(false);
        if (faceResult.IsFailure)
        {
            return Result.Failure<CanonicalFaceGeometry, PipelineError>(faceResult.Error);
        }

        return await AnalyzeAsync(sourceImage, faceResult.Value, cancellationToken).ConfigureAwait(false);
    }
}

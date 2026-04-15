using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
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
    IFaceDetector faceDetector,
    ILandmarkExtractor landmarkExtractor,
    ILogger<FaceGeometryPipeline> logger)
{
    private readonly IFaceDetector _faceDetector = faceDetector;
    private readonly ILandmarkExtractor _landmarkExtractor = landmarkExtractor;
    private readonly ILogger<FaceGeometryPipeline> _logger = logger;

    /// <summary>
    /// Detects all faces in an image and returns them ordered by descending confidence.
    /// </summary>
    public async Task<Result<IReadOnlyList<DetectedFace>>> DetectFacesAsync(
        Image<Rgba32> image,
        CancellationToken cancellationToken = default)
    {
        var facesResult = await _faceDetector
            .DetectFacesAsync(image, cancellationToken)
            .ConfigureAwait(false);
        if (facesResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<DetectedFace>>(facesResult.Error);
        }

        return Result.Success<IReadOnlyList<DetectedFace>>(facesResult.Value
            .OrderByDescending(face => face.Confidence)
            .ToArray());
    }

    /// <summary>
    /// Selects a single face that satisfies the supplied input profile.
    /// </summary>
    public async Task<Result<DetectedFace>> SelectSingleFaceAsync(
        Image<Rgba32> image,
        InputProfileDefinition profile,
        CancellationToken cancellationToken = default)
    {
        var facesResult = await DetectFacesAsync(image, cancellationToken).ConfigureAwait(false);
        if (facesResult.IsFailure)
        {
            return Result.Failure<DetectedFace>($"Input analysis failed: {facesResult.Error}");
        }

        var faces = facesResult.Value
            .Where(face => face.Confidence >= profile.MinimumFaceConfidence)
            .ToArray();

        if (faces.Length == 0)
        {
            return Result.Failure<DetectedFace>(
                $"{profile.DisplayName} rejected: no suitable face was detected.");
        }

        if (faces.Length > 1)
        {
            return Result.Failure<DetectedFace>(
                $"{profile.DisplayName} rejected: multiple faces were detected.");
        }

        return Result.Success(faces[0]);
    }

    /// <summary>
    /// Builds canonical original-space landmarks from a previously selected coarse detection.
    /// </summary>
    public async Task<Result<CanonicalFaceGeometry>> AnalyzeAsync(
        Image<Rgba32> sourceImage,
        DetectedFace detectedFace,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Building canonical face geometry from coarse detection {BoundingBox}",
            detectedFace.BoundingBox);

        return await CanonicalFaceGeometryPipeline
            .ExtractAsync(sourceImage, detectedFace, _landmarkExtractor, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Selects a single face using the supplied profile and builds canonical original-space landmarks.
    /// </summary>
    public async Task<Result<CanonicalFaceGeometry>> AnalyzeSingleFaceAsync(
        Image<Rgba32> sourceImage,
        InputProfileDefinition profile,
        CancellationToken cancellationToken = default)
    {
        var faceResult = await SelectSingleFaceAsync(sourceImage, profile, cancellationToken)
            .ConfigureAwait(false);
        if (faceResult.IsFailure)
        {
            return Result.Failure<CanonicalFaceGeometry>(faceResult.Error);
        }

        return await AnalyzeAsync(sourceImage, faceResult.Value, cancellationToken).ConfigureAwait(false);
    }
}

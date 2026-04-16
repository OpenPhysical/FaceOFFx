using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Canonical profile-based encoding pipeline.
/// </summary>
public sealed class ProfileEncoder(
    FaceGeometryPipeline faceGeometryPipeline,
    IFacialProcessingServiceFactory processingServiceFactory,
    ILogger<ProfileEncoder> logger)
{
    private readonly FaceGeometryPipeline _faceGeometryPipeline = faceGeometryPipeline;
    private readonly IFacialProcessingServiceFactory _processingServiceFactory = processingServiceFactory;
    private readonly ILogger<ProfileEncoder> _logger = logger;

    /// <summary>
    /// Processes source bytes into the specified profile output.
    /// </summary>
    public async Task<Result<ProfileEncodingResult, PipelineError>> ProcessAsync(
        byte[] imageData,
        ProfileSpecification profile,
        CancellationToken cancellationToken = default)
    {
        if (imageData.Length == 0)
        {
            return Result.Failure<ProfileEncodingResult, PipelineError>(
                new InputError("Input image data is required.", nameof(imageData)));
        }

        try
        {
            using var sourceImage = Image.Load<Rgba32>(imageData);
            return await ProcessAsync(sourceImage, profile, cancellationToken).ConfigureAwait(false);
        }
        catch (UnknownImageFormatException ex)
        {
            return Result.Failure<ProfileEncodingResult, PipelineError>(
                new InputError($"Input image format is not supported: {ex.Message}", nameof(imageData)));
        }
        catch (Exception ex)
        {
            return Result.Failure<ProfileEncodingResult, PipelineError>(
                new RenderError($"Failed to decode image bytes: {ex.Message}", profile.Id));
        }
    }

    internal async Task<Result<ProfileEncodingResult, PipelineError>> ProcessAsync(
        Image<Rgba32> sourceImage,
        ProfileSpecification profile,
        CancellationToken cancellationToken = default)
    {
        var faceResult = await SelectFaceAsync(sourceImage, profile, cancellationToken).ConfigureAwait(false);
        if (faceResult.IsFailure)
        {
            return Result.Failure<ProfileEncodingResult, PipelineError>(faceResult.Error);
        }

        var geometryResult = await _faceGeometryPipeline
            .AnalyzeAsync(sourceImage, faceResult.Value, cancellationToken)
            .ConfigureAwait(false);
        if (geometryResult.IsFailure)
        {
            return Result.Failure<ProfileEncodingResult, PipelineError>(geometryResult.Error);
        }

        return await ProcessAsync(sourceImage, geometryResult.Value, profile, cancellationToken).ConfigureAwait(false);
    }

    internal Task<Result<ProfileEncodingResult, PipelineError>> ProcessAsync(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        ProfileSpecification profile,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var servicesResult = _processingServiceFactory.GetServices();
        if (servicesResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<ProfileEncodingResult, PipelineError>(servicesResult.Error));
        }

        var planResult = PortraitPlanSolver.Solve(
            new ImageDimensions(sourceImage.Width, sourceImage.Height),
            geometry.SourceLandmarks,
            profile.Portrait);
        if (planResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<ProfileEncodingResult, PipelineError>(planResult.Error));
        }

        var renderResult = PortraitRenderer.Render(sourceImage, geometry, planResult.Value);
        if (renderResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<ProfileEncodingResult, PipelineError>(renderResult.Error));
        }

        using var renderedPortrait = renderResult.Value.Image;
        var encodingResult = EncodingPlanSolver.Encode(
            renderedPortrait,
            renderResult.Value.RoiSet,
            servicesResult.Value.Encoder,
            profile.Encoding);
        if (encodingResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<ProfileEncodingResult, PipelineError>(encodingResult.Error));
        }

        _logger.LogDebug(
            "Encoded {ProfileId} portrait at {Rate:F2} bpp with {FileSize} bytes.",
            profile.Id,
            encodingResult.Value.Decision.CompressionRate,
            encodingResult.Value.Decision.FileSize);

        var result = new ProfileEncodingResult(
            encodingResult.Value.ImageData,
            profile.Encoding.MimeType,
            profile,
            planResult.Value.OutputDimensions,
            renderResult.Value.Landmarks,
            planResult.Value.RotationDegrees,
            geometry.CoarseDetection.Confidence,
            encodingResult.Value.Decision,
            planResult.Value.CandidateTraces);

        return Task.FromResult(Result.Success<ProfileEncodingResult, PipelineError>(result));
    }

    internal Result<RenderedPortrait, PipelineError> RenderPortrait(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        ProfileSpecification profile)
    {
        var planResult = PortraitPlanSolver.Solve(
            new ImageDimensions(sourceImage.Width, sourceImage.Height),
            geometry.SourceLandmarks,
            profile.Portrait);
        if (planResult.IsFailure)
        {
            return Result.Failure<RenderedPortrait, PipelineError>(planResult.Error);
        }

        return PortraitRenderer.Render(sourceImage, geometry, planResult.Value);
    }

    private async Task<Result<DetectedFace, PipelineError>> SelectFaceAsync(
        Image<Rgba32> sourceImage,
        ProfileSpecification profile,
        CancellationToken cancellationToken)
    {
        var facesResult = await _faceGeometryPipeline.DetectFacesAsync(sourceImage, cancellationToken).ConfigureAwait(false);
        if (facesResult.IsFailure)
        {
            return Result.Failure<DetectedFace, PipelineError>(facesResult.Error);
        }

        var faces = facesResult.Value
            .Where(face => face.Confidence >= profile.FaceSelection.MinimumFaceConfidence)
            .Where(face => face.BoundingBox.Width >= profile.FaceSelection.MinimumFaceSizePixels)
            .Where(face => face.BoundingBox.Height >= profile.FaceSelection.MinimumFaceSizePixels)
            .ToArray();

        if (faces.Length == 0)
        {
            return Result.Failure<DetectedFace, PipelineError>(
                new DetectionError($"No face satisfied the '{profile.DisplayName}' capture requirements.", profile.Id));
        }

        if (profile.FaceSelection.RequireSingleFace && faces.Length != 1)
        {
            return Result.Failure<DetectedFace, PipelineError>(
                new DetectionError(
                    $"Profile '{profile.DisplayName}' requires exactly one usable face, but {faces.Length} faces were detected.",
                    profile.Id));
        }

        return Result.Success<DetectedFace, PipelineError>(faces[0]);
    }
}

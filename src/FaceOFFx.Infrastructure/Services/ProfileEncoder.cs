using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Canonical profile-based encoding pipeline.
/// </summary>
internal sealed class ProfileEncoder(
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
        ArgumentNullException.ThrowIfNull(imageData);
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        if (imageData.Length == 0)
        {
            return Result.Failure<ProfileEncodingResult, PipelineError>(
                new InputError("Input image data is required.", nameof(imageData)));
        }

        try
        {
            var sourceInfo = Image.Identify(imageData);
            if (checked((long)sourceInfo.Width * sourceInfo.Height) > PivSourceColorService.MaximumSourcePixels)
            {
                return Result.Failure<ProfileEncodingResult, PipelineError>(new InputError(
                    $"Source images support at most {PivSourceColorService.MaximumSourcePixels} pixels.", nameof(imageData)));
            }
            using var sourceImage = Image.Load<Rgba32>(imageData);
            return await ProcessAsync(sourceImage, profile, cancellationToken).ConfigureAwait(false);
        }
        catch (UnknownImageFormatException ex)
        {
            return Result.Failure<ProfileEncodingResult, PipelineError>(
                new InputError($"Input image format is not supported: {ex.Message}", nameof(imageData)));
        }
        catch (OperationCanceledException)
        {
            throw;
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
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        using var preparedImage = sourceImage.Clone();
        var colorResult = PivSourceColorService.Prepare(preparedImage);
        if (colorResult.IsFailure)
        {
            return Result.Failure<ProfileEncodingResult, PipelineError>(colorResult.Error);
        }
        preparedImage.Mutate(context => context.AutoOrient());
        var faceResult = await SelectFaceAsync(preparedImage, profile, cancellationToken).ConfigureAwait(false);
        if (faceResult.IsFailure)
        {
            return Result.Failure<ProfileEncodingResult, PipelineError>(faceResult.Error);
        }

        var geometryResult = await _faceGeometryPipeline
            .AnalyzeAsync(preparedImage, faceResult.Value, cancellationToken)
            .ConfigureAwait(false);
        if (geometryResult.IsFailure)
        {
            return Result.Failure<ProfileEncodingResult, PipelineError>(geometryResult.Error);
        }

        return await ProcessPreparedAsync(preparedImage, geometryResult.Value, profile,
            colorResult.Value, cancellationToken).ConfigureAwait(false);
    }

    internal Task<Result<ProfileEncodingResult, PipelineError>> ProcessAsync(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        ProfileSpecification profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceImage);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();
        using var preparedImage = sourceImage.Clone();
        if (preparedImage.Metadata.ExifProfile is { } exif &&
            exif.TryGetValue(ExifTag.Orientation, out var orientation) && orientation.Value > 1)
        {
            return Task.FromResult(Result.Failure<ProfileEncodingResult, PipelineError>(new InputError(
                "Normalize source orientation before supplying precomputed face geometry.", nameof(sourceImage))));
        }
        var colorResult = PivSourceColorService.Prepare(preparedImage);
        return colorResult.IsFailure
            ? Task.FromResult(Result.Failure<ProfileEncodingResult, PipelineError>(colorResult.Error))
            : ProcessPreparedAsync(preparedImage, geometry, profile, colorResult.Value, cancellationToken);
    }

    private Task<Result<ProfileEncodingResult, PipelineError>> ProcessPreparedAsync(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        ProfileSpecification profile,
        PivSourceColorEvidence colorEvidence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var servicesResult = _processingServiceFactory.GetServices();
        if (servicesResult.IsFailure)
        {
            return Task.FromResult(Result.Failure<ProfileEncodingResult, PipelineError>(servicesResult.Error));
        }

        var traces = new List<CandidateTrace>();
        PipelineError lastError = new GeometryError("Supply at least one portrait framing candidate.", profile.Id);
        foreach (var ratio in profile.Portrait.HeadWidthCandidateRatios)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var planResult = PortraitPlanSolver.Solve(
                new ImageDimensions(sourceImage.Width, sourceImage.Height),
                geometry.SourceLandmarks,
                profile.Portrait with { HeadWidthCandidateRatios = new[] { ratio } },
                geometry.SourceAnatomy);
            if (planResult.IsFailure)
            {
                lastError = planResult.Error;
                traces.Add(new CandidateTrace(ratio, Maybe<float>.None, lastError.Message));
                continue;
            }

            var renderResult = PortraitRenderer.Render(sourceImage, geometry, planResult.Value);
            if (renderResult.IsFailure)
            {
                lastError = renderResult.Error;
                traces.Add(new CandidateTrace(ratio, Maybe<float>.None, lastError.Message));
                continue;
            }

            using var renderedPortrait = renderResult.Value.Image;
            var encodingResult = EncodingPlanSolver.Encode(renderedPortrait, renderResult.Value.RoiSet,
                servicesResult.Value.Encoder, profile.Encoding);
            if (encodingResult.IsFailure)
            {
                lastError = encodingResult.Error;
                traces.Add(new CandidateTrace(ratio, Maybe<float>.None, lastError.Message));
                continue;
            }

            traces.AddRange(planResult.Value.CandidateTraces);
            _logger.LogDebug("Encoded {ProfileId} portrait at {Rate:F4} bpp with {FileSize} bytes.",
                profile.Id, encodingResult.Value.Decision.CompressionRate, encodingResult.Value.Decision.FileSize);
            var result = new ProfileEncodingResult(encodingResult.Value.ImageData, profile.Encoding.MimeType, profile,
                planResult.Value.OutputDimensions, renderResult.Value.Landmarks, planResult.Value.RotationDegrees,
                geometry.CoarseDetection.Confidence, encodingResult.Value.Decision, traces.ToArray())
            {
                GeometryEvidence = planResult.Value.GeometryEvidence,
                RoiCoverage = renderResult.Value.RoiSet.Coverage,
                SourceColorEvidence = colorEvidence
            };
            return Task.FromResult(Result.Success<ProfileEncodingResult, PipelineError>(result));
        }

        return Task.FromResult(Result.Failure<ProfileEncodingResult, PipelineError>(new RenderError(
            $"A source-supported crop and image allocation are required for '{profile.DisplayName}'. {string.Join(" ", traces.Select(trace => trace.Summary))} Final check: {lastError.Message}", profile.Id)));
    }

    internal Result<RenderedPortrait, PipelineError> RenderPortrait(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        ProfileSpecification profile)
    {
        var planResult = PortraitPlanSolver.Solve(
            new ImageDimensions(sourceImage.Width, sourceImage.Height),
            geometry.SourceLandmarks,
            profile.Portrait,
            geometry.SourceAnatomy);
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

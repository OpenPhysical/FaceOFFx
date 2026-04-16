using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Core.Domain.Transformations;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Shared document rendering service for canonical geometry and profile-based outputs.
/// </summary>
public sealed class DocumentRenderService(
    FaceGeometryPipeline faceGeometryPipeline,
    PassportPhotoRenderService passportPhotoRenderService,
    ProfileEncoder profileEncoder,
    ILogger<DocumentRenderService> logger)
{
    private readonly FaceGeometryPipeline _faceGeometryPipeline = faceGeometryPipeline;
    private readonly PassportPhotoRenderService _passportPhotoRenderService = passportPhotoRenderService;
    private readonly ProfileEncoder _profileEncoder = profileEncoder;
    private readonly ILogger<DocumentRenderService> _logger = logger;

    /// <summary>
    /// Builds canonical geometry for a single face selected by the supplied input profile.
    /// </summary>
    public Task<Result<CanonicalFaceGeometry, PipelineError>> AnalyzeSingleFaceAsync(
        Image<Rgba32> sourceImage,
        InputProfileDefinition profile,
        CancellationToken cancellationToken = default) =>
        _faceGeometryPipeline.AnalyzeSingleFaceAsync(sourceImage, profile, cancellationToken);

    /// <summary>
    /// Renders a passport-style portrait from canonical original-space landmarks.
    /// </summary>
    public Result<PassportPhotoRenderResult, PipelineError> RenderPassport(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        PassportPhotoSpec spec) =>
        _passportPhotoRenderService.Align(sourceImage, geometry, spec);

    /// <summary>
    /// Renders a built-in profile portrait from canonical original-space landmarks.
    /// </summary>
    public Result<RenderedPortrait, PipelineError> RenderProfile(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        ProfileSpecification profile)
    {
        _logger.LogDebug("Rendering {ProfileId} portrait from canonical face geometry.", profile.Id);
        return _profileEncoder.RenderPortrait(sourceImage, geometry, profile);
    }

    /// <summary>
    /// Renders and encodes a built-in profile artifact from canonical original-space landmarks.
    /// </summary>
    public Task<Result<ProfileEncodingResult, PipelineError>> EncodeProfileAsync(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        ProfileSpecification profile,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Encoding {ProfileId} portrait from canonical face geometry.", profile.Id);
        return _profileEncoder.ProcessAsync(sourceImage, geometry, profile, cancellationToken);
    }
}

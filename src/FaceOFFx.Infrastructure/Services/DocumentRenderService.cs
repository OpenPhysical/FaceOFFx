using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Core.Domain.Transformations;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Public document render service for shared two-stage geometry based rendering.
/// </summary>
public sealed class DocumentRenderService(
    FaceGeometryPipeline faceGeometryPipeline,
    PassportPhotoRenderService passportPhotoRenderService,
    IJpeg2000Encoder jpeg2000Encoder,
    ILogger<DocumentRenderService> logger)
{
    private readonly FaceGeometryPipeline _faceGeometryPipeline = faceGeometryPipeline;
    private readonly PassportPhotoRenderService _passportPhotoRenderService = passportPhotoRenderService;
    private readonly IJpeg2000Encoder _jpeg2000Encoder = jpeg2000Encoder;
    private readonly ILogger<DocumentRenderService> _logger = logger;

    /// <summary>
    /// Builds canonical geometry for a single face selected by the supplied input profile.
    /// </summary>
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
    /// <summary>
    /// Renders a passport-style portrait from canonical original-space landmarks.
    /// </summary>
    public Result<PassportPhotoRenderResult, PipelineError> RenderPassport(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        PassportPhotoSpec spec) =>
        _passportPhotoRenderService.Align(sourceImage, geometry, spec);

    /// <summary>
    /// Renders a PIV portrait from canonical original-space landmarks.
    /// </summary>
    /// <summary>
    /// Renders a PIV portrait from canonical original-space landmarks.
    /// </summary>
    public Result<PivLandmarkResult, PipelineError> RenderPiv(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        PivProcessingOptions options)
    {
        _logger.LogDebug("Rendering PIV portrait from canonical face geometry.");
        return PivLandmarkProcessor.ProcessAsync(sourceImage, geometry, options, _logger)
            .ToPipelineResult(error => new RenderError(error, "piv-render"));
    }

    /// <summary>
    /// Renders and JPEG 2000 encodes a PIV card image from canonical original-space landmarks.
    /// </summary>
    /// <summary>
    /// Renders and JPEG 2000 encodes a PIV card image from canonical original-space landmarks.
    /// </summary>
    public Result<PivRenderedArtifact, PipelineError> RenderPivCard(
        Image<Rgba32> sourceImage,
        CanonicalFaceGeometry geometry,
        PivProcessingOptions options)
    {
        var pivResult = RenderPiv(sourceImage, geometry, options);
        if (pivResult.IsFailure)
        {
            return Result.Failure<PivRenderedArtifact, PipelineError>(pivResult.Error);
        }

        using var imageForEncoding = pivResult.Value.PivImage.Clone();
        return _jpeg2000Encoder.EncodeWithRoi(
                imageForEncoding,
                pivResult.Value.RoiSet,
                options.BaseRate,
                options.RoiStartLevel,
                enableRoi: true,
                roiAlign: false)
            .Map(encoded => new PivRenderedArtifact(encoded, pivResult.Value));
    }
}

/// <summary>
/// Encoded PIV render artifact plus its deterministic geometry result.
/// </summary>
public sealed record PivRenderedArtifact(byte[] ImageData, PivLandmarkResult Geometry);

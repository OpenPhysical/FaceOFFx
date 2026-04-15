using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Diagnostics.Cli.Services;

internal sealed class DiagnosticsBatchService(
    FaceGeometryPipeline faceGeometryPipeline,
    DocumentRenderService documentRenderService,
    DocumentJobRunner documentJobRunner,
    ILogger<DiagnosticsBatchService> logger)
{
    private readonly FaceGeometryPipeline _faceGeometryPipeline = faceGeometryPipeline;
    private readonly DocumentRenderService _documentRenderService = documentRenderService;
    private readonly DocumentJobRunner _documentJobRunner = documentJobRunner;
    private readonly ILogger<DiagnosticsBatchService> _logger = logger;

    public async Task<Result<DetectionAnalysisResult, PipelineError>> DetectAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var imageResult = await LoadImageAsync(inputPath, cancellationToken).ConfigureAwait(false);
        if (imageResult.IsFailure)
        {
            return Result.Failure<DetectionAnalysisResult, PipelineError>(imageResult.Error);
        }

        using var image = imageResult.Value;
        var facesResult = await _faceGeometryPipeline.DetectFacesAsync(image, cancellationToken);
        if (facesResult.IsFailure)
        {
            return Result.Failure<DetectionAnalysisResult, PipelineError>(facesResult.Error);
        }

        var faces = facesResult.Value.OrderByDescending(face => face.Confidence).ToArray();
        FaceLandmarks68? landmarks = null;
        Maybe<DetectedFace> primaryFace = Maybe<DetectedFace>.None;
        Maybe<CanonicalFaceGeometry> canonicalGeometry = Maybe<CanonicalFaceGeometry>.None;
        IReadOnlyList<PointDto> chipPolygon = Array.Empty<PointDto>();
        IReadOnlyList<PointDto> coarseLandmarks = Array.Empty<PointDto>();
        if (faces.Length > 0)
        {
            primaryFace = Maybe<DetectedFace>.From(faces[0]);
            if (faces[0].Landmarks5.HasValue)
            {
                var coarseResult = faces[0].Landmarks5.ToPipelineResult(
                    new DetectionError("Coarse 5-point landmarks were expected.", inputPath));
                if (coarseResult.IsFailure)
                {
                    return Result.Failure<DetectionAnalysisResult, PipelineError>(coarseResult.Error);
                }

                var coarse = coarseResult.Value;
                coarseLandmarks = new[]
                {
                    new PointDto(coarse.LeftEye.X, coarse.LeftEye.Y),
                    new PointDto(coarse.RightEye.X, coarse.RightEye.Y),
                    new PointDto(coarse.Nose.X, coarse.Nose.Y),
                    new PointDto(coarse.LeftMouth.X, coarse.LeftMouth.Y),
                    new PointDto(coarse.RightMouth.X, coarse.RightMouth.Y)
                };
            }

            var geometryResult = await _faceGeometryPipeline
                .AnalyzeAsync(image, faces[0], cancellationToken);
            if (geometryResult.IsSuccess)
            {
                canonicalGeometry = Maybe<CanonicalFaceGeometry>.From(geometryResult.Value);
                landmarks = geometryResult.Value.SourceLandmarks;
                chipPolygon = BuildChipPolygon(geometryResult.Value);
            }
        }

        return Result.Success<DetectionAnalysisResult, PipelineError>(new DetectionAnalysisResult(
            inputPath,
            image.Width,
            image.Height,
            faces.Select(face => new DetectionFaceResult(face.BoundingBox.X, face.BoundingBox.Y, face.BoundingBox.Width, face.BoundingBox.Height, face.Confidence)).ToArray(),
            coarseLandmarks,
            chipPolygon,
            landmarks?.Points.Select(point => new PointDto(point.X, point.Y)).ToArray() ?? Array.Empty<PointDto>(),
            primaryFace,
            canonicalGeometry));
    }

    public async Task<Result<OverlayAnalysisResult, PipelineError>> AnalyzeOverlayAsync(
        string inputPath,
        string? profileId,
        CancellationToken cancellationToken = default)
    {
        var detection = await DetectAsync(inputPath, cancellationToken);
        if (detection.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult, PipelineError>(detection.Error);
        }

        return await AnalyzeOverlayAsync(inputPath, detection.Value, profileId, cancellationToken);
    }

    public async Task<Result<OverlayAnalysisResult, PipelineError>> AnalyzeOverlayAsync(
        string inputPath,
        DetectionAnalysisResult detection,
        string? profileId,
        CancellationToken cancellationToken = default)
    {
        var imageResult = await LoadImageAsync(inputPath, cancellationToken).ConfigureAwait(false);
        if (imageResult.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult, PipelineError>(imageResult.Error);
        }

        using var image = imageResult.Value;

        if (string.IsNullOrWhiteSpace(profileId))
        {
            return Result.Success<OverlayAnalysisResult, PipelineError>(new OverlayAnalysisResult(detection, Array.Empty<PointDto>(), Array.Empty<PointDto>(), Array.Empty<PointDto>(), "raw"));
        }

        if (string.Equals(profileId, "piv", StringComparison.OrdinalIgnoreCase))
        {
            return await AnalyzePivOverlayAsync(inputPath, detection, cancellationToken);
        }

        return await AnalyzePassportStyleOverlayAsync(inputPath, detection, profileId!, cancellationToken);
    }

    public Task<Result<DocumentJobResult, PipelineError>> CropAsync(
        string inputPath,
        string profileId,
        string? variant,
        string outputDirectory,
        CancellationToken cancellationToken = default) =>
        _documentJobRunner.RunAsync(
            new DocumentJobRequest(inputPath, profileId, variant, outputDirectory),
            cancellationToken);

    public static async Task WriteManifestAsync<T>(string outputPath, T manifest, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);
    }

    private static IReadOnlyList<PointDto> BuildChipPolygon(CanonicalFaceGeometry geometry)
    {
        var width = geometry.ChipDimensions.Width;
        var height = geometry.ChipDimensions.Height;
        var corners = new[]
        {
            new Point2D(0, 0),
            new Point2D(width, 0),
            new Point2D(width, height),
            new Point2D(0, height)
        };

        return corners
            .Select(geometry.ChipToSource.TransformPoint)
            .Select(point => new PointDto(point.X, point.Y))
            .ToArray();
    }

    private async Task<Result<OverlayAnalysisResult, PipelineError>> AnalyzePivOverlayAsync(
        string inputPath,
        DetectionAnalysisResult detection,
        CancellationToken cancellationToken)
    {
        var geometryResult = detection.CanonicalGeometry.ToPipelineResult(
            new GeometryError("Canonical geometry was not available for PIV overlay rendering.", "piv"));
        if (geometryResult.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult, PipelineError>(geometryResult.Error);
        }

        var sourceImageResult = await LoadImageAsync(inputPath, cancellationToken).ConfigureAwait(false);
        if (sourceImageResult.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult, PipelineError>(sourceImageResult.Error);
        }

        using var sourceImage = sourceImageResult.Value;
        var pivResult = _documentRenderService.RenderPiv(
            sourceImage,
            geometryResult.Value,
            PivProcessingOptions.Default);
        if (pivResult.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult, PipelineError>(pivResult.Error);
        }

        var transformedLandmarks = pivResult.Value.Landmarks;
        var transformMapResult = RenderTransformMapBuilder.CreateRotateCropResize(
            new ImageDimensions(sourceImage.Width, sourceImage.Height),
            pivResult.Value.AppliedRotation,
            RenderTransformMapBuilder.ComputeExpandedRotationDimensions(
                new ImageDimensions(sourceImage.Width, sourceImage.Height),
                pivResult.Value.AppliedRotation),
            pivResult.Value.FaceCrop,
            pivResult.Value.Dimensions);
        if (transformMapResult.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult, PipelineError>(transformMapResult.Error);
        }

        var transformMap = transformMapResult.Value;

        var projected = transformedLandmarks.Points
            .Select(transformMap.MapOutputToSource)
            .Select(point => new PointDto(point.X, point.Y))
            .ToArray();

        var cropPolygon = transformMap.OutputBoundsOnSource()
            .Select(point => new PointDto(point.X, point.Y))
            .ToArray();

        return Result.Success<OverlayAnalysisResult, PipelineError>(new OverlayAnalysisResult(
            detection,
            projected,
            cropPolygon,
            transformedLandmarks.Points.Select(point => new PointDto(point.X, point.Y)).ToArray(),
            "piv"));
    }

    private async Task<Result<OverlayAnalysisResult, PipelineError>> AnalyzePassportStyleOverlayAsync(
        string inputPath,
        DetectionAnalysisResult detection,
        string documentId,
        CancellationToken cancellationToken)
    {
        var documentResult = DocumentCatalog.GetDocument(documentId);
        if (documentResult.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult, PipelineError>(documentResult.Error);
        }

        var document = documentResult.Value;
        var variantId = document.PrimaryVariantId;
        var deliverable = document.Variants[variantId].Deliverables.First();
        var specId = deliverable.ProductionDefaults["spec"];
        var specResult = DocumentCatalog.GetPassportPhotoSpec(specId);
        if (specResult.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult, PipelineError>(specResult.Error);
        }

        var spec = specResult.Value;

        var geometryResult = detection.CanonicalGeometry.ToPipelineResult(
            new GeometryError("Canonical geometry was not available for document overlay rendering.", documentId));
        if (geometryResult.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult, PipelineError>(geometryResult.Error);
        }

        var sourceImageResult = await LoadImageAsync(inputPath, cancellationToken).ConfigureAwait(false);
        if (sourceImageResult.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult, PipelineError>(sourceImageResult.Error);
        }

        using var sourceImage = sourceImageResult.Value;
        var alignment = _documentRenderService.RenderPassport(
            sourceImage,
            geometryResult.Value,
            spec);
        if (alignment.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult, PipelineError>(alignment.Error);
        }

        using var alignedPortrait = alignment.Value.Image;
        var projected = alignment.Value.Landmarks.Points
            .Select(alignment.Value.TransformMap.MapOutputToSource)
            .Select(point => new PointDto(point.X, point.Y))
            .ToArray();

        var cropPolygon = alignment.Value.TransformMap.OutputBoundsOnSource()
            .Select(point => new PointDto(point.X, point.Y))
            .ToArray();

        return Result.Success<OverlayAnalysisResult, PipelineError>(new OverlayAnalysisResult(
            detection,
            projected,
            cropPolygon,
            alignment.Value.Landmarks.Points.Select(point => new PointDto(point.X, point.Y)).ToArray(),
            documentId));
    }

    private static async Task<Result<Image<Rgba32>, PipelineError>> LoadImageAsync(string inputPath, CancellationToken cancellationToken)
    {
        try
        {
            return Result.Success<Image<Rgba32>, PipelineError>(
                await Image.LoadAsync<Rgba32>(inputPath, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return Result.Failure<Image<Rgba32>, PipelineError>(
                new InputError($"Failed to load image '{inputPath}': {ex.Message}", inputPath));
        }
    }

}

internal sealed record DetectionAnalysisResult(
    string InputPath,
    int Width,
    int Height,
    IReadOnlyList<DetectionFaceResult> Faces,
    IReadOnlyList<PointDto> CoarseLandmarks,
    IReadOnlyList<PointDto> ChipPolygon,
    IReadOnlyList<PointDto> RawLandmarks,
    [property: JsonIgnore] Maybe<DetectedFace> PrimaryFace,
    [property: JsonIgnore] Maybe<CanonicalFaceGeometry> CanonicalGeometry);

internal sealed record DetectionFaceResult(float X, float Y, float Width, float Height, float Confidence);

internal sealed record OverlayAnalysisResult(
    DetectionAnalysisResult Detection,
    IReadOnlyList<PointDto> ProjectedLandmarks,
    IReadOnlyList<PointDto> CropPolygon,
    IReadOnlyList<PointDto> OutputLandmarks,
    string ProfileId);

internal sealed record PointDto(float X, float Y);

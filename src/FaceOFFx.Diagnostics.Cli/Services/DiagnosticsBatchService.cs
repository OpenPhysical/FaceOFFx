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

    public async Task<Result<DetectionAnalysisResult>> DetectAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        using var image = await Image.LoadAsync<Rgba32>(inputPath, cancellationToken);
        var facesResult = await _faceGeometryPipeline.DetectFacesAsync(image, cancellationToken);
        if (facesResult.IsFailure)
        {
            return Result.Failure<DetectionAnalysisResult>(facesResult.Error);
        }

        var faces = facesResult.Value.OrderByDescending(face => face.Confidence).ToArray();
        FaceLandmarks68? landmarks = null;
        Maybe<DetectedFace> primaryFace = Maybe<DetectedFace>.None;
        Maybe<CanonicalFaceGeometry> canonicalGeometry = Maybe<CanonicalFaceGeometry>.None;
        IReadOnlyList<PointDto> coarseLandmarks = Array.Empty<PointDto>();
        if (faces.Length > 0)
        {
            primaryFace = Maybe<DetectedFace>.From(faces[0]);
            if (faces[0].Landmarks5.HasValue)
            {
                var coarse = faces[0].Landmarks5.GetValueOrThrow("Coarse 5-point landmarks were expected.");
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
            }
        }

        return Result.Success(new DetectionAnalysisResult(
            inputPath,
            image.Width,
            image.Height,
            faces.Select(face => new DetectionFaceResult(face.BoundingBox.X, face.BoundingBox.Y, face.BoundingBox.Width, face.BoundingBox.Height, face.Confidence)).ToArray(),
            coarseLandmarks,
            canonicalGeometry.HasValue
                ? BuildChipPolygon(canonicalGeometry.GetValueOrThrow("Canonical geometry is required for chip polygon projection."))
                : Array.Empty<PointDto>(),
            landmarks?.Points.Select(point => new PointDto(point.X, point.Y)).ToArray() ?? Array.Empty<PointDto>(),
            primaryFace,
            canonicalGeometry));
    }

    public async Task<Result<OverlayAnalysisResult>> AnalyzeOverlayAsync(
        string inputPath,
        string? profileId,
        CancellationToken cancellationToken = default)
    {
        var detection = await DetectAsync(inputPath, cancellationToken);
        if (detection.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult>(detection.Error);
        }

        return await AnalyzeOverlayAsync(inputPath, detection.Value, profileId, cancellationToken);
    }

    public async Task<Result<OverlayAnalysisResult>> AnalyzeOverlayAsync(
        string inputPath,
        DetectionAnalysisResult detection,
        string? profileId,
        CancellationToken cancellationToken = default)
    {
        using var image = await Image.LoadAsync<Rgba32>(inputPath, cancellationToken);

        if (string.IsNullOrWhiteSpace(profileId))
        {
            return Result.Success(new OverlayAnalysisResult(detection, Array.Empty<PointDto>(), Array.Empty<PointDto>(), Array.Empty<PointDto>(), "raw"));
        }

        if (string.Equals(profileId, "piv", StringComparison.OrdinalIgnoreCase))
        {
            return await AnalyzePivOverlayAsync(inputPath, detection, cancellationToken);
        }

        return await AnalyzePassportStyleOverlayAsync(inputPath, detection, profileId!, cancellationToken);
    }

    public Task<Result<DocumentJobResult>> CropAsync(
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

    private async Task<Result<OverlayAnalysisResult>> AnalyzePivOverlayAsync(
        string inputPath,
        DetectionAnalysisResult detection,
        CancellationToken cancellationToken)
    {
        if (detection.CanonicalGeometry.HasNoValue)
        {
            return Result.Failure<OverlayAnalysisResult>("Canonical geometry was not available for PIV overlay rendering.");
        }

        using var sourceImage = await Image.LoadAsync<Rgba32>(inputPath, cancellationToken);
        var pivResult = _documentRenderService.RenderPiv(
            sourceImage,
            detection.CanonicalGeometry.GetValueOrThrow("Canonical geometry is required for PIV overlays."),
            PivProcessingOptions.Default);
        if (pivResult.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult>(pivResult.Error);
        }

        var transformedLandmarks = pivResult.Value.Landmarks;
        var transformMap = RenderTransformMapBuilder.CreateRotateCropResize(
            new ImageDimensions(sourceImage.Width, sourceImage.Height),
            pivResult.Value.AppliedRotation,
            RenderTransformMapBuilder.ComputeExpandedRotationDimensions(
                new ImageDimensions(sourceImage.Width, sourceImage.Height),
                pivResult.Value.AppliedRotation),
            pivResult.Value.FaceCrop,
            pivResult.Value.Dimensions);

        var projected = transformedLandmarks.Points
            .Select(transformMap.MapOutputToSource)
            .Select(point => new PointDto(point.X, point.Y))
            .ToArray();

        var cropPolygon = transformMap.OutputBoundsOnSource()
            .Select(point => new PointDto(point.X, point.Y))
            .ToArray();

        return Result.Success(new OverlayAnalysisResult(
            detection,
            projected,
            cropPolygon,
            transformedLandmarks.Points.Select(point => new PointDto(point.X, point.Y)).ToArray(),
            "piv"));
    }

    private async Task<Result<OverlayAnalysisResult>> AnalyzePassportStyleOverlayAsync(
        string inputPath,
        DetectionAnalysisResult detection,
        string documentId,
        CancellationToken cancellationToken)
    {
        var document = DocumentCatalog.GetDocumentOrThrow(documentId);
        var variantId = document.PrimaryVariantId;
        var deliverable = document.Variants[variantId].Deliverables.First();
        var specId = deliverable.ProductionDefaults["spec"];
        var spec = DocumentCatalog.GetPassportPhotoSpecOrThrow(specId);

        if (detection.CanonicalGeometry.HasNoValue)
        {
            return Result.Failure<OverlayAnalysisResult>("Canonical geometry was not available for document overlay rendering.");
        }

        using var sourceImage = await Image.LoadAsync<Rgba32>(inputPath, cancellationToken);
        var alignment = _documentRenderService.RenderPassport(
            sourceImage,
            detection.CanonicalGeometry.GetValueOrThrow("Canonical geometry is required for overlay rendering."),
            spec);
        if (alignment.IsFailure)
        {
            return Result.Failure<OverlayAnalysisResult>(alignment.Error);
        }

        using var alignedPortrait = alignment.Value.Image;
        var projected = alignment.Value.Landmarks.Points
            .Select(alignment.Value.TransformMap.MapOutputToSource)
            .Select(point => new PointDto(point.X, point.Y))
            .ToArray();

        var cropPolygon = alignment.Value.TransformMap.OutputBoundsOnSource()
            .Select(point => new PointDto(point.X, point.Y))
            .ToArray();

        return Result.Success(new OverlayAnalysisResult(
            detection,
            projected,
            cropPolygon,
            alignment.Value.Landmarks.Points.Select(point => new PointDto(point.X, point.Y)).ToArray(),
            documentId));
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

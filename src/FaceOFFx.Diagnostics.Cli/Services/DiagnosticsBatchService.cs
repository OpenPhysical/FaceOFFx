using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Diagnostics.Cli.Services;

internal sealed class DiagnosticsBatchService(
    FaceGeometryPipeline faceGeometryPipeline,
    ILogger<DiagnosticsBatchService> logger)
{
    private readonly FaceGeometryPipeline _faceGeometryPipeline = faceGeometryPipeline;
    private readonly ILogger<DiagnosticsBatchService> _logger = logger;

    public async Task<Result<DetectionAnalysisResult, PipelineError>> DetectAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        var imageResult = await LoadImageAsync(inputPath, cancellationToken).ConfigureAwait(false);
        if (imageResult.IsFailure)
        {
            return Result.Failure<DetectionAnalysisResult, PipelineError>(imageResult.Error);
        }

        using var image = imageResult.Value;
        var facesResult = await _faceGeometryPipeline.DetectFacesAsync(image, cancellationToken)
            .ConfigureAwait(false);
        if (facesResult.IsFailure)
        {
            return Result.Failure<DetectionAnalysisResult, PipelineError>(facesResult.Error);
        }

        var faces = facesResult.Value.OrderByDescending(face => face.Confidence).ToArray();
        FaceLandmarks68? sourceLandmarks = null;
        Maybe<DetectedFace> primaryFace = Maybe<DetectedFace>.None;
        Maybe<CanonicalFaceGeometry> canonicalGeometry = Maybe<CanonicalFaceGeometry>.None;
        IReadOnlyList<PointDto> chipPolygon = Array.Empty<PointDto>();
        IReadOnlyList<PointDto> coarseLandmarks = Array.Empty<PointDto>();
        string? geometryError = null;

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
                coarseLandmarks =
                [
                    new PointDto(coarse.LeftEye.X, coarse.LeftEye.Y),
                    new PointDto(coarse.RightEye.X, coarse.RightEye.Y),
                    new PointDto(coarse.Nose.X, coarse.Nose.Y),
                    new PointDto(coarse.LeftMouth.X, coarse.LeftMouth.Y),
                    new PointDto(coarse.RightMouth.X, coarse.RightMouth.Y)
                ];
            }

            var geometryResult = await _faceGeometryPipeline
                .AnalyzeAsync(image, faces[0], cancellationToken)
                .ConfigureAwait(false);
            if (geometryResult.IsSuccess)
            {
                canonicalGeometry = Maybe<CanonicalFaceGeometry>.From(geometryResult.Value);
                sourceLandmarks = geometryResult.Value.SourceLandmarks;
                chipPolygon = BuildChipPolygon(geometryResult.Value);
            }
            else
            {
                geometryError = geometryResult.Error.Message;
                _logger.LogDebug(
                    "Canonical geometry unavailable for {InputPath}: {Error}",
                    inputPath,
                    geometryError);
            }
        }
        else
        {
            geometryError = "No faces were detected.";
        }

        return Result.Success<DetectionAnalysisResult, PipelineError>(new DetectionAnalysisResult(
            inputPath,
            image.Width,
            image.Height,
            faces.Select(face => new DetectionFaceResult(
                face.BoundingBox.X,
                face.BoundingBox.Y,
                face.BoundingBox.Width,
                face.BoundingBox.Height,
                face.Confidence)).ToArray(),
            coarseLandmarks,
            chipPolygon,
            sourceLandmarks?.Points.Select(point => new PointDto(point.X, point.Y)).ToArray()
            ?? Array.Empty<PointDto>(),
            geometryError,
            primaryFace,
            canonicalGeometry));
    }

    public static async Task WriteManifestAsync<T>(
        string outputPath,
        T manifest,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);
    }

    private static IReadOnlyList<PointDto> BuildChipPolygon(CanonicalFaceGeometry geometry)
    {
        var corners = new[]
        {
            new Point2D(0, 0),
            new Point2D(geometry.ChipDimensions.Width, 0),
            new Point2D(geometry.ChipDimensions.Width, geometry.ChipDimensions.Height),
            new Point2D(0, geometry.ChipDimensions.Height)
        };

        return corners
            .Select(geometry.ChipToSource.TransformPoint)
            .Select(point => new PointDto(point.X, point.Y))
            .ToArray();
    }

    private static async Task<Result<Image<Rgba32>, PipelineError>> LoadImageAsync(
        string inputPath,
        CancellationToken cancellationToken)
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
    string? GeometryError,
    [property: JsonIgnore] Maybe<DetectedFace> PrimaryFace,
    [property: JsonIgnore] Maybe<CanonicalFaceGeometry> CanonicalGeometry);

internal sealed record DetectionFaceResult(float X, float Y, float Width, float Height, float Confidence);

internal sealed record PointDto(float X, float Y);

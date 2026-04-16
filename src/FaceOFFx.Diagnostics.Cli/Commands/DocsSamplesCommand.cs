using System.ComponentModel;
using CoreJ2K;
using CoreJ2K.ImageSharp;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Diagnostics.Cli.Services;
using FaceOFFx.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Diagnostics.Cli.Commands;

internal sealed class DocsSamplesCommand(
    DiagnosticsCorpusService corpusService,
    FaceGeometryPipeline geometryPipeline,
    IFacialProcessingServiceFactory processingServiceFactory,
    ProfileEncoder profileEncoder,
    DocumentRenderService documentRenderService,
    IAnsiConsole console) : AsyncCommand<DocsSamplesCommand.Settings>
{
    private const int ThumbnailWidth = 420;
    private static readonly JpegEncoder ThumbnailEncoder = new() { Quality = 90 };
    private static bool imageSharpRegistered;

    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--corpus <ID>")]
        [Description("Named corpus id used for README samples")]
        public string CorpusId { get; init; } = "people";

        [CommandOption("-o|--output <DIR>")]
        [Description("Output directory for README sample assets")]
        public string OutputDirectory { get; init; } = Path.Combine("docs", "samples");

        [CommandOption("--quality-subject <ID>")]
        [Description("Subject id used for bitrate comparison assets")]
        public string QualitySubject { get; init; } = "starmer";

        [CommandOption("--crop-subject <ID>")]
        [Description("Subject id used for cross-document crop assets")]
        public string CropSubject { get; init; } = "starmer";
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var subjectsResult = corpusService.ResolveInputs(null, settings.CorpusId);
        if (subjectsResult.IsFailure)
        {
            console.MarkupLine($"[red]{Markup.Escape(subjectsResult.Error.Message)}[/]");
            return 1;
        }

        var sampleSubjects = subjectsResult.Value
            .Where(subject => IsReadmeSubject(subject.Id))
            .OrderBy(subject => subject.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var qualitySubjectResult = FindSubject(subjectsResult.Value, settings.QualitySubject);
        var cropSubjectResult = FindSubject(subjectsResult.Value, settings.CropSubject);
        var canadaPrSpecResult = DocumentCatalog.GetPassportPhotoSpec("canada-print");
        if (qualitySubjectResult.IsFailure)
        {
            console.MarkupLine($"[red]{Markup.Escape(qualitySubjectResult.Error.Message)}[/]");
            return 1;
        }

        if (cropSubjectResult.IsFailure)
        {
            console.MarkupLine($"[red]{Markup.Escape(cropSubjectResult.Error.Message)}[/]");
            return 1;
        }

        var servicesResult = processingServiceFactory.GetServices();
        if (servicesResult.IsFailure)
        {
            console.MarkupLine($"[red]{Markup.Escape(servicesResult.Error.Message)}[/]");
            return 1;
        }

        if (canadaPrSpecResult.IsFailure)
        {
            console.MarkupLine($"[red]{Markup.Escape(canadaPrSpecResult.Error.Message)}[/]");
            return 1;
        }

        var outputRoot = Path.GetFullPath(settings.OutputDirectory);
        var originalDirectory = Path.Combine(outputRoot, "original");
        var processedDirectory = Path.Combine(outputRoot, "processed");
        Directory.CreateDirectory(originalDirectory);
        Directory.CreateDirectory(processedDirectory);

        var manifestRecords = new List<DocsSampleRecord>();
        foreach (var subject in sampleSubjects)
        {
            var subjectName = NormalizeSubjectName(subject.Id);
            using var sourceImage = await Image.LoadAsync<Rgba32>(subject.InputPath, cancellationToken);
            var thumbnailPath = Path.Combine(originalDirectory, $"{subjectName}_420w.jpg");
            await SaveSourceThumbnailAsync(sourceImage, thumbnailPath, cancellationToken).ConfigureAwait(false);

            var pivRecordResult = await GenerateProfilePreviewAsync(
                subject.InputPath,
                subjectName,
                ProfileSpecifications.Piv,
                Path.Combine(processedDirectory, $"{subjectName}_piv.png"),
                cancellationToken).ConfigureAwait(false);
            if (pivRecordResult.IsFailure)
            {
                console.MarkupLine($"[red]{Markup.Escape(pivRecordResult.Error.Message)}[/]");
                return 1;
            }

            manifestRecords.Add(pivRecordResult.Value);
        }

        using var qualityImage = await Image.LoadAsync<Rgba32>(qualitySubjectResult.Value.InputPath, cancellationToken);
        var ladderResult = await GenerateRateLadderAsync(
            qualityImage,
            qualitySubjectResult.Value,
            processedDirectory,
            servicesResult.Value.Encoder,
            cancellationToken).ConfigureAwait(false);
        if (ladderResult.IsFailure)
        {
            console.MarkupLine($"[red]{Markup.Escape(ladderResult.Error.Message)}[/]");
            return 1;
        }

        manifestRecords.AddRange(ladderResult.Value);

        using var cropImage = await Image.LoadAsync<Rgba32>(cropSubjectResult.Value.InputPath, cancellationToken);
        var cropRecordsResult = await GenerateCropComparisonAsync(
            cropImage,
            cropSubjectResult.Value,
            processedDirectory,
            canadaPrSpecResult.Value,
            cancellationToken).ConfigureAwait(false);
        if (cropRecordsResult.IsFailure)
        {
            console.MarkupLine($"[red]{Markup.Escape(cropRecordsResult.Error.Message)}[/]");
            return 1;
        }

        manifestRecords.AddRange(cropRecordsResult.Value);

        await DiagnosticsBatchService.WriteManifestAsync(
            Path.Combine(outputRoot, "manifest.json"),
            new
            {
                Corpus = settings.CorpusId,
                Assets = manifestRecords
            },
            cancellationToken).ConfigureAwait(false);

        WriteSummary(manifestRecords);
        return 0;
    }

    private async Task<Result<DocsSampleRecord, PipelineError>> GenerateProfilePreviewAsync(
        string inputPath,
        string subjectName,
        ProfileSpecification profile,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var imageData = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);
        var result = await profileEncoder.ProcessAsync(imageData, profile, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result.Failure<DocsSampleRecord, PipelineError>(result.Error);
        }

        using var preview = DecodeJp2(result.Value.ImageData);
        await preview.SaveAsPngAsync(outputPath, cancellationToken).ConfigureAwait(false);

        return Result.Success<DocsSampleRecord, PipelineError>(ProfileRecord(
            subjectName,
            ToRelativePath(inputPath),
            profile.DisplayName,
            outputPath,
            preview.Width,
            preview.Height,
            result.Value.Encoding.FileSize,
            result.Value.Encoding.CompressionRate,
            result.Value.RotationDegrees,
            result.Value.FaceConfidence));
    }

    private async Task<Result<IReadOnlyList<DocsSampleRecord>, PipelineError>> GenerateRateLadderAsync(
        Image<Rgba32> sourceImage,
        CorpusSubject subject,
        string processedDirectory,
        FaceOFFx.Core.Abstractions.IJpeg2000Encoder encoder,
        CancellationToken cancellationToken)
    {
        var geometryResult = await AnalyzeForProfileAsync(sourceImage, ProfileSpecifications.Piv, cancellationToken)
            .ConfigureAwait(false);
        if (geometryResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<DocsSampleRecord>, PipelineError>(geometryResult.Error);
        }

        var renderResult = documentRenderService.RenderProfile(sourceImage, geometryResult.Value, ProfileSpecifications.Piv);
        if (renderResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<DocsSampleRecord>, PipelineError>(renderResult.Error);
        }

        using var rendered = renderResult.Value.Image;
        var rates = new[] { 0.36f, 0.68f, 0.96f, 1.70f, 4.00f };
        var records = new List<DocsSampleRecord>();
        foreach (var rate in rates)
        {
            var encoding = ProfileSpecifications.Piv.Encoding with { Goal = new ExplicitRateGoal(rate) };
            var encodedResult = EncodingPlanSolver.Encode(rendered, renderResult.Value.RoiSet, encoder, encoding);
            if (encodedResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<DocsSampleRecord>, PipelineError>(encodedResult.Error);
            }

            using var preview = DecodeJp2(encodedResult.Value.ImageData);
            var rateName = rate.ToString("0.00").Replace(".", string.Empty, StringComparison.Ordinal);
            var outputPath = Path.Combine(processedDirectory, $"{NormalizeSubjectName(subject.Id)}_rate_{rateName}.png");
            await preview.SaveAsPngAsync(outputPath, cancellationToken).ConfigureAwait(false);

            records.Add(ProfileRecord(
                NormalizeSubjectName(subject.Id),
                ToRelativePath(subject.InputPath),
                $"PIV {rate:0.00} bpp",
                outputPath,
                preview.Width,
                preview.Height,
                encodedResult.Value.Decision.FileSize,
                encodedResult.Value.Decision.CompressionRate,
                renderResult.Value.Plan.RotationDegrees,
                geometryResult.Value.CoarseDetection.Confidence));
        }

        return Result.Success<IReadOnlyList<DocsSampleRecord>, PipelineError>(records);
    }

    private async Task<Result<IReadOnlyList<DocsSampleRecord>, PipelineError>> GenerateCropComparisonAsync(
        Image<Rgba32> sourceImage,
        CorpusSubject subject,
        string processedDirectory,
        PassportPhotoSpec canadaPrSpec,
        CancellationToken cancellationToken)
    {
        var subjectName = NormalizeSubjectName(subject.Id);
        var records = new List<DocsSampleRecord>();

        foreach (var profile in new[] { ProfileSpecifications.Piv, ProfileSpecifications.Icao })
        {
            var profileResult = await GenerateProfilePreviewAsync(
                subject.InputPath,
                subjectName,
                profile,
                Path.Combine(processedDirectory, $"{subjectName}_{profile.Id}.png"),
                cancellationToken).ConfigureAwait(false);
            if (profileResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<DocsSampleRecord>, PipelineError>(profileResult.Error);
            }

            records.Add(profileResult.Value);
        }

        var renderer = new PassportPhotoRenderService(
            processingServiceFactory,
            NullLogger<PassportPhotoRenderService>.Instance);
        var canadaResult = await renderer.AlignAsync(sourceImage, canadaPrSpec, cancellationToken)
            .ConfigureAwait(false);
        if (canadaResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<DocsSampleRecord>, PipelineError>(canadaResult.Error);
        }

        using var canadaRender = canadaResult.Value;
        using var canadaPreview = ResizeToWidth(canadaRender.Image, ThumbnailWidth);
        var canadaPath = Path.Combine(processedDirectory, $"{subjectName}_canada_pr.png");
        await canadaPreview.SaveAsPngAsync(canadaPath, cancellationToken).ConfigureAwait(false);
        records.Add(new DocsSampleRecord(
            subjectName,
            ToRelativePath(subject.InputPath),
            "Canada PR card",
            ToRelativePath(canadaPath),
            canadaPreview.Width,
            canadaPreview.Height,
            canadaRender.Image.Width,
            canadaRender.Image.Height,
            0,
            0,
            canadaRender.RotationDegrees,
            0));

        return Result.Success<IReadOnlyList<DocsSampleRecord>, PipelineError>(records);
    }

    private async Task<Result<CanonicalFaceGeometry, PipelineError>> AnalyzeForProfileAsync(
        Image<Rgba32> sourceImage,
        ProfileSpecification profile,
        CancellationToken cancellationToken)
    {
        var facesResult = await geometryPipeline.DetectFacesAsync(sourceImage, cancellationToken).ConfigureAwait(false);
        if (facesResult.IsFailure)
        {
            return Result.Failure<CanonicalFaceGeometry, PipelineError>(facesResult.Error);
        }

        var faces = facesResult.Value
            .Where(face => face.Confidence >= profile.FaceSelection.MinimumFaceConfidence)
            .Where(face => face.BoundingBox.Width >= profile.FaceSelection.MinimumFaceSizePixels)
            .Where(face => face.BoundingBox.Height >= profile.FaceSelection.MinimumFaceSizePixels)
            .ToArray();

        if (faces.Length == 0)
        {
            return Result.Failure<CanonicalFaceGeometry, PipelineError>(
                new DetectionError($"No face satisfied the '{profile.DisplayName}' capture requirements.", profile.Id));
        }

        if (profile.FaceSelection.RequireSingleFace && faces.Length != 1)
        {
            return Result.Failure<CanonicalFaceGeometry, PipelineError>(
                new DetectionError(
                    $"Profile '{profile.DisplayName}' requires exactly one usable face, but {faces.Length} faces were detected.",
                    profile.Id));
        }

        return await geometryPipeline.AnalyzeAsync(sourceImage, faces[0], cancellationToken).ConfigureAwait(false);
    }

    private static async Task SaveSourceThumbnailAsync(
        Image<Rgba32> sourceImage,
        string outputPath,
        CancellationToken cancellationToken)
    {
        using var thumbnail = ResizeToWidth(sourceImage, ThumbnailWidth);
        await thumbnail.SaveAsJpegAsync(outputPath, ThumbnailEncoder, cancellationToken).ConfigureAwait(false);
    }

    private static Image<Rgba32> DecodeJp2(byte[] imageData)
    {
        if (!imageSharpRegistered)
        {
            ImageSharpImageCreator.Register();
            imageSharpRegistered = true;
        }

        return J2kImage.FromBytes(imageData).As<Image<Rgba32>>();
    }

    private static Image<Rgba32> ResizeToWidth(Image<Rgba32> image, int width)
    {
        var height = (int)Math.Round(image.Height * (width / (double)image.Width));
        return image.Clone(context => context.Resize(width, height));
    }

    private static DocsSampleRecord ProfileRecord(
        string subject,
        string inputPath,
        string label,
        string outputPath,
        int width,
        int height,
        int bytes,
        float rate,
        float rotation,
        float confidence) =>
        new(
            subject,
            inputPath,
            label,
            ToRelativePath(outputPath),
            width,
            height,
            width,
            height,
            bytes,
            rate,
            rotation,
            confidence);

    private static Result<CorpusSubject, PipelineError> FindSubject(
        IReadOnlyList<CorpusSubject> subjects,
        string subjectId)
    {
        var matches = subjects
            .Where(candidate => string.Equals(candidate.Id, subjectId, StringComparison.OrdinalIgnoreCase))
            .Take(1)
            .ToArray();
        return matches.Length == 1
            ? Result.Success<CorpusSubject, PipelineError>(matches[0])
            : Result.Failure<CorpusSubject, PipelineError>(
                new InputError($"Corpus subject '{subjectId}' was not found.", subjectId));
    }

    private static bool IsReadmeSubject(string subjectId) =>
        string.Equals(subjectId, "bush", StringComparison.OrdinalIgnoreCase)
        || string.Equals(subjectId, "carter", StringComparison.OrdinalIgnoreCase)
        || string.Equals(subjectId, "generic-guy", StringComparison.OrdinalIgnoreCase)
        || string.Equals(subjectId, "johnson", StringComparison.OrdinalIgnoreCase)
        || string.Equals(subjectId, "starmer", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSubjectName(string subjectId) =>
        subjectId.Replace("-", "_", StringComparison.Ordinal);

    private static string ToRelativePath(string outputPath) =>
        Path.GetRelativePath(Directory.GetCurrentDirectory(), outputPath);

    private void WriteSummary(IReadOnlyList<DocsSampleRecord> records)
    {
        var table = new Table()
            .AddColumn("Subject")
            .AddColumn("Asset")
            .AddColumn("Output")
            .AddColumn("Size")
            .AddColumn("Rate");

        foreach (var record in records)
        {
            table.AddRow(
                Markup.Escape(record.Subject),
                Markup.Escape(record.Label),
                $"{record.Width}x{record.Height}",
                record.Bytes > 0 ? $"{record.Bytes:N0} bytes" : "display",
                record.Rate > 0 ? $"{record.Rate:0.00} bpp" : "n/a");
        }

        console.Write(table);
    }
}

internal sealed record DocsSampleRecord(
    string Subject,
    string InputPath,
    string Label,
    string OutputPath,
    int Width,
    int Height,
    int EncodedWidth,
    int EncodedHeight,
    int Bytes,
    float Rate,
    float RotationDegrees,
    float Confidence);

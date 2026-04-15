using System.Text.Json;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Standards;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Extensions;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Executes shipped document issuance jobs and writes both artifacts and provenance.
/// </summary>
public sealed class DocumentJobRunner(
    DocumentRenderService documentRenderService,
    ILogger<DocumentJobRunner> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly DocumentRenderService _documentRenderService = documentRenderService;
    private readonly ILogger<DocumentJobRunner> _logger = logger;

    /// <summary>
    /// Runs a single document workflow and writes its artifacts plus provenance.
    /// </summary>
    public async Task<Result<DocumentJobResult>> RunAsync(
        DocumentJobRequest request,
        CancellationToken cancellationToken = default)
    {
        DocumentDefinition document;
        VariantDefinition variant;

        try
        {
            document = DocumentCatalog.GetDocumentOrThrow(request.DocumentId);
            var variantId = request.VariantId ?? document.PrimaryVariantId;
            if (!document.Variants.TryGetValue(variantId, out variant!))
            {
                return Result.Failure<DocumentJobResult>(
                    $"Unsupported variant '{variantId}' for document '{document.Id}'.");
            }
        }
        catch (Exception ex)
        {
            return Result.Failure<DocumentJobResult>(ex.Message);
        }

        if (!File.Exists(request.InputPath))
        {
            return Result.Failure<DocumentJobResult>($"Input file '{request.InputPath}' was not found.");
        }

        var outputDirectory = request.OutputDirectory
            ?? Path.GetDirectoryName(Path.GetFullPath(request.InputPath))
            ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        var inputBytes = await File.ReadAllBytesAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
        using var sourceImage = Image.Load<Rgba32>(inputBytes);

        Maybe<CanonicalFaceGeometry> canonicalGeometry = Maybe<CanonicalFaceGeometry>.None;
        Result<AutomatedDocumentInputResult> inputCheckResult;

        if (document.WorkflowFamily == DocumentWorkflowFamily.Piv)
        {
            inputCheckResult = await EvaluateInputAsync(document, inputBytes).ConfigureAwait(false);
        }
        else
        {
            var profile = DocumentCatalog.GetInputProfileOrThrow(document.InputProfileId);
            var geometryResult = await _documentRenderService
                .AnalyzeSingleFaceAsync(sourceImage, profile, cancellationToken)
                .ConfigureAwait(false);
            if (geometryResult.IsFailure)
            {
                inputCheckResult = Result.Success(new AutomatedDocumentInputResult(false, geometryResult.Error));
            }
            else
            {
                canonicalGeometry = Maybe<CanonicalFaceGeometry>.From(geometryResult.Value);
                var rollDegrees = Math.Abs(FaceGeometryTransformations.CalculateEyeRotation(
                    geometryResult.Value.SourceLandmarks.LeftEyeCenter,
                    geometryResult.Value.SourceLandmarks.RightEyeCenter));

                inputCheckResult = rollDegrees > profile.MaxRollDegrees
                    ? Result.Success(new AutomatedDocumentInputResult(
                        false,
                        $"{profile.DisplayName} rejected: eye-line rotation {rollDegrees:F1}° exceeds the capture limit of {profile.MaxRollDegrees:F1}°."))
                    : Result.Success(new AutomatedDocumentInputResult(
                        true,
                        $"{profile.DisplayName} accepted: one usable face was detected with a level eye line."));
            }
        }

        if (inputCheckResult.IsFailure)
        {
            return Result.Failure<DocumentJobResult>(inputCheckResult.Error);
        }

        var inputChecks = new[]
        {
            new AutomatedCheckResult(
                document.InputChecks[0].Id,
                document.InputChecks[0].Stage,
                document.InputChecks[0].Disposition,
                document.InputChecks[0].Name,
                inputCheckResult.Value.IsCompliant,
                inputCheckResult.Value.Summary,
                document.InputChecks[0].Citations)
        };

        var manualChecklist = document.ManualChecklist
            .Select(item => new ManualChecklistResult(
                item.Id,
                item.Name,
                item.Description,
                item.Citations))
            .ToArray();

        if (!inputCheckResult.Value.IsCompliant)
        {
            var failedResult = new DocumentJobResult(
                document,
                variant,
                request.InputPath,
                false,
                inputCheckResult.Value.Summary,
                Path.Combine(outputDirectory, BuildProvenanceFileName(request.InputPath, document.Id)),
                inputChecks,
                manualChecklist,
                Array.Empty<DeliverableResult>(),
                DateTimeOffset.UtcNow);

            await WriteProvenanceAsync(failedResult, cancellationToken).ConfigureAwait(false);
            return Result.Success(failedResult);
        }

        var deliverables = new List<DeliverableResult>();

        foreach (var deliverable in variant.Deliverables)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var renderResult = await RenderDeliverableAsync(
                document,
                deliverable,
                request.InputPath,
                inputBytes,
                sourceImage,
                outputDirectory,
                canonicalGeometry).ConfigureAwait(false);
            if (renderResult.IsFailure)
            {
                return Result.Failure<DocumentJobResult>(renderResult.Error);
            }

            deliverables.Add(renderResult.Value);
        }

        var jobResult = new DocumentJobResult(
            document,
            variant,
            request.InputPath,
            true,
            inputCheckResult.Value.Summary,
            Path.Combine(outputDirectory, BuildProvenanceFileName(request.InputPath, document.Id)),
            inputChecks,
            manualChecklist,
            deliverables,
            DateTimeOffset.UtcNow);

        await WriteProvenanceAsync(jobResult, cancellationToken).ConfigureAwait(false);
        return Result.Success(jobResult);
    }

    private async Task<Result<AutomatedDocumentInputResult>> EvaluateInputAsync(DocumentDefinition document, byte[] inputBytes)
    {
        if (document.WorkflowFamily == DocumentWorkflowFamily.Piv)
        {
            var result = await inputBytes.ValidateComplianceAsync(
                "piv",
                logger: _logger,
                mode: AssessmentMode.InputValidation).ConfigureAwait(false);

            return result.IsFailure
                ? Result.Failure<AutomatedDocumentInputResult>($"Input analysis failed: {result.Error}")
                : Result.Success(new AutomatedDocumentInputResult(result.Value.IsCompliant, result.Value.Summary));
        }

        using var image = Image.Load<Rgba32>(inputBytes);
        var profile = DocumentCatalog.GetInputProfileOrThrow(document.InputProfileId);
        var geometryResult = await _documentRenderService
            .AnalyzeSingleFaceAsync(image, profile)
            .ConfigureAwait(false);
        if (geometryResult.IsFailure)
        {
            return Result.Success(new AutomatedDocumentInputResult(false, geometryResult.Error));
        }

        var rollDegrees = Math.Abs(FaceGeometryTransformations.CalculateEyeRotation(
            geometryResult.Value.SourceLandmarks.LeftEyeCenter,
            geometryResult.Value.SourceLandmarks.RightEyeCenter));
        if (rollDegrees > profile.MaxRollDegrees)
        {
            return Result.Success(new AutomatedDocumentInputResult(
                false,
                $"{profile.DisplayName} rejected: eye-line rotation {rollDegrees:F1}° exceeds the capture limit of {profile.MaxRollDegrees:F1}°."));
        }

        return Result.Success(new AutomatedDocumentInputResult(
            true,
            $"{profile.DisplayName} accepted: one usable face was detected with a level eye line."));
    }

    private async Task<Result<DeliverableResult>> RenderDeliverableAsync(
        DocumentDefinition document,
        DeliverableDefinition deliverable,
        string inputPath,
        byte[] inputBytes,
        Image<Rgba32> sourceImage,
        string outputDirectory,
        Maybe<CanonicalFaceGeometry> canonicalGeometry)
    {
        return (document.WorkflowFamily, deliverable.Kind) switch
        {
            (DocumentWorkflowFamily.Piv, DeliverableKind.PivCardImage) => await RenderPivCardAsync(deliverable, inputPath, sourceImage, outputDirectory),
            (DocumentWorkflowFamily.Piv, DeliverableKind.PivPrintedPhoto) => await RenderPivPrintAsync(deliverable, inputPath, sourceImage, outputDirectory),
            (DocumentWorkflowFamily.PassportStyle, DeliverableKind.PaperPhoto) => await RenderPortraitDeliverableAsync(deliverable, inputPath, inputBytes, sourceImage, outputDirectory, canonicalGeometry),
            (DocumentWorkflowFamily.PassportStyle, DeliverableKind.DigitalUploadPhoto) => await RenderPortraitDeliverableAsync(deliverable, inputPath, inputBytes, sourceImage, outputDirectory, canonicalGeometry),
            _ => Result.Failure<DeliverableResult>($"Deliverable kind '{deliverable.Kind}' is not implemented for '{document.WorkflowFamily}'.")
        };
    }

    private async Task<Result<DeliverableResult>> RenderPivCardAsync(
        DeliverableDefinition deliverable,
        string inputPath,
        Image<Rgba32> sourceImage,
        string outputDirectory)
    {
        var profile = DocumentCatalog.GetInputProfileOrThrow("piv-capture");
        var geometryResult = await _documentRenderService
            .AnalyzeSingleFaceAsync(sourceImage, profile)
            .ConfigureAwait(false);
        if (geometryResult.IsFailure)
        {
            return Result.Failure<DeliverableResult>(
                $"Failed to render deliverable '{deliverable.Id}': {geometryResult.Error}");
        }

        var pivResult = _documentRenderService.RenderPivCard(
            sourceImage,
            geometryResult.Value,
            PivProcessingOptions.Default);
        if (pivResult.IsFailure)
        {
            return Result.Failure<DeliverableResult>(
                $"Failed to render deliverable '{deliverable.Id}': {pivResult.Error}");
        }

        var outputPath = BuildOutputPath(inputPath, outputDirectory, deliverable.FileSuffix);
        await File.WriteAllBytesAsync(outputPath, pivResult.Value.ImageData).ConfigureAwait(false);
        var validation = pivResult.Value.Geometry.ComplianceValidation;

        var checks = deliverable.OutputChecks
            .Select(check => new AutomatedCheckResult(
                check.Id,
                check.Stage,
                check.Disposition,
                check.Name,
                validation.IsFullyCompliant,
                validation.IsFullyCompliant
                    ? "Rendered card image satisfies the cited PIV geometry rules."
                    : validation.Summary,
                check.Citations))
            .ToArray();

        return Result.Success(CreateDeliverableResult(
            deliverable,
            outputPath,
            pivResult.Value.ImageData.Length,
            checks,
            null,
            null));
    }

    private async Task<Result<DeliverableResult>> RenderPivPrintAsync(
        DeliverableDefinition deliverable,
        string inputPath,
        Image<Rgba32> sourceImage,
        string outputDirectory)
    {
        var profile = DocumentCatalog.GetInputProfileOrThrow("piv-capture");
        var geometryResult = await _documentRenderService
            .AnalyzeSingleFaceAsync(sourceImage, profile)
            .ConfigureAwait(false);
        if (geometryResult.IsFailure)
        {
            return Result.Failure<DeliverableResult>(
                $"Failed to render deliverable '{deliverable.Id}': {geometryResult.Error}");
        }

        var pivResult = _documentRenderService.RenderPiv(
            sourceImage,
            geometryResult.Value,
            PivProcessingOptions.Default);
        if (pivResult.IsFailure)
        {
            return Result.Failure<DeliverableResult>(
                $"Failed to render deliverable '{deliverable.Id}': {pivResult.Error}");
        }

        var validation = pivResult.Value.ComplianceValidation;
        using var printableImage = pivResult.Value.PivImage.Clone();
        var outputPath = BuildOutputPath(inputPath, outputDirectory, deliverable.FileSuffix);
        var fileBytes = await EncodeJpegAsync(printableImage, 92, 300).ConfigureAwait(false);
        await File.WriteAllBytesAsync(outputPath, fileBytes).ConfigureAwait(false);
        using var reloaded = Image.Load<Rgba32>(fileBytes);
        var dpiPassed = reloaded.Metadata.HorizontalResolution >= 300f
            && reloaded.Metadata.VerticalResolution >= 300f;

        var checks = deliverable.OutputChecks.Select(check =>
        {
            var passed = check.Id switch
            {
                "piv-output-geometry" => validation.IsFullyCompliant,
                "piv-print-dpi" => dpiPassed,
                _ => false
            };

            var summary = check.Id switch
            {
                "piv-output-geometry" => passed
                    ? "Rendered print photo satisfies the cited PIV geometry rules."
                    : validation.Summary,
                "piv-print-dpi" => passed
                    ? $"Rendered print artifact is tagged at {reloaded.Metadata.HorizontalResolution:F0} DPI."
                    : $"Rendered print artifact is tagged below 300 DPI ({reloaded.Metadata.HorizontalResolution:F0} DPI).",
                _ => "Check not implemented."
            };

            return new AutomatedCheckResult(
                check.Id,
                check.Stage,
                check.Disposition,
                check.Name,
                passed,
                summary,
                check.Citations);
        }).ToArray();

        return Result.Success(CreateDeliverableResult(
            deliverable,
            outputPath,
            fileBytes.Length,
            checks,
            null,
            null));
    }

    private async Task<Result<DeliverableResult>> RenderPortraitDeliverableAsync(
        DeliverableDefinition deliverable,
        string inputPath,
        byte[] inputBytes,
        Image<Rgba32> sourceImage,
        string outputDirectory,
        Maybe<CanonicalFaceGeometry> canonicalGeometry)
    {
        if (!deliverable.ProductionDefaults.TryGetValue("spec", out var specId))
        {
            return Result.Failure<DeliverableResult>($"Deliverable '{deliverable.Id}' is missing a portrait spec.");
        }

        var spec = DocumentCatalog.GetPassportPhotoSpecOrThrow(specId);
        if (deliverable.Kind == DeliverableKind.DigitalUploadPhoto && spec.PreserveOriginalFileWhenValid)
        {
            var preserved = await TryPreserveOriginalDigitalAsync(
                deliverable,
                inputPath,
                inputBytes,
                sourceImage,
                outputDirectory,
                spec).ConfigureAwait(false);
            if (preserved is not null)
            {
                return Result.Success(preserved);
            }
        }

        if (canonicalGeometry.HasNoValue)
        {
            return Result.Failure<DeliverableResult>(
                $"Failed to render deliverable '{deliverable.Id}': canonical face geometry was not available.");
        }

        var alignment = _documentRenderService.RenderPassport(
            sourceImage,
            canonicalGeometry.GetValueOrThrow("Canonical face geometry is required."),
            spec);
        if (alignment.IsFailure)
        {
            return Result.Failure<DeliverableResult>(
                $"Failed to render deliverable '{deliverable.Id}': {alignment.Error}");
        }

        using var alignedPortrait = alignment.Value.Image;
        var outputPath = BuildOutputPath(inputPath, outputDirectory, deliverable.FileSuffix);
        var quality = deliverable.Kind == DeliverableKind.DigitalUploadPhoto ? 92 : 95;
        var fileBytes = await EncodeJpegAsync(alignedPortrait, quality, spec.Dpi).ConfigureAwait(false);
        if (spec.MaxFileSizeBytes.HasValue)
        {
            fileBytes = await ShrinkJpegToMaxBytesAsync(alignedPortrait, spec.MaxFileSizeBytes.Value, spec.Dpi).ConfigureAwait(false);
        }

        await File.WriteAllBytesAsync(outputPath, fileBytes).ConfigureAwait(false);
        using var reloaded = Image.Load<Rgba32>(fileBytes);

        var checks = BuildPortraitChecks(
            deliverable,
            spec,
            outputPath,
            fileBytes.Length,
            reloaded.Width,
            reloaded.Height,
            MeasurePortraitComposition(alignment.Value.Landmarks, spec.TargetHeight),
            originalFileRequirementSatisfied: false);

        var supportingInfoPath = spec.RequiresSupportingInfoSidecar
            ? await WriteSupportingInfoFileAsync(outputPath, sourceImage).ConfigureAwait(false)
            : null;

        return Result.Success(CreateDeliverableResult(
            deliverable,
            outputPath,
            fileBytes.Length,
            checks,
            supportingInfoPath,
            spec.RequiresSupportingInfoSidecar ? false : null));
    }

    private async Task<DeliverableResult?> TryPreserveOriginalDigitalAsync(
        DeliverableDefinition deliverable,
        string inputPath,
        byte[] inputBytes,
        Image<Rgba32> sourceImage,
        string outputDirectory,
        PassportPhotoSpec spec)
    {
        var extension = Path.GetExtension(inputPath);
        if (!string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var landmarks = await DetectLandmarksAsync(sourceImage).ConfigureAwait(false);
        if (landmarks.IsFailure)
        {
            return null;
        }

        var outputPath = BuildOutputPath(inputPath, outputDirectory, deliverable.FileSuffix);
        var measurements = MeasurePortraitComposition(landmarks.Value, sourceImage.Height);
        var checks = BuildPortraitChecks(
            deliverable,
            spec,
            outputPath,
            inputBytes.Length,
            sourceImage.Width,
            sourceImage.Height,
            measurements,
            originalFileRequirementSatisfied: true);

        var blockingPassed = checks
            .Where(check => check.Disposition == DocumentCheckDisposition.Blocking)
            .All(check => check.Passed);
        if (!blockingPassed)
        {
            return null;
        }

        await File.WriteAllBytesAsync(outputPath, inputBytes).ConfigureAwait(false);
        var supportingInfoPath = spec.RequiresSupportingInfoSidecar
            ? await WriteSupportingInfoFileAsync(outputPath, sourceImage).ConfigureAwait(false)
            : null;

        return CreateDeliverableResult(
            deliverable,
            outputPath,
            inputBytes.Length,
            checks,
            supportingInfoPath,
            true);
    }

    private IReadOnlyList<AutomatedCheckResult> BuildPortraitChecks(
        DeliverableDefinition deliverable,
        PassportPhotoSpec spec,
        string outputPath,
        int fileSizeBytes,
        int width,
        int height,
        PortraitMeasurements measurements,
        bool originalFileRequirementSatisfied)
    {
        return deliverable.OutputChecks.Select(check =>
        {
            var passed = check.Id switch
            {
                "us-print-composition" or "canada-print-composition" =>
                    measurements.HeadHeightRatio >= spec.MinHeadHeightRatio
                    && measurements.HeadHeightRatio <= spec.MaxHeadHeightRatio
                    && measurements.EyeFromBottomRatio >= spec.MinEyeFromBottomRatio
                    && measurements.EyeFromBottomRatio <= spec.MaxEyeFromBottomRatio,
                "us-digital-technical" or "canada-pr-digital-technical" or "canada-citizenship-digital-technical" =>
                    width >= spec.MinWidth
                    && width <= spec.MaxWidth
                    && height >= spec.MinHeight
                    && height <= spec.MaxHeight
                    && (!spec.RequireSquare || width == height)
                    && (!spec.MaxFileSizeBytes.HasValue || fileSizeBytes <= spec.MaxFileSizeBytes.Value)
                    && string.Equals(Path.GetExtension(outputPath), ".jpeg", StringComparison.OrdinalIgnoreCase),
                "canada-digital-originality" => originalFileRequirementSatisfied,
                _ => false
            };

            var summary = check.Id switch
            {
                "us-print-composition" => passed
                    ? $"Rendered print photo keeps head height at {measurements.HeadHeightRatio:P0} and eye line at {measurements.EyeFromBottomRatio:P0} from the bottom."
                    : $"Rendered print photo is outside the cited composition range (head {measurements.HeadHeightRatio:P0}, eyes {measurements.EyeFromBottomRatio:P0} from bottom).",
                "canada-print-composition" => passed
                    ? $"Rendered print photo keeps chin-to-crown height at {measurements.HeadHeightRatio:P0} and eye line at {measurements.EyeFromBottomRatio:P0} from the bottom."
                    : $"Rendered print photo is outside the cited composition range (head {measurements.HeadHeightRatio:P0}, eyes {measurements.EyeFromBottomRatio:P0} from bottom).",
                "us-digital-technical" => passed
                    ? $"Rendered digital file is {width}x{height} and {fileSizeBytes:N0} bytes."
                    : $"Rendered digital file does not meet the cited U.S. size requirements ({width}x{height}, {fileSizeBytes:N0} bytes).",
                "canada-pr-digital-technical" => passed
                    ? $"Rendered digital file is {width}x{height} and {fileSizeBytes:N0} bytes."
                    : $"Rendered digital file does not meet the cited Canada permanent resident size requirements ({width}x{height}, {fileSizeBytes:N0} bytes).",
                "canada-citizenship-digital-technical" => passed
                    ? $"Rendered digital file is {width}x{height} and {fileSizeBytes:N0} bytes."
                    : $"Rendered digital file does not meet the cited Canada citizenship size requirements ({width}x{height}, {fileSizeBytes:N0} bytes).",
                "canada-digital-originality" => passed
                    ? "The digital output could be preserved as the unchanged original JPEG file."
                    : "The digital output required transformation, so the unchanged-original file requirement remains advisory.",
                _ => "Check not implemented."
            };

            return new AutomatedCheckResult(
                check.Id,
                check.Stage,
                check.Disposition,
                check.Name,
                passed,
                summary,
                check.Citations);
        }).ToArray();
    }

    private async Task<Result<FaceLandmarks68>> DetectLandmarksAsync(Image<Rgba32> image)
    {
        var profile = DocumentCatalog.GetInputProfileOrThrow("us-portrait-capture");
        var geometryResult = await _documentRenderService
            .AnalyzeSingleFaceAsync(image, profile)
            .ConfigureAwait(false);
        return geometryResult.IsFailure
            ? Result.Failure<FaceLandmarks68>(geometryResult.Error)
            : Result.Success(geometryResult.Value.SourceLandmarks);
    }

    private static PortraitMeasurements MeasurePortraitComposition(FaceLandmarks68 landmarks, int imageHeight)
    {
        var browY = landmarks.Points.Take(17).Min(point => point.Y);
        var chinY = landmarks.Points[8].Y;
        var faceHeight = chinY - browY;
        var estimatedHeadTop = browY - (faceHeight * 0.3f);
        var headHeightRatio = (chinY - estimatedHeadTop) / imageHeight;
        var eyeCenterY = (landmarks.LeftEyeCenter.Y + landmarks.RightEyeCenter.Y) / 2f;
        var eyeFromBottomRatio = (imageHeight - eyeCenterY) / imageHeight;
        return new PortraitMeasurements(headHeightRatio, eyeFromBottomRatio);
    }

    private static async Task<byte[]> EncodeJpegAsync(Image<Rgba32> image, int quality, int? dpi)
    {
        if (dpi.HasValue)
        {
            image.Metadata.HorizontalResolution = dpi.Value;
            image.Metadata.VerticalResolution = dpi.Value;
            image.Metadata.ResolutionUnits = PixelResolutionUnit.PixelsPerInch;
        }

        await using var stream = new MemoryStream();
        await image.SaveAsJpegAsync(stream, new JpegEncoder { Quality = quality }).ConfigureAwait(false);
        return stream.ToArray();
    }

    private static async Task<byte[]> ShrinkJpegToMaxBytesAsync(Image<Rgba32> image, int maxBytes, int? dpi)
    {
        for (var quality = 95; quality >= 40; quality -= 5)
        {
            var encoded = await EncodeJpegAsync(image, quality, dpi).ConfigureAwait(false);
            if (encoded.Length <= maxBytes)
            {
                return encoded;
            }
        }

        return await EncodeJpegAsync(image, 40, dpi).ConfigureAwait(false);
    }

    private static async Task<string> WriteSupportingInfoFileAsync(string outputPath, Image<Rgba32> sourceImage)
    {
        var supportingInfoPath = $"{Path.ChangeExtension(outputPath, null)}.supporting-info.txt";
        var dateTaken = TryGetPhotoTakenDate(sourceImage) ?? "[enter date photo was taken]";

        var contents = string.Join(Environment.NewLine, new[]
        {
            "Subject first name: ",
            "Subject last name: ",
            "Photographer or studio name: ",
            "Photographer or studio address: ",
            $"Date photo taken: {dateTaken}"
        });

        await File.WriteAllTextAsync(supportingInfoPath, contents).ConfigureAwait(false);
        return supportingInfoPath;
    }

    private static string? TryGetPhotoTakenDate(Image<Rgba32> image)
    {
        return null;
    }

    private static DeliverableResult CreateDeliverableResult(
        DeliverableDefinition deliverable,
        string outputPath,
        int fileSizeBytes,
        IReadOnlyList<AutomatedCheckResult> checks,
        string? supportingInfoPath,
        bool? originalFileRequirementSatisfied)
    {
        var blockingFailures = checks
            .Where(check => check.Disposition == DocumentCheckDisposition.Blocking && !check.Passed)
            .ToArray();
        var advisoryFailures = checks
            .Where(check => check.Disposition == DocumentCheckDisposition.Advisory && !check.Passed)
            .ToArray();

        var passed = blockingFailures.Length == 0;
        var summary = passed
            ? advisoryFailures.Length == 0
                ? $"{deliverable.DisplayName} produced and validated."
                : $"{deliverable.DisplayName} produced and validated with advisory notes."
            : $"{deliverable.DisplayName} produced but failed one or more blocking output checks.";

        return new DeliverableResult(
            deliverable.Id,
            deliverable.DisplayName,
            outputPath,
            passed,
            summary,
            fileSizeBytes,
            supportingInfoPath,
            originalFileRequirementSatisfied,
            deliverable.ProductionDefaults,
            checks);
    }

    private static string BuildOutputPath(string inputPath, string outputDirectory, string suffix) =>
        Path.Combine(outputDirectory, $"{Path.GetFileNameWithoutExtension(inputPath)}{suffix}");

    private static string BuildProvenanceFileName(string inputPath, string documentId) =>
        $"{Path.GetFileNameWithoutExtension(inputPath)}.{documentId}.provenance.json";

    private static async Task WriteProvenanceAsync(DocumentJobResult jobResult, CancellationToken cancellationToken)
    {
        var provenance = new
        {
            DocumentId = jobResult.Document.Id,
            DocumentDisplayName = jobResult.Document.DisplayName,
            VariantId = jobResult.Variant.Id,
            VariantDisplayName = jobResult.Variant.DisplayName,
            jobResult.InputPath,
            jobResult.InputPassed,
            jobResult.InputSummary,
            GeneratedAtUtc = jobResult.GeneratedAtUtc,
            DocumentCitations = jobResult.Document.Citations,
            InputChecks = jobResult.InputChecks,
            ManualChecklist = jobResult.ManualChecklist,
            Deliverables = jobResult.Deliverables
        };

        var json = JsonSerializer.Serialize(provenance, JsonOptions);
        await File.WriteAllTextAsync(jobResult.ProvenancePath, json, cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct PortraitMeasurements(float HeadHeightRatio, float EyeFromBottomRatio);

    private readonly record struct AutomatedDocumentInputResult(bool IsCompliant, string Summary);
}

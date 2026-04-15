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
    /// <summary>
    /// Runs a single document workflow and writes its artifacts plus provenance.
    /// </summary>
    public async Task<Result<DocumentJobResult, PipelineError>> RunAsync(
        DocumentJobRequest request,
        CancellationToken cancellationToken = default)
    {
        var requestContextResult = ResolveRequest(request);
        if (requestContextResult.IsFailure)
        {
            return Result.Failure<DocumentJobResult, PipelineError>(requestContextResult.Error);
        }

        var requestContext = requestContextResult.Value;

        var inputResult = await LoadInputAsync(request, requestContext.OutputDirectory, cancellationToken).ConfigureAwait(false);
        if (inputResult.IsFailure)
        {
            return Result.Failure<DocumentJobResult, PipelineError>(inputResult.Error);
        }

        using var sourceImage = inputResult.Value.SourceImage;

        var inputCheckResult = await EvaluateInputDetailedAsync(
            requestContext.Document,
            inputResult.Value.InputBytes,
            sourceImage,
            cancellationToken).ConfigureAwait(false);
        if (inputCheckResult.IsFailure)
        {
            return Result.Failure<DocumentJobResult, PipelineError>(inputCheckResult.Error);
        }

        var inputChecks = new[]
        {
            new AutomatedCheckResult(
                requestContext.Document.InputChecks[0].Id,
                requestContext.Document.InputChecks[0].Stage,
                requestContext.Document.InputChecks[0].Disposition,
                requestContext.Document.InputChecks[0].Name,
                inputCheckResult.Value.IsAccepted,
                inputCheckResult.Value.Summary,
                requestContext.Document.InputChecks[0].Citations)
        };

        var manualChecklist = requestContext.Document.ManualChecklist
            .Select(item => new ManualChecklistResult(
                item.Id,
                item.Name,
                item.Description,
                item.Citations))
            .ToArray();

        if (inputCheckResult.Value is RejectedInputState rejected)
        {
            var failedResult = new DocumentJobResult(
                requestContext.Document,
                requestContext.Variant,
                request.InputPath,
                false,
                rejected.Summary,
                Path.Combine(requestContext.OutputDirectory, BuildProvenanceFileName(request.InputPath, requestContext.Document.Id)),
                inputChecks,
                manualChecklist,
                Array.Empty<DeliverableResult>(),
                DateTimeOffset.UtcNow);

            var provenanceResult = await WriteProvenanceDetailedAsync(failedResult, cancellationToken).ConfigureAwait(false);
            return provenanceResult.IsFailure
                ? Result.Failure<DocumentJobResult, PipelineError>(provenanceResult.Error)
                : Result.Success<DocumentJobResult, PipelineError>(failedResult);
        }

        var deliverables = new List<DeliverableResult>();

        foreach (var deliverable in requestContext.Variant.Deliverables)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var renderResult = await RenderDeliverableAsync(
                requestContext.Document,
                deliverable,
                request.InputPath,
                inputResult.Value.InputBytes,
                sourceImage,
                requestContext.OutputDirectory,
                inputCheckResult.Value).ConfigureAwait(false);
            if (renderResult.IsFailure)
            {
                return Result.Failure<DocumentJobResult, PipelineError>(renderResult.Error);
            }

            deliverables.Add(renderResult.Value);
        }

        var jobResult = new DocumentJobResult(
            requestContext.Document,
            requestContext.Variant,
            request.InputPath,
            true,
            inputCheckResult.Value.Summary,
            Path.Combine(requestContext.OutputDirectory, BuildProvenanceFileName(request.InputPath, requestContext.Document.Id)),
            inputChecks,
            manualChecklist,
            deliverables,
            DateTimeOffset.UtcNow);

        var writeResult = await WriteProvenanceDetailedAsync(jobResult, cancellationToken).ConfigureAwait(false);
        return writeResult.IsFailure
            ? Result.Failure<DocumentJobResult, PipelineError>(writeResult.Error)
            : Result.Success<DocumentJobResult, PipelineError>(jobResult);
    }

    private static Result<RequestContext, PipelineError> ResolveRequest(DocumentJobRequest request)
    {
        var documentResult = DocumentCatalog.GetDocument(request.DocumentId);
        if (documentResult.IsFailure)
        {
            return Result.Failure<RequestContext, PipelineError>(documentResult.Error);
        }

        var document = documentResult.Value;
        var variantId = request.VariantId ?? document.PrimaryVariantId;
        if (!document.Variants.TryGetValue(variantId, out var variant))
        {
            return Result.Failure<RequestContext, PipelineError>(
                new ConfigurationError($"Unsupported variant '{variantId}' for document '{document.Id}'."));
        }

        var outputDirectory = request.OutputDirectory
            ?? Path.GetDirectoryName(Path.GetFullPath(request.InputPath))
            ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outputDirectory);

        return Result.Success<RequestContext, PipelineError>(new RequestContext(document, variant, outputDirectory));
    }

    private static async Task<Result<LoadedInput, PipelineError>> LoadInputAsync(
        DocumentJobRequest request,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.InputPath))
        {
            return Result.Failure<LoadedInput, PipelineError>(
                new InputError($"Input file '{request.InputPath}' was not found."));
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);
            var inputBytes = await File.ReadAllBytesAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
            return Result.Success<LoadedInput, PipelineError>(new LoadedInput(inputBytes, Image.Load<Rgba32>(inputBytes)));
        }
        catch (Exception ex)
        {
            return Result.Failure<LoadedInput, PipelineError>(
                new PersistenceError(ex.Message, request.InputPath));
        }
    }

    private async Task<Result<InputEvaluationState, PipelineError>> EvaluateInputDetailedAsync(
        DocumentDefinition document,
        byte[] inputBytes,
        Image<Rgba32> sourceImage,
        CancellationToken cancellationToken)
    {
        if (document.WorkflowFamily == DocumentWorkflowFamily.Piv)
        {
            var result = await inputBytes.ValidateComplianceAsync(
                    "piv",
                    logger: _logger,
                    mode: AssessmentMode.InputValidation)
                .ConfigureAwait(false);

            return result.IsFailure
                ? Result.Failure<InputEvaluationState, PipelineError>(new ValidationError($"Input analysis failed: {result.Error}", document.Id))
                : result.Value.IsCompliant
                    ? Result.Success<InputEvaluationState, PipelineError>(new AcceptedWithoutGeometry(result.Value.Summary))
                    : Result.Success<InputEvaluationState, PipelineError>(new RejectedInputState(result.Value.Summary));
        }

        var profileResult = DocumentCatalog.GetInputProfile(document.InputProfileId);
        if (profileResult.IsFailure)
        {
            return Result.Failure<InputEvaluationState, PipelineError>(profileResult.Error);
        }

        var profile = profileResult.Value;
        var geometryResult = await _documentRenderService
            .AnalyzeSingleFaceAsync(sourceImage, profile, cancellationToken)
            .ConfigureAwait(false);
        if (geometryResult.IsFailure)
        {
            return Result.Success<InputEvaluationState, PipelineError>(new RejectedInputState(geometryResult.Error.Message));
        }

        var rollDegrees = Math.Abs(FaceGeometryTransformations.CalculateEyeRotation(
            geometryResult.Value.SourceLandmarks.LeftEyeCenter,
            geometryResult.Value.SourceLandmarks.RightEyeCenter));
        return rollDegrees > profile.MaxRollDegrees
            ? Result.Success<InputEvaluationState, PipelineError>(new RejectedInputState(
                $"{profile.DisplayName} rejected: eye-line rotation {rollDegrees:F1}° exceeds the capture limit of {profile.MaxRollDegrees:F1}°."))
            : Result.Success<InputEvaluationState, PipelineError>(new AcceptedWithGeometry(
                geometryResult.Value,
                $"{profile.DisplayName} accepted: one usable face was detected with a level eye line."));
    }

    private async Task<Result<DeliverableResult, PipelineError>> RenderDeliverableAsync(
        DocumentDefinition document,
        DeliverableDefinition deliverable,
        string inputPath,
        byte[] inputBytes,
        Image<Rgba32> sourceImage,
        string outputDirectory,
        InputEvaluationState inputState)
    {
        return (document.WorkflowFamily, deliverable.Kind) switch
        {
            (DocumentWorkflowFamily.Piv, DeliverableKind.PivCardImage) => await RenderPivCardAsync(deliverable, inputPath, sourceImage, outputDirectory),
            (DocumentWorkflowFamily.Piv, DeliverableKind.PivPrintedPhoto) => await RenderPivPrintAsync(deliverable, inputPath, sourceImage, outputDirectory),
            (DocumentWorkflowFamily.PassportStyle, DeliverableKind.PaperPhoto) => await RenderPortraitDeliverableAsync(deliverable, inputPath, inputBytes, sourceImage, outputDirectory, inputState),
            (DocumentWorkflowFamily.PassportStyle, DeliverableKind.DigitalUploadPhoto) => await RenderPortraitDeliverableAsync(deliverable, inputPath, inputBytes, sourceImage, outputDirectory, inputState),
            _ => Result.Failure<DeliverableResult, PipelineError>(new RenderError($"Deliverable kind '{deliverable.Kind}' is not implemented for '{document.WorkflowFamily}'.", deliverable.Id))
        };
    }

    private async Task<Result<DeliverableResult, PipelineError>> RenderPivCardAsync(
        DeliverableDefinition deliverable,
        string inputPath,
        Image<Rgba32> sourceImage,
        string outputDirectory)
    {
        var profileResult = DocumentCatalog.GetInputProfile("piv-capture");
        if (profileResult.IsFailure)
        {
            return Result.Failure<DeliverableResult, PipelineError>(profileResult.Error);
        }

        var profile = profileResult.Value;
        var geometryResult = await _documentRenderService
            .AnalyzeSingleFaceAsync(sourceImage, profile)
            .ConfigureAwait(false);
        if (geometryResult.IsFailure)
        {
            return Result.Failure<DeliverableResult, PipelineError>(
                new RenderError($"Failed to render deliverable '{deliverable.Id}': {geometryResult.Error.Message}", deliverable.Id));
        }

        var pivResult = _documentRenderService.RenderPivCard(
            sourceImage,
            geometryResult.Value,
            PivProcessingOptions.Default);
        if (pivResult.IsFailure)
        {
            return Result.Failure<DeliverableResult, PipelineError>(
                new RenderError($"Failed to render deliverable '{deliverable.Id}': {pivResult.Error.Message}", deliverable.Id));
        }

        var outputPath = BuildOutputPath(inputPath, outputDirectory, deliverable.FileSuffix);
        try
        {
            await File.WriteAllBytesAsync(outputPath, pivResult.Value.ImageData).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure<DeliverableResult, PipelineError>(new PersistenceError(ex.Message, outputPath));
        }

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

        return Result.Success<DeliverableResult, PipelineError>(CreateDeliverableResult(
            deliverable,
            outputPath,
            pivResult.Value.ImageData.Length,
            checks,
            null,
            null));
    }

    private async Task<Result<DeliverableResult, PipelineError>> RenderPivPrintAsync(
        DeliverableDefinition deliverable,
        string inputPath,
        Image<Rgba32> sourceImage,
        string outputDirectory)
    {
        var profileResult = DocumentCatalog.GetInputProfile("piv-capture");
        if (profileResult.IsFailure)
        {
            return Result.Failure<DeliverableResult, PipelineError>(profileResult.Error);
        }

        var profile = profileResult.Value;
        var geometryResult = await _documentRenderService
            .AnalyzeSingleFaceAsync(sourceImage, profile)
            .ConfigureAwait(false);
        if (geometryResult.IsFailure)
        {
            return Result.Failure<DeliverableResult, PipelineError>(
                new RenderError($"Failed to render deliverable '{deliverable.Id}': {geometryResult.Error.Message}", deliverable.Id));
        }

        var pivResult = _documentRenderService.RenderPiv(
            sourceImage,
            geometryResult.Value,
            PivProcessingOptions.Default);
        if (pivResult.IsFailure)
        {
            return Result.Failure<DeliverableResult, PipelineError>(
                new RenderError($"Failed to render deliverable '{deliverable.Id}': {pivResult.Error.Message}", deliverable.Id));
        }

        var validation = pivResult.Value.ComplianceValidation;
        using var printableImage = pivResult.Value.PivImage.Clone();
        var outputPath = BuildOutputPath(inputPath, outputDirectory, deliverable.FileSuffix);
        var fileBytes = await EncodeJpegAsync(printableImage, 92, 300).ConfigureAwait(false);
        try
        {
            await File.WriteAllBytesAsync(outputPath, fileBytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure<DeliverableResult, PipelineError>(new PersistenceError(ex.Message, outputPath));
        }

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

        return Result.Success<DeliverableResult, PipelineError>(CreateDeliverableResult(
            deliverable,
            outputPath,
            fileBytes.Length,
            checks,
            null,
            null));
    }

    private async Task<Result<DeliverableResult, PipelineError>> RenderPortraitDeliverableAsync(
        DeliverableDefinition deliverable,
        string inputPath,
        byte[] inputBytes,
        Image<Rgba32> sourceImage,
        string outputDirectory,
        InputEvaluationState inputState)
    {
        if (!deliverable.ProductionDefaults.TryGetValue("spec", out var specId))
        {
            return Result.Failure<DeliverableResult, PipelineError>(
                new ConfigurationError($"Deliverable '{deliverable.Id}' is missing a portrait spec.", deliverable.Id));
        }

        var specResult = DocumentCatalog.GetPassportPhotoSpec(specId);
        if (specResult.IsFailure)
        {
            return Result.Failure<DeliverableResult, PipelineError>(specResult.Error);
        }

        var spec = specResult.Value;
        if (deliverable.Kind == DeliverableKind.DigitalUploadPhoto && spec.PreserveOriginalFileWhenValid)
        {
            var preserved = await TryPreserveOriginalDigitalAsync(
                deliverable,
                inputPath,
                inputBytes,
                sourceImage,
                outputDirectory,
                spec).ConfigureAwait(false);
            if (preserved.HasValue)
            {
                return Result.Success<DeliverableResult, PipelineError>(preserved.Value);
            }
        }

        if (inputState is not AcceptedWithGeometry accepted)
        {
            return Result.Failure<DeliverableResult, PipelineError>(
                new RenderError($"Failed to render deliverable '{deliverable.Id}': canonical face geometry was not available.", deliverable.Id));
        }

        var alignment = _documentRenderService.RenderPassport(
            sourceImage,
            accepted.Geometry,
            spec);
        if (alignment.IsFailure)
        {
            return Result.Failure<DeliverableResult, PipelineError>(
                new RenderError($"Failed to render deliverable '{deliverable.Id}': {alignment.Error.Message}", deliverable.Id));
        }

        using var alignedPortrait = alignment.Value.Image;
        var outputPath = BuildOutputPath(inputPath, outputDirectory, deliverable.FileSuffix);
        var quality = deliverable.Kind == DeliverableKind.DigitalUploadPhoto ? 92 : 95;
        var fileBytes = await EncodeJpegAsync(alignedPortrait, quality, spec.Dpi).ConfigureAwait(false);
        if (spec.MaxFileSizeBytes.HasValue)
        {
            fileBytes = await ShrinkJpegToMaxBytesAsync(alignedPortrait, spec.MaxFileSizeBytes.Value, spec.Dpi).ConfigureAwait(false);
        }

        try
        {
            await File.WriteAllBytesAsync(outputPath, fileBytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Result.Failure<DeliverableResult, PipelineError>(new PersistenceError(ex.Message, outputPath));
        }

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

        return Result.Success<DeliverableResult, PipelineError>(CreateDeliverableResult(
            deliverable,
            outputPath,
            fileBytes.Length,
            checks,
            supportingInfoPath,
            spec.RequiresSupportingInfoSidecar ? false : null));
    }

    private async Task<Maybe<DeliverableResult>> TryPreserveOriginalDigitalAsync(
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
            return Maybe<DeliverableResult>.None;
        }

        var landmarks = await DetectLandmarksAsync(sourceImage).ConfigureAwait(false);
        if (landmarks.IsFailure)
        {
            return Maybe<DeliverableResult>.None;
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
            return Maybe<DeliverableResult>.None;
        }

        await File.WriteAllBytesAsync(outputPath, inputBytes).ConfigureAwait(false);
        var supportingInfoPath = spec.RequiresSupportingInfoSidecar
            ? await WriteSupportingInfoFileAsync(outputPath, sourceImage).ConfigureAwait(false)
            : null;

        return Maybe<DeliverableResult>.From(CreateDeliverableResult(
            deliverable,
            outputPath,
            inputBytes.Length,
            checks,
            supportingInfoPath,
            true));
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

    private async Task<Result<FaceLandmarks68, PipelineError>> DetectLandmarksAsync(Image<Rgba32> image)
    {
        var profileResult = DocumentCatalog.GetInputProfile("us-portrait-capture");
        if (profileResult.IsFailure)
        {
            return Result.Failure<FaceLandmarks68, PipelineError>(profileResult.Error);
        }

        var profile = profileResult.Value;
        var geometryResult = await _documentRenderService
            .AnalyzeSingleFaceAsync(image, profile)
            .ConfigureAwait(false);
        return geometryResult.IsFailure
            ? Result.Failure<FaceLandmarks68, PipelineError>(geometryResult.Error)
            : Result.Success<FaceLandmarks68, PipelineError>(geometryResult.Value.SourceLandmarks);
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
        var dateTaken = TryGetPhotoTakenDate(sourceImage).GetValueOrDefault("[enter date photo was taken]");

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

    private static Maybe<string> TryGetPhotoTakenDate(Image<Rgba32> image)
    {
        return Maybe<string>.None;
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

    private static async Task<UnitResult<PipelineError>> WriteProvenanceDetailedAsync(DocumentJobResult jobResult, CancellationToken cancellationToken)
    {
        try
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
            return UnitResult.Success<PipelineError>();
        }
        catch (Exception ex)
        {
            return UnitResult.Failure<PipelineError>(new PersistenceError(ex.Message, jobResult.ProvenancePath));
        }
    }

    private readonly record struct PortraitMeasurements(float HeadHeightRatio, float EyeFromBottomRatio);

    private sealed record RequestContext(DocumentDefinition Document, VariantDefinition Variant, string OutputDirectory);
    private sealed record LoadedInput(byte[] InputBytes, Image<Rgba32> SourceImage);

    private abstract record InputEvaluationState(string Summary)
    {
        public abstract bool IsAccepted { get; }
    }

    private sealed record RejectedInputState(string Summary) : InputEvaluationState(Summary)
    {
        public override bool IsAccepted => false;
    }

    private sealed record AcceptedWithoutGeometry(string Summary) : InputEvaluationState(Summary)
    {
        public override bool IsAccepted => true;
    }

    private sealed record AcceptedWithGeometry(CanonicalFaceGeometry Geometry, string Summary) : InputEvaluationState(Summary)
    {
        public override bool IsAccepted => true;
    }
}

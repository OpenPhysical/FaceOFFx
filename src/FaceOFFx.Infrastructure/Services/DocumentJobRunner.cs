using System.Text.Json;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Quality.Assessors;
using FaceOFFx.Core.Domain.Standards;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Extensions;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Executes shipped document issuance jobs and writes both artifacts and provenance.
/// </summary>
public sealed class DocumentJobRunner(
    IFaceDetector faceDetector,
    ILandmarkExtractor landmarkExtractor,
    IJpeg2000Encoder jpeg2000Encoder,
    ILogger<DocumentJobRunner> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IFaceDetector _faceDetector = faceDetector;
    private readonly ILandmarkExtractor _landmarkExtractor = landmarkExtractor;
    private readonly IJpeg2000Encoder _jpeg2000Encoder = jpeg2000Encoder;
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

        var inputBytes = await File.ReadAllBytesAsync(request.InputPath, cancellationToken);
        var inputCheckResult = await EvaluateInputAsync(document, inputBytes).ConfigureAwait(false);
        if (inputCheckResult.IsFailure)
        {
            return Result.Failure<DocumentJobResult>(inputCheckResult.Error);
        }

        var inputChecks = new[]
        {
            new AutomatedCheckResult(
                document.InputChecks[0].Id,
                document.InputChecks[0].Stage,
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

        using var sourceImage = Image.Load<Rgba32>(inputBytes);
        var deliverables = new List<DeliverableResult>();

        foreach (var deliverable in variant.Deliverables)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var renderResult = await RenderDeliverableAsync(
                document,
                deliverable,
                request.InputPath,
                sourceImage,
                outputDirectory,
                cancellationToken).ConfigureAwait(false);
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

    private async Task<Result<ComplianceAssessment>> EvaluateInputAsync(DocumentDefinition document, byte[] inputBytes)
    {
        var standardName = document.Id.Equals("piv", StringComparison.OrdinalIgnoreCase) ? "piv" : "icao";
        var result = await inputBytes.ValidateComplianceAsync(
            standardName,
            logger: _logger,
            mode: AssessmentMode.InputValidation).ConfigureAwait(false);

        return result.IsFailure
            ? Result.Failure<ComplianceAssessment>($"Input analysis failed: {result.Error}")
            : Result.Success(result.Value);
    }

    private async Task<Result<DeliverableResult>> RenderDeliverableAsync(
        DocumentDefinition document,
        DeliverableDefinition deliverable,
        string inputPath,
        Image<Rgba32> sourceImage,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        return deliverable.RendererKey switch
        {
            "piv-card" => await RenderPivCardAsync(document, deliverable, inputPath, sourceImage, outputDirectory),
            "piv-print" => await RenderPivPrintAsync(document, deliverable, inputPath, sourceImage, outputDirectory),
            "us-paper" => await RenderPassportStyleJpegAsync(document, deliverable, inputPath, sourceImage, outputDirectory, 1200, 1200, 0.60f, 0.625f, 600, 0.492f, 0.689f, 0.551f, 0.689f, digitalTechnicalCheck: false),
            "us-digital" => await RenderPassportStyleJpegAsync(document, deliverable, inputPath, sourceImage, outputDirectory, 600, 600, 0.60f, 0.625f, null, 0.50f, 0.69f, 0.56f, 0.69f, digitalTechnicalCheck: true),
            "canada-paper" => await RenderPassportStyleJpegAsync(document, deliverable, inputPath, sourceImage, outputDirectory, 1181, 1654, 0.48f, 0.62f, 600, 31f / 70f, 36f / 70f, 0.55f, 0.72f, digitalTechnicalCheck: false),
            "canada-digital" => await RenderPassportStyleJpegAsync(document, deliverable, inputPath, sourceImage, outputDirectory, 1200, 1800, 0.48f, 0.62f, null, 31f / 70f, 36f / 70f, 0.55f, 0.72f, digitalTechnicalCheck: true),
            _ => Result.Failure<DeliverableResult>($"Renderer '{deliverable.RendererKey}' is not implemented.")
        };
    }

    private async Task<Result<DeliverableResult>> RenderPivCardAsync(
        DocumentDefinition document,
        DeliverableDefinition deliverable,
        string inputPath,
        Image<Rgba32> sourceImage,
        string outputDirectory)
    {
        using var pivSource = sourceImage.Clone();
        var pivResult = await PivProcessor.ProcessAsync(
            pivSource,
            _faceDetector,
            _landmarkExtractor,
            _jpeg2000Encoder,
            logger: _logger).ConfigureAwait(false);
        if (pivResult.IsFailure)
        {
            return Result.Failure<DeliverableResult>(
                $"Failed to render deliverable '{deliverable.Id}': {pivResult.Error}");
        }

        var outputPath = BuildOutputPath(inputPath, outputDirectory, deliverable.FileSuffix);
        await File.WriteAllBytesAsync(outputPath, pivResult.Value.ImageData).ConfigureAwait(false);

        if (!pivResult.Value.Metadata.TryGetValue("ComplianceValidation", out var validationObject)
            || validationObject is not PivComplianceValidation validation)
        {
            return Result.Failure<DeliverableResult>("PIV processing did not expose compliance validation.");
        }

        var checks = new List<AutomatedCheckResult>();
        foreach (var check in deliverable.OutputChecks)
        {
            checks.Add(new AutomatedCheckResult(
                check.Id,
                check.Stage,
                check.Name,
                validation.IsFullyCompliant,
                validation.IsFullyCompliant
                    ? "Rendered card image satisfies the cited PIV geometry rules."
                    : validation.Summary,
                check.Citations));
        }

        return Result.Success(CreateDeliverableResult(deliverable, outputPath, pivResult.Value.ImageData.Length, checks));
    }

    private async Task<Result<DeliverableResult>> RenderPivPrintAsync(
        DocumentDefinition document,
        DeliverableDefinition deliverable,
        string inputPath,
        Image<Rgba32> sourceImage,
        string outputDirectory)
    {
        using var pivSource = sourceImage.Clone();
        var pivResult = await PivProcessor.ProcessAsync(
            pivSource,
            _faceDetector,
            _landmarkExtractor,
            _jpeg2000Encoder,
            logger: _logger).ConfigureAwait(false);
        if (pivResult.IsFailure)
        {
            return Result.Failure<DeliverableResult>(
                $"Failed to render deliverable '{deliverable.Id}': {pivResult.Error}");
        }

        if (!pivResult.Value.Metadata.TryGetValue("ComplianceValidation", out var validationObject)
            || validationObject is not PivComplianceValidation validation)
        {
            return Result.Failure<DeliverableResult>("PIV processing did not expose compliance validation.");
        }

        if (!pivResult.Value.Metadata.TryGetValue("PivImage", out var pivImageObject)
            || pivImageObject is not Image<Rgba32> pivImage)
        {
            return Result.Failure<DeliverableResult>("PIV processing did not expose the rendered PIV image.");
        }

        using var printableImage = pivImage.Clone();
        var outputPath = BuildOutputPath(inputPath, outputDirectory, deliverable.FileSuffix);
        var fileBytes = await EncodeJpegAsync(printableImage, 92, 300).ConfigureAwait(false);
        await File.WriteAllBytesAsync(outputPath, fileBytes).ConfigureAwait(false);
        using var reloaded = Image.Load<Rgba32>(fileBytes);
        var dpiPassed = reloaded.Metadata.HorizontalResolution >= 300f
            && reloaded.Metadata.VerticalResolution >= 300f;

        var checks = new List<AutomatedCheckResult>();
        foreach (var check in deliverable.OutputChecks)
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
                    ? "Rendered printed photo satisfies the cited PIV geometry rules."
                    : validation.Summary,
                "piv-print-dpi" => passed
                    ? $"Rendered print artifact is tagged at {reloaded.Metadata.HorizontalResolution:F0} DPI."
                    : $"Rendered print artifact is tagged below 300 DPI ({reloaded.Metadata.HorizontalResolution:F0} DPI).",
                _ => "Check not implemented."
            };

            checks.Add(new AutomatedCheckResult(check.Id, check.Stage, check.Name, passed, summary, check.Citations));
        }

        return Result.Success(CreateDeliverableResult(deliverable, outputPath, fileBytes.Length, checks));
    }

    private async Task<Result<DeliverableResult>> RenderPassportStyleJpegAsync(
        DocumentDefinition document,
        DeliverableDefinition deliverable,
        string inputPath,
        Image<Rgba32> sourceImage,
        string outputDirectory,
        int targetWidth,
        int targetHeight,
        float targetHeadHeightRatio,
        float targetEyeFromBottomRatio,
        int? dpi,
        float minHeadHeightRatio,
        float maxHeadHeightRatio,
        float minEyeFromBottomRatio,
        float maxEyeFromBottomRatio,
        bool digitalTechnicalCheck)
    {
        var alignment = await AlignPortraitAsync(
            sourceImage,
            targetWidth,
            targetHeight,
            targetHeadHeightRatio,
            targetEyeFromBottomRatio).ConfigureAwait(false);
        if (alignment.IsFailure)
        {
            return Result.Failure<DeliverableResult>(
                $"Failed to render deliverable '{deliverable.Id}': {alignment.Error}");
        }

        using var alignedPortrait = alignment.Value.Image;
        var outputPath = BuildOutputPath(inputPath, outputDirectory, deliverable.FileSuffix);

        var quality = digitalTechnicalCheck ? 88 : 94;
        var fileBytes = await EncodeJpegAsync(alignedPortrait, quality, dpi).ConfigureAwait(false);
        if (digitalTechnicalCheck && deliverable.ValidatorKey.StartsWith("us-", StringComparison.OrdinalIgnoreCase))
        {
            fileBytes = await ShrinkJpegToMaxBytesAsync(alignedPortrait, 245_760, dpi).ConfigureAwait(false);
        }

        await File.WriteAllBytesAsync(outputPath, fileBytes).ConfigureAwait(false);

        var measurements = MeasurePassportComposition(
            alignment.Value.Landmarks,
            targetWidth,
            targetHeight);

        using var reloaded = Image.Load<Rgba32>(fileBytes);
        var checks = new List<AutomatedCheckResult>();
        foreach (var check in deliverable.OutputChecks)
        {
            var passed = check.Id switch
            {
                "us-paper-composition" => measurements.HeadHeightRatio >= minHeadHeightRatio
                    && measurements.HeadHeightRatio <= maxHeadHeightRatio
                    && measurements.EyeFromBottomRatio >= minEyeFromBottomRatio
                    && measurements.EyeFromBottomRatio <= maxEyeFromBottomRatio,
                "us-digital-technical" => reloaded.Width == reloaded.Height
                    && reloaded.Width >= 600
                    && reloaded.Width <= 1200
                    && fileBytes.Length <= 245_760,
                "canada-paper-composition" => measurements.HeadHeightRatio >= minHeadHeightRatio
                    && measurements.HeadHeightRatio <= maxHeadHeightRatio,
                "canada-digital-technical" => reloaded.Width >= 1200
                    && reloaded.Height >= 1800
                    && string.Equals(Path.GetExtension(outputPath), ".jpg", StringComparison.OrdinalIgnoreCase),
                _ => false
            };

            var summary = check.Id switch
            {
                "us-paper-composition" => passed
                    ? $"Rendered paper photo keeps head height at {measurements.HeadHeightRatio:P0} and eye line at {measurements.EyeFromBottomRatio:P0} from the bottom."
                    : $"Rendered paper photo is outside the cited composition range (head {measurements.HeadHeightRatio:P0}, eyes {measurements.EyeFromBottomRatio:P0} from bottom).",
                "us-digital-technical" => passed
                    ? $"Rendered JPEG is {reloaded.Width}x{reloaded.Height} and {fileBytes.Length:N0} bytes."
                    : $"Rendered digital file does not meet the cited square/JPEG/size limits ({reloaded.Width}x{reloaded.Height}, {fileBytes.Length:N0} bytes).",
                "canada-paper-composition" => passed
                    ? $"Rendered paper photo keeps chin-to-crown height at {measurements.HeadHeightRatio:P0} of the image height."
                    : $"Rendered paper photo is outside the 31-36 mm head-height equivalent range ({measurements.HeadHeightRatio:P0}).",
                "canada-digital-technical" => passed
                    ? $"Rendered digital file is {reloaded.Width}x{reloaded.Height} JPEG."
                    : $"Rendered digital file does not meet the published Canada digital upload dimensions.",
                _ => "Check not implemented."
            };

            checks.Add(new AutomatedCheckResult(check.Id, check.Stage, check.Name, passed, summary, check.Citations));
        }

        return Result.Success(CreateDeliverableResult(deliverable, outputPath, fileBytes.Length, checks));
    }

    private async Task<Result<AlignedPortrait>> AlignPortraitAsync(
        Image<Rgba32> sourceImage,
        int targetWidth,
        int targetHeight,
        float targetHeadHeightRatio,
        float targetEyeFromBottomRatio)
    {
        var detection = await _faceDetector.DetectFacesAsync(sourceImage).ConfigureAwait(false);
        if (detection.IsFailure || detection.Value.Count == 0)
        {
            return Result.Failure<AlignedPortrait>("No suitable faces found for portrait rendering.");
        }

        var face = detection.Value
            .OrderByDescending(candidate => candidate.Confidence)
            .First();

        var landmarksResult = await _landmarkExtractor
            .ExtractLandmarksAsync(sourceImage, face.BoundingBox)
            .ConfigureAwait(false);
        if (landmarksResult.IsFailure)
        {
            return Result.Failure<AlignedPortrait>($"Landmark extraction failed: {landmarksResult.Error}");
        }

        var rotation = StandardPortraitProcessorService.CalculateRotation(
            landmarksResult.Value.LeftEyeCenter,
            landmarksResult.Value.RightEyeCenter);

        using var rotated = Math.Abs(rotation) > 0.1f
            ? sourceImage.Clone(ctx => ctx.Rotate(rotation))
            : sourceImage.Clone();

        var rotatedDetection = await _faceDetector.DetectFacesAsync(rotated).ConfigureAwait(false);
        if (rotatedDetection.IsFailure || rotatedDetection.Value.Count == 0)
        {
            return Result.Failure<AlignedPortrait>("Failed to detect a suitable face after leveling the portrait.");
        }

        var rotatedFace = rotatedDetection.Value
            .OrderByDescending(candidate => candidate.Confidence)
            .First();

        var rotatedLandmarksResult = await _landmarkExtractor
            .ExtractLandmarksAsync(rotated, rotatedFace.BoundingBox)
            .ConfigureAwait(false);
        if (rotatedLandmarksResult.IsFailure)
        {
            return Result.Failure<AlignedPortrait>($"Landmark extraction failed after rotation: {rotatedLandmarksResult.Error}");
        }

        var crop = CalculatePassportCrop(
            rotatedLandmarksResult.Value,
            rotated.Width,
            rotated.Height,
            targetWidth,
            targetHeight,
            targetHeadHeightRatio,
            targetEyeFromBottomRatio);

        var outputImage = rotated.Clone(ctx =>
        {
            ctx.Crop(crop);
            ctx.Resize(targetWidth, targetHeight);
        });

        var transformedLandmarks = TransformLandmarks(rotatedLandmarksResult.Value, crop, targetWidth, targetHeight);
        return Result.Success(new AlignedPortrait(outputImage, transformedLandmarks));
    }

    private static Rectangle CalculatePassportCrop(
        FaceLandmarks68 landmarks,
        int imageWidth,
        int imageHeight,
        int targetWidth,
        int targetHeight,
        float targetHeadHeightRatio,
        float targetEyeFromBottomRatio)
    {
        var imageAspect = (float)targetWidth / targetHeight;
        var browY = landmarks.Points.Take(17).Min(point => point.Y);
        var chinY = landmarks.Points[8].Y;
        var faceHeight = chinY - browY;
        var estimatedHeadTop = browY - (faceHeight * 0.3f);
        var headHeight = chinY - estimatedHeadTop;
        var desiredCropHeight = headHeight / targetHeadHeightRatio;
        var desiredCropWidth = desiredCropHeight * imageAspect;
        var eyeCenterY = (landmarks.LeftEyeCenter.Y + landmarks.RightEyeCenter.Y) / 2f;
        var desiredEyeFromTopRatio = 1f - targetEyeFromBottomRatio;
        var cropY = eyeCenterY - (desiredCropHeight * desiredEyeFromTopRatio);

        var faceCenterX = landmarks.Points.Average(point => point.X);
        var cropX = faceCenterX - (desiredCropWidth / 2f);

        desiredCropWidth = Math.Min(desiredCropWidth, imageWidth);
        desiredCropHeight = Math.Min(desiredCropHeight, imageHeight);
        cropX = Math.Clamp(cropX, 0f, Math.Max(0f, imageWidth - desiredCropWidth));
        cropY = Math.Clamp(cropY, 0f, Math.Max(0f, imageHeight - desiredCropHeight));

        var width = Math.Clamp((int)Math.Round(desiredCropWidth), 1, imageWidth);
        var height = Math.Clamp((int)Math.Round(desiredCropHeight), 1, imageHeight);
        var x = Math.Clamp((int)Math.Round(cropX), 0, Math.Max(0, imageWidth - width));
        var y = Math.Clamp((int)Math.Round(cropY), 0, Math.Max(0, imageHeight - height));

        return new Rectangle(x, y, width, height);
    }

    private static FaceLandmarks68 TransformLandmarks(
        FaceLandmarks68 landmarks,
        Rectangle crop,
        int targetWidth,
        int targetHeight)
    {
        var scaleX = (float)targetWidth / crop.Width;
        var scaleY = (float)targetHeight / crop.Height;
        return new FaceLandmarks68(landmarks.Points
            .Select(point => new Point2D(
                (point.X - crop.X) * scaleX,
                (point.Y - crop.Y) * scaleY))
            .ToList());
    }

    private static PassportCompositionMeasurements MeasurePassportComposition(
        FaceLandmarks68 landmarks,
        int width,
        int height)
    {
        var browY = landmarks.Points.Take(17).Min(point => point.Y);
        var chinY = landmarks.Points[8].Y;
        var faceHeight = chinY - browY;
        var estimatedHeadTop = browY - (faceHeight * 0.3f);
        var headHeightRatio = (chinY - estimatedHeadTop) / height;
        var eyeCenterY = (landmarks.LeftEyeCenter.Y + landmarks.RightEyeCenter.Y) / 2f;
        var eyeFromBottomRatio = (height - eyeCenterY) / height;
        return new PassportCompositionMeasurements(headHeightRatio, eyeFromBottomRatio);
    }

    private static async Task<byte[]> EncodeJpegAsync(Image<Rgba32> image, int quality, int? dpi)
    {
        if (dpi.HasValue)
        {
            image.Metadata.HorizontalResolution = dpi.Value;
            image.Metadata.VerticalResolution = dpi.Value;
            image.Metadata.ResolutionUnits = PixelResolutionUnit.PixelsPerInch;
        }

        image.Metadata.ExifProfile ??= new ExifProfile();

        await using var stream = new MemoryStream();
        await image.SaveAsJpegAsync(stream, new JpegEncoder { Quality = quality }).ConfigureAwait(false);
        return stream.ToArray();
    }

    private static async Task<byte[]> ShrinkJpegToMaxBytesAsync(Image<Rgba32> image, int maxBytes, int? dpi)
    {
        for (var quality = 90; quality >= 40; quality -= 5)
        {
            var encoded = await EncodeJpegAsync(image, quality, dpi).ConfigureAwait(false);
            if (encoded.Length <= maxBytes)
            {
                return encoded;
            }
        }

        return await EncodeJpegAsync(image, 40, dpi).ConfigureAwait(false);
    }

    private static DeliverableResult CreateDeliverableResult(
        DeliverableDefinition deliverable,
        string outputPath,
        int fileSizeBytes,
        IReadOnlyList<AutomatedCheckResult> checks)
    {
        var passed = checks.All(check => check.Passed);
        var summary = passed
            ? $"{deliverable.DisplayName} produced and validated."
            : $"{deliverable.DisplayName} produced but failed one or more cited output checks.";

        return new DeliverableResult(
            deliverable.Id,
            deliverable.DisplayName,
            outputPath,
            passed,
            summary,
            fileSizeBytes,
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

    private sealed record AlignedPortrait(Image<Rgba32> Image, FaceLandmarks68 Landmarks);

    private readonly record struct PassportCompositionMeasurements(
        float HeadHeightRatio,
        float EyeFromBottomRatio);
}

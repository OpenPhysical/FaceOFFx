using System.Text.Json;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
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

        var inputBytes = await File.ReadAllBytesAsync(request.InputPath, cancellationToken).ConfigureAwait(false);
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

        using var sourceImage = Image.Load<Rgba32>(inputBytes);
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
                outputDirectory).ConfigureAwait(false);
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
        var faceResult = await SelectSingleFaceAsync(image, profile).ConfigureAwait(false);
        if (faceResult.IsFailure)
        {
            return Result.Success(new AutomatedDocumentInputResult(false, faceResult.Error));
        }

        var landmarksResult = await _landmarkExtractor
            .ExtractLandmarksAsync(image, faceResult.Value.BoundingBox)
            .ConfigureAwait(false);
        if (landmarksResult.IsFailure)
        {
            return Result.Failure<AutomatedDocumentInputResult>($"Input analysis failed: {landmarksResult.Error}");
        }

        var rollDegrees = Math.Abs(StandardPortraitProcessorService.CalculateRotation(
            landmarksResult.Value.LeftEyeCenter,
            landmarksResult.Value.RightEyeCenter));
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

    private async Task<Result<DetectedFace>> SelectSingleFaceAsync(
        Image<Rgba32> image,
        InputProfileDefinition profile)
    {
        var facesResult = await _faceDetector.DetectFacesAsync(image).ConfigureAwait(false);
        if (facesResult.IsFailure)
        {
            return Result.Failure<DetectedFace>($"Input analysis failed: {facesResult.Error}");
        }

        var faces = facesResult.Value
            .Where(face => face.Confidence >= profile.MinimumFaceConfidence)
            .OrderByDescending(face => face.Confidence)
            .ToArray();

        if (faces.Length == 0)
        {
            return Result.Failure<DetectedFace>(
                $"{profile.DisplayName} rejected: no suitable face was detected.");
        }

        if (profile.RequireSingleFace && faces.Length > 1)
        {
            return Result.Failure<DetectedFace>(
                $"{profile.DisplayName} rejected: multiple faces were detected.");
        }

        return Result.Success(faces[0]);
    }

    private async Task<Result<DeliverableResult>> RenderDeliverableAsync(
        DocumentDefinition document,
        DeliverableDefinition deliverable,
        string inputPath,
        byte[] inputBytes,
        Image<Rgba32> sourceImage,
        string outputDirectory)
    {
        return (document.WorkflowFamily, deliverable.Kind) switch
        {
            (DocumentWorkflowFamily.Piv, DeliverableKind.PivCardImage) => await RenderPivCardAsync(deliverable, inputPath, sourceImage, outputDirectory),
            (DocumentWorkflowFamily.Piv, DeliverableKind.PivPrintedPhoto) => await RenderPivPrintAsync(deliverable, inputPath, sourceImage, outputDirectory),
            (DocumentWorkflowFamily.PassportStyle, DeliverableKind.PaperPhoto) => await RenderPortraitDeliverableAsync(deliverable, inputPath, inputBytes, sourceImage, outputDirectory),
            (DocumentWorkflowFamily.PassportStyle, DeliverableKind.DigitalUploadPhoto) => await RenderPortraitDeliverableAsync(deliverable, inputPath, inputBytes, sourceImage, outputDirectory),
            _ => Result.Failure<DeliverableResult>($"Deliverable kind '{deliverable.Kind}' is not implemented for '{document.WorkflowFamily}'.")
        };
    }

    private async Task<Result<DeliverableResult>> RenderPivCardAsync(
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
        string outputDirectory)
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

        var alignment = await AlignPortraitAsync(
            sourceImage,
            spec.TargetWidth,
            spec.TargetHeight,
            spec.TargetHeadHeightRatio,
            spec.TargetEyeFromBottomRatio).ConfigureAwait(false);
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
        var detectionResult = await _faceDetector.DetectFacesAsync(image).ConfigureAwait(false);
        if (detectionResult.IsFailure || detectionResult.Value.Count == 0)
        {
            return Result.Failure<FaceLandmarks68>("No suitable face found.");
        }

        var face = detectionResult.Value
            .OrderByDescending(candidate => candidate.Confidence)
            .First();

        return await _landmarkExtractor.ExtractLandmarksAsync(image, face.BoundingBox).ConfigureAwait(false);
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

        var crop = CalculatePortraitCrop(
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

    private static Rectangle CalculatePortraitCrop(
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

    private sealed record AlignedPortrait(Image<Rgba32> Image, FaceLandmarks68 Landmarks);

    private readonly record struct PortraitMeasurements(float HeadHeightRatio, float EyeFromBottomRatio);

    private readonly record struct AutomatedDocumentInputResult(bool IsCompliant, string Summary);
}

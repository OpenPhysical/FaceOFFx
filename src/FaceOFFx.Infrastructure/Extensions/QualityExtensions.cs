using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Extensions;

/// <summary>
/// Extension methods for integrating quality assessment into the processing pipeline
/// </summary>
[PublicAPI]
public static class QualityExtensions
{
    /// <summary>
    /// Processes an image with quality assessment
    /// </summary>
    public static async Task<Result<(ProcessingResultDto Result, Iso19794Assessment Quality), PipelineError>> 
        ProcessWithQualityAsync(
            this byte[] imageData,
            ProcessingOptions processingOptions,
            QualityAssessmentOptions qualityOptions,
            string format = "jp2",
            int jpegQuality = 85,
            ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        
        try
        {
            using var image = Image.Load<Rgba32>(imageData);
            var servicesResult = FacialProcessingServices.Create(logger);
            if (servicesResult.IsFailure)
            {
                return Result.Failure<(ProcessingResultDto, Iso19794Assessment), PipelineError>(
                    servicesResult.Error);
            }

            using var services = servicesResult.Value;
            
            // Step 1: Detect face
            var faceResult = await services.Detector.DetectFacesAsync(image);
            if (faceResult.IsFailure)
            {
                return Result.Failure<(ProcessingResultDto, Iso19794Assessment), PipelineError>(
                    new DetectionError($"Face detection failed: {faceResult.Error.Message}", "quality"));
            }
            
            var faces = faceResult.Value;
            if (!faces.Any())
            {
                return Result.Failure<(ProcessingResultDto, Iso19794Assessment), PipelineError>(
                    new DetectionError("No faces detected in the image", "quality"));
            }
            
            var primaryFace = faces.OrderByDescending(f => f.Confidence).First();
            
            // Step 2: Extract landmarks
            var landmarksResult = await services.LandmarkExtractor
                .ExtractLandmarksAsync(image, primaryFace.BoundingBox);
            if (landmarksResult.IsFailure)
            {
                return Result.Failure<(ProcessingResultDto, Iso19794Assessment), PipelineError>(
                    landmarksResult.Error);
            }
            
            // Step 3: Perform quality assessment
            var qualityAssessor = new QualityAssessor(logger as ILogger<QualityAssessor> ?? 
                NullLogger<QualityAssessor>.Instance);
            
            var qualityResult = await qualityAssessor.AssessAsync(
                image,
                primaryFace,
                landmarksResult.Value,
                qualityOptions);
            
            if (qualityResult.IsFailure)
            {
                return Result.Failure<(ProcessingResultDto, Iso19794Assessment), PipelineError>(
                    qualityResult.Error);
            }
            
            var quality = qualityResult.Value;
            
            // Step 4: Check if quality meets threshold
            if (qualityOptions.EnforceCompliance && !quality.IsCompliant)
            {
                return Result.Failure<(ProcessingResultDto, Iso19794Assessment), PipelineError>(
                    new ValidationError(
                        $"Image quality {quality.Overall} does not meet compliance requirements. Critical violations: {quality.Violations.Count(v => v.Severity == ViolationSeverity.Critical)}",
                        "quality"));
            }
            
            if (quality.Overall.Value < qualityOptions.MinQualityThreshold)
            {
                return Result.Failure<(ProcessingResultDto, Iso19794Assessment), PipelineError>(
                    new ValidationError(
                        $"Image quality {quality.Overall} is below minimum threshold {qualityOptions.MinQualityThreshold:P0}",
                        "quality"));
            }
            
            // Step 5: Process the image
            var processingResult = await FacialImageEncoder.ProcessAsync(
                imageData,
                processingOptions,
                format,
                jpegQuality,
                logger);
            
            return processingResult.Map(result => (result, quality));
        }
        catch (Exception ex)
        {
            return Result.Failure<(ProcessingResultDto, Iso19794Assessment), PipelineError>(
                new RenderError($"Processing with quality assessment failed: {ex.Message}", "quality"));
        }
    }
    
    /// <summary>
    /// Performs quality assessment on an image without processing
    /// </summary>
    public static async Task<Result<Iso19794Assessment, PipelineError>> AssessQualityAsync(
        this byte[] imageData,
        QualityAssessmentOptions? options = null,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        options ??= new QualityAssessmentOptions();
        
        try
        {
            using var image = Image.Load<Rgba32>(imageData);
            var servicesResult = FacialProcessingServices.Create(logger);
            if (servicesResult.IsFailure)
            {
                return Result.Failure<Iso19794Assessment, PipelineError>(servicesResult.Error);
            }

            using var services = servicesResult.Value;
            
            // Detect face
            var faceResult = await services.Detector.DetectFacesAsync(image);
            if (faceResult.IsFailure)
            {
                return Result.Failure<Iso19794Assessment, PipelineError>(
                    new DetectionError($"Face detection failed: {faceResult.Error.Message}", "quality"));
            }
            
            var faces = faceResult.Value;
            if (!faces.Any())
            {
                return Result.Failure<Iso19794Assessment, PipelineError>(
                    new DetectionError("No faces detected in the image", "quality"));
            }
            
            var primaryFace = faces.OrderByDescending(f => f.Confidence).First();
            
            // Extract landmarks
            var landmarksResult = await services.LandmarkExtractor
                .ExtractLandmarksAsync(image, primaryFace.BoundingBox);
            if (landmarksResult.IsFailure)
            {
                return Result.Failure<Iso19794Assessment, PipelineError>(landmarksResult.Error);
            }
            
            // Perform quality assessment
            var qualityAssessor = new QualityAssessor(logger as ILogger<QualityAssessor> ?? 
                NullLogger<QualityAssessor>.Instance);
            
            return await qualityAssessor.AssessAsync(
                image,
                primaryFace,
                landmarksResult.Value,
                options);
        }
        catch (Exception ex)
        {
            return Result.Failure<Iso19794Assessment, PipelineError>(
                new RenderError($"Quality assessment failed: {ex.Message}", "quality"));
        }
    }
    
    /// <summary>
    /// Validates compliance with a specific standard without processing
    /// </summary>
    public static async Task<Result<ComplianceAssessment, PipelineError>> ValidateComplianceAsync(
        this byte[] imageData,
        string standardName = "PIV",
        float minConfidence = 0.8f,
        ILogger? logger = null,
        AssessmentMode mode = AssessmentMode.InputValidation)
    {
        logger ??= NullLogger.Instance;
        
        try
        {
            using var image = Image.Load<Rgba32>(imageData);
            var servicesResult = FacialProcessingServices.Create(logger);
            if (servicesResult.IsFailure)
            {
                return Result.Failure<ComplianceAssessment, PipelineError>(servicesResult.Error);
            }

            using var services = servicesResult.Value;
            Maybe<OutputPortraitAssessmentInput> outputPortrait = Maybe<OutputPortraitAssessmentInput>.None;
            try
            {
                // Detect face
                var faceResult = await services.Detector.DetectFacesAsync(image);
                if (faceResult.IsFailure)
                {
                    return Result.Failure<ComplianceAssessment, PipelineError>(
                        new DetectionError($"Face detection failed: {faceResult.Error.Message}", standardName));
                }
                
                var faces = faceResult.Value;
                if (!faces.Any())
                {
                    return Result.Failure<ComplianceAssessment, PipelineError>(
                        new DetectionError("No faces detected in the image", standardName));
                }
                
                var primaryFace = faces.OrderByDescending(f => f.Confidence).First();
                if (primaryFace.Confidence < minConfidence)
                {
                    return Result.Failure<ComplianceAssessment, PipelineError>(
                        new ValidationError(
                            $"Face detection confidence {primaryFace.Confidence:P1} is below minimum {minConfidence:P1}",
                            standardName));
                }
                
                // Extract landmarks
                var landmarksResult = await services.LandmarkExtractor
                    .ExtractLandmarksAsync(image, primaryFace.BoundingBox);
                if (landmarksResult.IsFailure)
                {
                    return Result.Failure<ComplianceAssessment, PipelineError>(landmarksResult.Error);
                }

                if (mode == AssessmentMode.OutputValidation)
                {
                    var portraitLogger = NullLogger<StandardPortraitProcessorService>.Instance;
                    var portraitProcessor = new StandardPortraitProcessorService(
                        services.Detector,
                        services.LandmarkExtractor,
                        services.Encoder,
                        portraitLogger);

                    var alignmentResult = await portraitProcessor.AlignAsync(
                        image,
                        standardName,
                        minConfidence,
                        minFaceSize: 50);

                    if (alignmentResult.IsFailure)
                    {
                        return Result.Failure<ComplianceAssessment, PipelineError>(alignmentResult.Error);
                    }

                    using var alignedPortrait = alignmentResult.Value;
                    outputPortrait = Maybe<OutputPortraitAssessmentInput>.From(new OutputPortraitAssessmentInput(
                        alignedPortrait.ProcessedImage.Clone(),
                        alignedPortrait.ProcessedLandmarks));
                }

                return mode == AssessmentMode.OutputValidation
                    ? await ComplianceAssessmentPipeline.AssessOutputComplianceAsync(
                        image,
                        primaryFace,
                        landmarksResult.Value,
                        outputPortrait.Value,
                        standardName)
                    : await ComplianceAssessmentPipeline.AssessInputComplianceAsync(
                        image,
                        primaryFace,
                        landmarksResult.Value,
                        standardName);
            }
            finally
            {
                if (outputPortrait.HasValue)
                {
                    outputPortrait.Value.Image.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            return Result.Failure<ComplianceAssessment, PipelineError>(
                new RenderError($"Compliance validation failed: {ex.Message}", standardName));
        }
    }
}

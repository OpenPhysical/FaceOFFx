using CSharpFunctionalExtensions;
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
    public static async Task<Result<(ProcessingResultDto Result, Iso19794Assessment Quality)>> 
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
            using var services = new FacialProcessingServices(logger);
            
            // Step 1: Detect face
            var faceResult = await services.Detector.DetectFacesAsync(image);
            if (faceResult.IsFailure)
            {
                return Result.Failure<(ProcessingResultDto, Iso19794Assessment)>(
                    $"Face detection failed: {faceResult.Error}");
            }
            
            var faces = faceResult.Value;
            if (!faces.Any())
            {
                return Result.Failure<(ProcessingResultDto, Iso19794Assessment)>(
                    "No faces detected in the image");
            }
            
            var primaryFace = faces.OrderByDescending(f => f.Confidence).First();
            
            // Step 2: Extract landmarks
            var landmarksResult = await services.LandmarkExtractor
                .ExtractLandmarksAsync(image, primaryFace.BoundingBox);
            if (landmarksResult.IsFailure)
            {
                return Result.Failure<(ProcessingResultDto, Iso19794Assessment)>(
                    $"Landmark extraction failed: {landmarksResult.Error}");
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
                return Result.Failure<(ProcessingResultDto, Iso19794Assessment)>(
                    $"Quality assessment failed: {qualityResult.Error}");
            }
            
            var quality = qualityResult.Value;
            
            // Step 4: Check if quality meets threshold
            if (qualityOptions.EnforceCompliance && !quality.IsCompliant)
            {
                return Result.Failure<(ProcessingResultDto, Iso19794Assessment)>(
                    $"Image quality {quality.Overall} does not meet compliance requirements. " +
                    $"Critical violations: {quality.Violations.Count(v => v.Severity == ViolationSeverity.Critical)}");
            }
            
            if (quality.Overall.Value < qualityOptions.MinQualityThreshold)
            {
                return Result.Failure<(ProcessingResultDto, Iso19794Assessment)>(
                    $"Image quality {quality.Overall} is below minimum threshold {qualityOptions.MinQualityThreshold:P0}");
            }
            
            // Step 5: Process the image
            var processingResult = await FacialImageEncoder.ProcessAsync(
                imageData,
                processingOptions,
                format,
                jpegQuality,
                logger);
            
            return Result.Success((processingResult, quality));
        }
        catch (Exception ex)
        {
            return Result.Failure<(ProcessingResultDto, Iso19794Assessment)>(
                $"Processing with quality assessment failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Performs quality assessment on an image without processing
    /// </summary>
    public static async Task<Result<Iso19794Assessment>> AssessQualityAsync(
        this byte[] imageData,
        QualityAssessmentOptions? options = null,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        options ??= new QualityAssessmentOptions();
        
        try
        {
            using var image = Image.Load<Rgba32>(imageData);
            using var services = new FacialProcessingServices(logger);
            
            // Detect face
            var faceResult = await services.Detector.DetectFacesAsync(image);
            if (faceResult.IsFailure)
            {
                return Result.Failure<Iso19794Assessment>($"Face detection failed: {faceResult.Error}");
            }
            
            var faces = faceResult.Value;
            if (!faces.Any())
            {
                return Result.Failure<Iso19794Assessment>("No faces detected in the image");
            }
            
            var primaryFace = faces.OrderByDescending(f => f.Confidence).First();
            
            // Extract landmarks
            var landmarksResult = await services.LandmarkExtractor
                .ExtractLandmarksAsync(image, primaryFace.BoundingBox);
            if (landmarksResult.IsFailure)
            {
                return Result.Failure<Iso19794Assessment>($"Landmark extraction failed: {landmarksResult.Error}");
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
            return Result.Failure<Iso19794Assessment>($"Quality assessment failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Validates compliance with a specific standard without processing
    /// </summary>
    public static async Task<Result<ComplianceAssessment>> ValidateComplianceAsync(
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
            using var services = new FacialProcessingServices(logger);
            OutputPortraitAssessmentInput? outputPortrait = null;
            try
            {
                // Detect face
                var faceResult = await services.Detector.DetectFacesAsync(image);
                if (faceResult.IsFailure)
                {
                    return Result.Failure<ComplianceAssessment>($"Face detection failed: {faceResult.Error}");
                }
                
                var faces = faceResult.Value;
                if (!faces.Any())
                {
                    return Result.Failure<ComplianceAssessment>("No faces detected in the image");
                }
                
                var primaryFace = faces.OrderByDescending(f => f.Confidence).First();
                if (primaryFace.Confidence < minConfidence)
                {
                    return Result.Failure<ComplianceAssessment>(
                        $"Face detection confidence {primaryFace.Confidence:P1} is below minimum {minConfidence:P1}");
                }
                
                // Extract landmarks
                var landmarksResult = await services.LandmarkExtractor
                    .ExtractLandmarksAsync(image, primaryFace.BoundingBox);
                if (landmarksResult.IsFailure)
                {
                    return Result.Failure<ComplianceAssessment>($"Landmark extraction failed: {landmarksResult.Error}");
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
                        return Result.Failure<ComplianceAssessment>(
                            $"Output portrait alignment failed: {alignmentResult.Error}");
                    }

                    using var alignedPortrait = alignmentResult.Value;
                    outputPortrait = new OutputPortraitAssessmentInput(
                        alignedPortrait.ProcessedImage.Clone(),
                        alignedPortrait.ProcessedLandmarks);
                }
                
                return await ComplianceAssessmentPipeline.AssessComplianceAsync(
                    image,
                    primaryFace,
                    landmarksResult.Value,
                    standardName,
                    mode,
                    outputPortrait);
            }
            finally
            {
                outputPortrait?.Image.Dispose();
            }
        }
        catch (Exception ex)
        {
            return Result.Failure<ComplianceAssessment>($"Compliance validation failed: {ex.Message}");
        }
    }
}

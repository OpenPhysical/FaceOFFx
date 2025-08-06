using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Quality;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Implementation of ISO/IEC 19794-5 quality assessment
/// </summary>
[PublicAPI]
public class QualityAssessor : IQualityAssessor
{
    private readonly ILogger<QualityAssessor> _logger;
    
    /// <summary>
    /// Initializes a new instance of the QualityAssessor class
    /// </summary>
    /// <param name="logger">Logger for diagnostic output</param>
    public QualityAssessor(ILogger<QualityAssessor> logger)
    {
        _logger = logger;
    }
    
    /// <inheritdoc />
    public async Task<Result<Iso19794Assessment>> AssessAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        QualityAssessmentOptions options)
    {
        _logger.LogDebug(
            "Starting quality assessment for {Width}x{Height} image with standard {Standard}",
            image.Width,
            image.Height,
            options.Standard.Name);
        
        var startTime = DateTime.UtcNow;
        
        var result = await QualityAssessmentPipeline.AssessAsync(image, face, landmarks, options);
        
        if (result.IsSuccess)
        {
            var assessment = result.Value;
            var elapsed = DateTime.UtcNow - startTime;
            
            _logger.LogInformation(
                "Quality assessment completed in {ElapsedMs}ms. Overall score: {Score}, Compliant: {Compliant}",
                elapsed.TotalMilliseconds,
                assessment.Overall,
                assessment.IsCompliant);
            
            if (assessment.Violations.Any())
            {
                _logger.LogWarning(
                    "Quality violations found: {ViolationCount} total ({CriticalCount} critical)",
                    assessment.Violations.Count,
                    assessment.Violations.Count(v => v.Severity == ViolationSeverity.Critical));
            }
        }
        else
        {
            _logger.LogError("Quality assessment failed: {Error}", result.Error);
        }
        
        return result;
    }
}
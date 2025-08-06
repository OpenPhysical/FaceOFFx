# API Changes and Breaking Changes - FaceOFFx v2.0

## Overview

This document outlines the API changes and breaking changes introduced in FaceOFFx v2.0 with the addition of the ISO/IEC 19794-5 quality assessment system.

## New Features Added

### ISO/IEC 19794-5 Quality Assessment System

A comprehensive quality assessment system has been added to evaluate facial images against international biometric standards.

#### New Namespaces

- `FaceOFFx.Core.Domain.Quality` - Core quality assessment types and pipeline
- `FaceOFFx.Core.Domain.Quality.Assessors` - Individual assessment components

#### New Public Types

##### Core Assessment Types

```csharp
// Quality score representation (0-1 normalized)
public record QualityScore
{
    public float Value { get; }
    public static Result<QualityScore> Create(float value)
    public static Result<QualityScore> FromPercentage(float percentage)
    public float AsPercentage()
    public static implicit operator float(QualityScore score)
}

// Complete ISO/IEC 19794-5 assessment result
public record Iso19794Assessment(
    QualityScore Overall,
    FacialSymmetryScore Symmetry,
    SharpnessScore Sharpness,
    GeometricCompliance Geometry,
    IReadOnlyList<ComplianceViolation> Violations,
    DateTime AssessmentTime)
{
    public bool IsCompliant { get; }
    public string Summary { get; }
}
```

##### Assessment Component Types

```csharp
// Facial symmetry assessment using Gabor wavelets
public record FacialSymmetryScore(
    QualityScore Overall,
    QualityScore Illumination,
    QualityScore Pose,
    IReadOnlyList<float> GaborResponses)

// Image sharpness assessment using DCT analysis
public record SharpnessScore(
    QualityScore Overall,
    float HighFrequencyRatio,
    IReadOnlyDictionary<string, float> RegionalScores)

// Geometric compliance with standards
public record GeometricCompliance(
    QualityScore HeadSize,
    QualityScore Centering,
    QualityScore InterPupillaryDistance,
    ImageDimensions ActualDimensions,
    ImageDimensions ExpectedDimensions)
{
    public QualityScore Overall { get; }
}
```

##### Configuration and Standards

```csharp
// Assessment configuration options
public record QualityAssessmentOptions
{
    public Iso19794Standard Standard { get; init; }
    public bool EnforceCompliance { get; init; }
    public float MinQualityThreshold { get; init; }
    public bool EnableParallelAssessment { get; init; }
    public float MaxRotationCorrection { get; init; }
    
    // Predefined presets
    public static QualityAssessmentOptions Strict { get; }
    public static QualityAssessmentOptions Lenient { get; }
    public static QualityAssessmentOptions Twic { get; }
    public static QualityAssessmentOptions Icao { get; }
    
    public static QualityAssessmentOptions ForStandard(string standard)
}

// Standard definitions for different credential types
public record Iso19794Standard
{
    public string Name { get; }
    public ImageDimensions ExpectedDimensions { get; }
    public float MinHeadWidthRatio { get; }
    public float MaxHeadWidthRatio { get; }
    public float MinInterPupillaryDistance { get; }
    public float MaxInterPupillaryDistance { get; }
    
    // Predefined standards
    public static Iso19794Standard Piv { get; }
    public static Iso19794Standard Twic { get; }
    public static Iso19794Standard Icao { get; }
    public static Iso19794Standard Cac { get; }
}
```

##### Violation Reporting

```csharp
// Compliance violation details
public record ComplianceViolation(
    string Category,
    string Description,
    ViolationSeverity Severity,
    RoiBoundingBox? AffectedRegion = null)

// Violation severity levels
public enum ViolationSeverity
{
    Minor,
    Moderate,
    Critical
}
```

#### New Static Assessment Classes

```csharp
// Main assessment pipeline
public static class QualityAssessmentPipeline
{
    public static async Task<Result<Iso19794Assessment>> AssessAsync(
        Image<Rgba32> image,
        DetectedFace face,
        FaceLandmarks68 landmarks,
        QualityAssessmentOptions options)
}

// Individual assessors
public static class SymmetryAssessor
{
    public static Result<FacialSymmetryScore> Assess(
        Image<Rgba32> image,
        FaceLandmarks68 landmarks)
}

public static class SharpnessAssessor
{
    public static Result<SharpnessScore> Assess(Image<Rgba32> image)
}

public static class GeometryAssessor
{
    public static Result<GeometricCompliance> Assess(
        ImageDimensions imageDimensions,
        FaceLandmarks68 landmarks,
        Iso19794Standard standard)
}
```

## API Integration Points

### Usage with Existing Pipeline

The quality assessment system integrates with existing FaceOFFx components:

```csharp
// Example integration
var detectionResult = await faceDetector.DetectAsync(image);
var landmarksResult = await landmarkExtractor.ExtractAsync(image, detectionResult.Value.First());

var qualityResult = await QualityAssessmentPipeline.AssessAsync(
    image,
    detectionResult.Value.First(),
    landmarksResult.Value,
    QualityAssessmentOptions.Strict);

if (qualityResult.IsSuccess && qualityResult.Value.IsCompliant)
{
    // Proceed with further processing
}
```

## Breaking Changes

### None

This release introduces **no breaking changes** to existing APIs. All new functionality is additive and existing code will continue to work without modification.

## Dependencies

### New Dependencies Added

The quality assessment system requires:

- **CSharpFunctionalExtensions** (already present) - For functional programming patterns
- **SixLabors.ImageSharp** (already present) - For image processing
- **JetBrains.Annotations** (already present) - For API documentation

No additional external dependencies are required.

## Performance Considerations

### Processing Overhead

Quality assessment adds processing time:

- **Symmetry Assessment**: ~50-100ms per image (Gabor wavelet analysis)
- **Sharpness Assessment**: ~20-50ms per image (DCT frequency analysis)
- **Geometry Assessment**: ~1-5ms per image (landmark calculations)

### Parallel Processing

- Quality assessments can run in parallel when `EnableParallelAssessment = true`
- Total assessment time with parallelization: ~50-100ms per image
- Memory usage increase: ~10-20MB per concurrent assessment

## Migration Guide

### For Existing Applications

No migration is required. To add quality assessment:

1. **Add quality assessment after face detection and landmark extraction**:
   ```csharp
   var options = QualityAssessmentOptions.ForStandard("piv");
   var assessment = await QualityAssessmentPipeline.AssessAsync(image, face, landmarks, options);
   ```

2. **Handle assessment results**:
   ```csharp
   if (assessment.IsSuccess)
   {
       var result = assessment.Value;
       if (result.IsCompliant)
       {
           // Image meets standards
       }
       else
       {
           // Handle violations
           foreach (var violation in result.Violations)
           {
               // Log or handle violation
           }
       }
   }
   ```

## Standard Compliance

The quality assessment system implements:

- **ISO/IEC 19794-5** facial image quality standards
- **PIV** (Personal Identity Verification) requirements
- **TWIC** (Transportation Worker Identification Credential) requirements
- **ICAO** (International Civil Aviation Organization) passport requirements
- **CAC** (Common Access Card) requirements

## Technical Implementation Details

### Algorithm Overview

1. **Symmetry Assessment**: Uses multi-orientation Gabor wavelets to detect facial asymmetries in pose and illumination
2. **Sharpness Assessment**: Employs DCT-based frequency analysis to measure image sharpness across regions
3. **Geometry Assessment**: Validates head size, centering, and inter-pupillary distance against standard requirements

### Functional Programming Approach

- **No exceptions thrown** - All failures returned as `Result<T>` types
- **Immutable data structures** - All assessment results are immutable records
- **Pure functions** - Assessors have no side effects
- **Railway-oriented programming** - Chained operations with early failure detection

## Version Compatibility

- **Minimum .NET version**: No change (continues to support existing target frameworks)
- **Backward compatibility**: 100% - existing code requires no changes
- **Forward compatibility**: New types follow established patterns and will remain stable

## Documentation and Examples

For detailed usage examples and advanced scenarios, see:

- `docs/QUALITY_ASSESSMENT_REVIEW.md` - Comprehensive code review and architecture details
- `docs/PERFORMANCE_ANALYSIS.md` - Performance characteristics and optimization guidance
- Unit tests in `tests/` directory - Practical usage examples

---

**Note**: This API documentation reflects the state as of FaceOFFx v2.0. The quality assessment system is production-ready and follows the established functional programming patterns used throughout the codebase.
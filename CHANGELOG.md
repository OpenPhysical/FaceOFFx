# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.1.0] - 2025-01-05

### Added
- **ISO/IEC 19794-5 Quality Assessment System** - Comprehensive quality assessment for facial images
  - Facial symmetry assessment using Gabor wavelets
  - Image sharpness assessment using DCT frequency analysis  
  - Geometric compliance validation against PIV/TWIC/ICAO/CAC standards
  - Configurable quality thresholds and enforcement modes
  - Detailed violation reporting with severity levels
  - Parallel and sequential assessment modes for performance optimization

### New API Components
- `QualityAssessmentPipeline` - Main assessment pipeline with async support
- `QualityScore` - Normalized quality scores (0-1) with validation
- `FacialSymmetryScore` - Symmetry assessment results with illumination and pose metrics
- `SharpnessScore` - Sharpness assessment with regional analysis
- `GeometricCompliance` - Standards compliance validation
- `Iso19794Assessment` - Complete assessment results with compliance status
- `QualityAssessmentOptions` - Configuration with preset options for different standards
- `Iso19794Standard` - Standard definitions for PIV, TWIC, ICAO, and CAC
- `ComplianceViolation` - Detailed violation reporting with severity classification

### Individual Assessors
- `SymmetryAssessor` - Gabor wavelet-based facial symmetry analysis
- `SharpnessAssessor` - DCT-based image sharpness measurement
- `GeometryAssessor` - Geometric compliance validation against standards

### Technical Improvements
- **Functional Programming Patterns**: All new code follows railway-oriented programming with `Result<T>` types
- **No Exceptions**: Quality assessment system uses defensive programming instead of exception handling
- **Immutable Data**: All assessment results are immutable records
- **Resource Management**: Proper disposal patterns without manual resource management
- **Float Consistency**: All mathematical operations use `MathF` for consistent float arithmetic
- **MIT License Headers**: All new files include proper MIT license headers

### Performance Characteristics
- Symmetry assessment: ~50-100ms per image
- Sharpness assessment: ~20-50ms per image  
- Geometry assessment: ~1-5ms per image
- Memory overhead: ~10-20MB per concurrent assessment
- Parallel processing support for optimal performance

### CLI Enhancements
- **New `quality` command** - Standalone quality assessment with multiple output formats
  - `--visual` flag generates quality overlay images showing issues
  - JSON, text, and detailed output formats
  - Configurable thresholds and standards
- **Enhanced `process` command** - Integrated quality assessment in processing pipeline
- **Validation Scripts**:
  - `setup-test-images.sh` - Test image acquisition with synthetic face support
  - `batch-quality-test.sh` - Batch assessment with HTML reporting
  - `quality-review-dashboard.sh` - Interactive human review interface
  - `apply-quality-variations.sh` - Generate test variations

### Quality Visualization
- **Visual Overlay System** - Real-time quality feedback on images
  - Color-coded quality score panels
  - Violation indicators for each quality issue type
  - Compliance status badges (pass/fail)
  - Supports batch generation for validation workflows

### Standards Compliance
- **PIV (Personal Identity Verification)** - FIPS 201-3 compliance
- **TWIC (Transportation Worker Identification Credential)** - TSA requirements
- **ICAO (International Civil Aviation Organization)** - Passport photo standards
- **CAC (Common Access Card)** - DoD requirements

### Breaking Changes
- **None** - This release is fully backward compatible

### Dependencies
- No new external dependencies added
- Continues to use CSharpFunctionalExtensions, SixLabors.ImageSharp, and JetBrains.Annotations

## [2.0.0] - 2024-12-XX

### Added
- Initial release of FaceOFFx
- PIV-compliant facial processing with FIPS 201-3 support
- JPEG 2000 ROI encoding capabilities
- Direct ONNX Runtime integration
- Clean architecture with functional programming patterns
- Face detection using RetinaFace
- 68-point facial landmark extraction
- PIV transformation processing
- CLI tools for processing and ROI operations

### Technical Features
- .NET 8.0 target framework
- Functional programming with Result patterns
- No exceptions in core processing pipeline
- Immutable domain models
- High-performance image processing with SixLabors.ImageSharp
- Git LFS for ONNX model storage

### Standards Support
- FIPS 201-3 Personal Identity Verification
- ISO/IEC 19794-5 facial image standards
- JPEG 2000 with Region of Interest encoding
- 68-point facial landmark standard

[2.1.0]: https://github.com/OpenPhysical/FaceOFFx/compare/v2.0.0...v2.1.0
[2.0.0]: https://github.com/OpenPhysical/FaceOFFx/releases/tag/v2.0.0

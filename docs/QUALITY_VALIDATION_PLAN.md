# FaceOFFx Quality Assessment Validation Plan

## Overview

This document outlines the comprehensive validation strategy for the ISO/IEC 19794-5 quality assessment functionality in FaceOFFx v2.1. The plan ensures accurate quality measurements through automated testing, visual validation, and human-in-the-loop review.

## Validation Components

### 1. Test Image Setup

#### Synthetic Faces (Recommended)
```bash
# Run the setup script
./scripts/setup-test-images.sh

# Options:
# 1. Download synthetic faces from Generated.photos or "This Person Does Not Exist"
# 2. Use Open Images V7 dataset
# 3. Provide your own test images
# 4. Generate geometric test patterns
```

#### Quality Variations
The `apply-quality-variations.sh` script creates test cases with:
- **Blur levels**: Slight (0x2), Moderate (0x5), Severe (0x10)
- **Rotation angles**: 5°, 10°, 15°, 20°
- **Size variations**: 80% (small), 120% (large)
- **Position offsets**: Horizontal and vertical shifts
- **Illumination**: Uneven lighting, side lighting
- **Contrast**: Low and high contrast variations
- **Noise levels**: Gaussian noise additions
- **Compression**: JPEG quality 20, 50, 85

### 2. CLI Testing

#### Individual Image Assessment
```bash
# Basic quality assessment
faceoffx quality -i image.jpg

# With visual overlay
faceoffx quality -i image.jpg --visual

# Detailed output with specific standard
faceoffx quality -i image.jpg -f detailed -s piv --visual

# JSON output for programmatic processing
faceoffx quality -i image.jpg -f json -o results.json
```

#### Batch Processing
```bash
# Run batch quality assessment
./scripts/batch-quality-test.sh ./test-images ./test-results

# This generates:
# - quality_results.csv: Scores for all images
# - quality_report.html: Interactive HTML report
# - *_quality.json: Individual JSON results
# - *_quality_visual.png: Visual overlays
```

### 3. Visual Validation

The visual overlay feature provides immediate feedback on quality issues:

#### Overlay Components
- **Quality Score Panel**: Semi-transparent panel showing:
  - Overall score with color-coded bar
  - Component scores (Symmetry, Sharpness, Geometry)
  - Sub-scores (Illumination, Pose, Head Size, etc.)

- **Violation Indicators**:
  - **Sharpness**: Edge blur indicators
  - **Illumination**: Gradient overlays for uneven lighting
  - **Geometry**: Center crosshair and expected face region
  - **Pose**: Rotation arc indicator

- **Compliance Badge**: 
  - Green checkmark for compliant images
  - Red X for non-compliant images

### 4. Human-in-the-Loop Review

#### Interactive Dashboard
```bash
# Launch the review dashboard
./scripts/quality-review-dashboard.sh

# Features:
# 1. Individual image review with side-by-side comparison
# 2. Batch review mode for quick validation
# 3. Review statistics and agreement rates
# 4. Export review reports
# 5. Direct browser access to HTML reports
```

#### Review Workflow
1. **Individual Review**:
   - Display image with quality assessment
   - Open original and overlay images
   - Record agreement/disagreement
   - Add manual overrides if needed
   - Include review notes

2. **Batch Review**:
   - Quick keyboard shortcuts (a=agree, d=disagree, s=skip)
   - Efficient processing of multiple images
   - Flag images for detailed review

3. **Review Tracking**:
   - JSON-based review storage
   - Reviewer identification
   - Timestamp tracking
   - Override decisions

### 5. Test Coverage Areas

#### Component Testing
1. **SymmetryAssessor**:
   - Gabor wavelet symmetry analysis
   - Illumination uniformity
   - Pose detection
   - Edge cases: null landmarks, out-of-bounds

2. **SharpnessAssessor**:
   - DCT-based frequency analysis
   - Regional sharpness mapping
   - Motion blur detection
   - Edge cases: solid colors, extreme blur

3. **GeometryAssessor**:
   - Head size compliance
   - Face centering
   - Inter-pupillary distance
   - Edge cases: partial faces, extreme angles

#### Integration Testing
- Full pipeline with real images
- Error propagation handling
- Performance with large images
- Memory usage validation

### 6. Expected Quality Ranges

#### PIV Compliance Thresholds
- **Overall**: ≥ 70% for compliance
- **Symmetry**: ≥ 60% (illumination and pose)
- **Sharpness**: ≥ 50% (face region clarity)
- **Geometry**: ≥ 80% (strict positioning)

#### Common Failure Modes
1. **Consumer Photos**:
   - Poor lighting (symmetry < 40%)
   - Casual poses (geometry < 60%)
   - Background blur (sharpness < 30%)

2. **Professional Photos**:
   - Over-processing (sharpness artifacts)
   - Artistic lighting (symmetry issues)
   - Non-standard dimensions

3. **ID Photos**:
   - Generally high compliance (> 80%)
   - May fail on old/worn photos
   - Scanner quality affects sharpness

### 7. Validation Metrics

#### Automated Metrics
- Test coverage: Target 80% for quality components
- Performance: < 100ms per assessment
- Memory usage: < 50MB per image
- Accuracy: Compare with reference implementations

#### Human Review Metrics
- Agreement rate with automated assessment
- False positive/negative identification
- Time per review
- Inter-reviewer consistency

### 8. Continuous Improvement

1. **Feedback Loop**:
   - Collect disagreement cases
   - Analyze failure patterns
   - Adjust thresholds based on data
   - Update test cases

2. **Documentation**:
   - Update threshold rationale
   - Document edge cases
   - Maintain validation logs
   - Track algorithm improvements

## Quick Start Guide

```bash
# 1. Setup test images
./scripts/setup-test-images.sh

# 2. Download or create test faces
cd test-images/quality-validation
python download_synthetic_faces.py

# 3. Apply quality variations
./apply-quality-variations.sh synthetic_faces variations

# 4. Run batch assessment
cd ../..
./scripts/batch-quality-test.sh test-images/quality-validation/variations test-results

# 5. Review results
./scripts/quality-review-dashboard.sh test-images/quality-validation/variations test-results

# 6. Open HTML report
open test-results/quality_report.html
```

## Troubleshooting

### Common Issues

1. **"No faces detected"**:
   - Ensure image contains a clear face
   - Check image orientation
   - Verify sufficient resolution (min 200x200)

2. **Low quality scores**:
   - Review specific component failures
   - Check visual overlay for issues
   - Compare with expected ranges

3. **Performance issues**:
   - Reduce image size if > 5MP
   - Process in batches
   - Check system resources

### Debug Mode
```bash
# Enable verbose logging
export FACEOFFX_LOG_LEVEL=Debug
faceoffx quality -i image.jpg --visual
```

## Conclusion

This validation plan ensures the FaceOFFx quality assessment system is:
- **Accurate**: Through comprehensive testing and validation
- **Transparent**: With visual overlays and detailed reporting
- **Reliable**: Via human-in-the-loop verification
- **Practical**: With clear workflows and automation

Regular execution of this validation plan maintains confidence in the quality assessment results and identifies areas for improvement.
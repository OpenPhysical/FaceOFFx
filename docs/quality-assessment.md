# ISO/IEC 19794-5 Quality Assessment

## Overview

FaceOFFx includes an ISO/IEC 19794-5 quality assessment system that evaluates facial images for compliance with biometric standards. This system provides automated quality checks for PIV, TWIC, ICAO, and CAC credentials.

**IMPORTANT**: These quality checks are **NOT** full ISO/IEC 19794-5 compliance checks. They are simplified assessments based on publicly available research and do not replace professional compliance testing.

## Quality Metrics

### 1. Facial Symmetry Assessment

The symmetry assessor uses Gabor wavelets to evaluate facial symmetry across multiple orientations. This helps detect:

- **Illumination issues**: Uneven lighting across the face
- **Pose variations**: Head tilt or rotation
- **Partial occlusions**: Hair or shadows covering parts of the face

**Implementation Details**:
- Uses 5 Gabor filter orientations (0°, 22.5°, 45°, 67.5°, 90°)
- Compares left and right facial halves
- Returns separate scores for illumination and pose symmetry

**Limitations**:
- Cannot detect all types of lighting problems
- May not identify subtle pose variations
- Does not account for natural facial asymmetry

### 2. Sharpness Assessment

The sharpness assessor uses Discrete Cosine Transform (DCT) frequency analysis to detect:

- **Motion blur**: Camera or subject movement
- **Out-of-focus images**: Poor camera focus
- **Low image quality**: Compression artifacts or poor resolution

**Implementation Details**:
- Analyzes high-frequency content in 8x8 blocks
- Calculates frequency ratios to determine sharpness
- Provides regional sharpness scores

**Limitations**:
- May not distinguish between artistic blur and poor focus
- Cannot improve image quality, only assess it
- Sensitive to JPEG compression artifacts

### 3. Geometric Compliance

The geometric assessor verifies that facial features meet positioning requirements:

- **Head size**: Appropriate ratio to image dimensions
- **Face centering**: Proper positioning within frame
- **Inter-pupillary distance (IPD)**: Within standard ranges

**Implementation Details**:
- Calculates head width from temple points
- Measures face center deviation from image center
- Estimates IPD in millimeters (assuming standard DPI)

**Limitations**:
- IPD calculations assume standard image resolution
- Cannot verify exact physical measurements
- Depends on accurate landmark detection

## Usage

### Command Line Interface

```bash
# Basic quality assessment
faceoffx quality --input photo.jpg

# Specify standard and output format
faceoffx quality --input photo.jpg --standard piv --format json

# Enforce strict compliance
faceoffx quality --input photo.jpg --threshold 0.8 --strict

# Generate detailed report
faceoffx quality --input photo.jpg --format detailed --output report.json
```

### Processing with Quality Gates

```bash
# Process image with quality gate
faceoffx process photo.jpg --quality-gate 0.8

# Process with quality report
faceoffx process photo.jpg --quality-gate 0.7 --quality-report
```

## Standards Support

### PIV (Personal Identity Verification)
- Expected dimensions: 420x560 pixels
- Head width ratio: 50-75% of image width
- IPD range: 90-120 pixels

### TWIC (Transportation Worker Identification Credential)
- Expected dimensions: 420x560 pixels
- Head width ratio: 50-75% of image width
- IPD range: 90-120 pixels

### ICAO (International Civil Aviation Organization)
- Expected dimensions: 413x531 pixels (35x45mm at 300 DPI)
- Head width ratio: 50-75% of image width
- IPD range: 90-120 pixels

### CAC (Common Access Card)
- Expected dimensions: 420x560 pixels
- Head width ratio: 50-75% of image width
- IPD range: 90-120 pixels

## Quality Scores

All quality metrics return normalized scores between 0 and 1:

- **0.8-1.0**: Excellent quality, fully compliant
- **0.6-0.8**: Good quality, minor issues
- **0.4-0.6**: Fair quality, noticeable issues
- **0.0-0.4**: Poor quality, significant problems

## Important Limitations

1. **Not a Certification Tool**: These assessments do not constitute official ISO/IEC 19794-5 certification.

2. **No ML Models**: The quality assessment uses traditional computer vision techniques, not machine learning. This makes it fast but less sophisticated than modern quality assessment systems.

3. **Simplified Metrics**: The implementation focuses on key quality indicators but does not cover all aspects of the ISO/IEC 19794-5 standard.

4. **Resolution Assumptions**: IPD calculations assume standard image resolutions. Actual physical measurements may vary.

5. **No Expression Analysis**: The system does not check for neutral expressions or closed mouths as required by some standards.

6. **No Background Analysis**: Background uniformity and color are not evaluated.

7. **Limited Pose Detection**: Only basic pose assessment through symmetry analysis.

8. **No Glasses/Occlusion Detection**: The system does not specifically check for glasses, hats, or other occlusions.

## Best Practices

1. **Use as a Pre-Check**: Use quality assessment as a preliminary check before professional compliance testing.

2. **Combine with Manual Review**: Always perform manual review for critical applications.

3. **Adjust Thresholds**: Different applications may require different quality thresholds.

4. **Consider Context**: Some "quality issues" may be acceptable depending on your use case.

5. **Regular Updates**: Keep the software updated as quality standards evolve.

## Integration Example

```csharp
// Using quality assessment in code
var imageData = await File.ReadAllBytesAsync("photo.jpg");

var qualityOptions = QualityAssessmentOptions.ForStandard("piv") with
{
    MinQualityThreshold = 0.75f,
    EnforceCompliance = true
};

var result = await imageData.AssessQualityAsync(qualityOptions);

if (result.IsSuccess)
{
    var assessment = result.Value;
    Console.WriteLine($"Overall Quality: {assessment.Overall}");
    Console.WriteLine($"Compliant: {assessment.IsCompliant}");
    
    foreach (var violation in assessment.Violations)
    {
        Console.WriteLine($"- {violation.Severity}: {violation.Description}");
    }
}
```

## References

This implementation is based on:

1. "Face Image Quality Evaluation for ISO/IEC Standards 19794-5 and 29794-5" - Research paper describing Gabor wavelet and DCT-based quality assessment techniques.

2. ISO/IEC 19794-5:2011 - Information technology — Biometric data interchange formats — Part 5: Face image data

3. NIST Special Publication 800-76-2 - Biometric Specifications for Personal Identity Verification

## Future Improvements

Potential enhancements not currently implemented:

- Machine learning-based quality assessment
- Expression neutrality detection
- Background uniformity analysis
- Glasses and occlusion detection
- Advanced pose estimation
- Color space analysis
- Skin tone evaluation
- Eye openness detection

For production biometric systems requiring full compliance, consider professional quality assessment tools and certification services.
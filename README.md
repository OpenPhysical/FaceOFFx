# FaceOFFx – PIV-Compatible Facial Processing for .NET

![FaceOFFx ROI Visualization](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/roi/generic_guy_roi_300w.jpg)

*"I want to take his face... off."*
— Castor Troy, *Face/Off* (1997)

[Quick Start](#quick-start) • [Installation](#installation) • [Samples](#sample-gallery) • [API](#api-reference) • [CLI](#cli-usage) • [Configuration](#configuration)

---

## About

FaceOFFx is a specialized, high-performance facial processing library for .NET, focused on **PIV (Personal Identity
Verification)**
compatibility for issuing credentials that follow government standards (FIPS 201). Derived from the excellent *
*[FaceONNX](https://github.com/FaceONNX/FaceONNX)** library,
FaceOFFx extends its capabilities with PIV-specific transformations, FIPS 201-3 compatibility features, and advanced JPEG
2000 ROI encoding.

### Key Features

- **PIV Card Compatibility** - FIPS 201-3 compatible 420×560 output
- **JPEG 2000 ROI Encoding** - Smart compression with profile-defined hard caps or explicit rates
- **68-Point Landmark Detection** - Precise facial feature mapping
- **High Performance** - Direct ONNX Runtime integration
- **Cross-Platform** - Windows, Linux, macOS via .NET 8, 9, and 10
- **Self-Contained** - Embedded models, no external dependencies
- **Pre-trained Models Only** - Uses existing RetinaFace and PFLD models, no training performed

## Quick Start

### Profile Encoding API

The public library API is profile-first. Choose a `ProfileSpecification`, pass source image bytes to
`ProfileEncoder`, and handle the typed railway result:

```csharp
using FaceOFFx.Infrastructure.Services;
using FaceOFFx.Core.Domain.Transformations;
using Microsoft.Extensions.Logging.Abstractions;

byte[] imageData = File.ReadAllBytes("photo.jpg");

using var loggerFactory = NullLoggerFactory.Instance;
using var serviceFactory = new OnnxFacialProcessingServiceFactory(loggerFactory);

var geometryPipeline = new FaceGeometryPipeline(
    serviceFactory,
    NullLogger<FaceGeometryPipeline>.Instance);

var encoder = new ProfileEncoder(
    geometryPipeline,
    serviceFactory,
    NullLogger<ProfileEncoder>.Instance);

var result = await encoder.ProcessAsync(imageData, ProfileSpecifications.Piv);
if (result.IsFailure)
{
    Console.WriteLine($"Processing failed: [{result.Error.Code}] {result.Error.Message}");
    return;
}

File.WriteAllBytes("piv.jp2", result.Value.ImageData);
Console.WriteLine($"Profile: {result.Value.Profile.DisplayName}");
Console.WriteLine($"Output: {result.Value.OutputDimensions.Width}x{result.Value.OutputDimensions.Height}");
Console.WriteLine($"Size: {result.Value.Encoding.FileSize:N0} bytes");
Console.WriteLine($"Rate: {result.Value.Encoding.CompressionRate:F2} bpp");
```

The PIV profile renders a 420×560 card portrait, preserves the facial ROI at higher quality,
and solves for the highest-quality JPEG 2000 candidate under the named `preferred` 22KB
card-image cap. Use the named `minimum` target when the raw JP2 output must leave room for
later card-container wrapping and signing overhead.

### Document Workflows

The primary CLI surface is now document-specific. These commands analyze the source photo, render the requested artifact set, validate the actual outputs, and write a provenance JSON file with the cited rules that were applied.

```bash
# Federal PIV issuance bundle
faceoffx piv photo.jpg

# Federal PIV digital card image using the minimum-capacity target
faceoffx piv photo.jpg --variant digital --filesize-target minimum

# U.S. passport paper photo
faceoffx us-passport photo.jpg

# U.S. passport digital photo
faceoffx us-passport photo.jpg --variant digital

# U.S. permanent resident photo
faceoffx us-permanent-resident photo.jpg

# Canadian passport paper photo
faceoffx canada-passport photo.jpg

# Canadian permanent resident card photo
faceoffx canada-permanent-resident photo.jpg

# Canadian proof of citizenship digital photo
faceoffx canada-proof-of-citizenship photo.jpg --variant digital

# Discover shipped document workflows and variants
faceoffx documents
```

### Diagnostics Workflow

Engineering diagnostics now live in a separate tool so the release CLI stays document-focused:

```bash
faceoffx-diagnostics detect --corpus people --output artifacts/diagnostics/detect --verify
faceoffx-diagnostics detect --corpus people --output artifacts/diagnostics/detect
```

`faceoffx-diagnostics detect` writes one folder per subject plus a root `manifest.json`. Each
subject folder contains an ordered set of PNGs that shows the human-detection pipeline step
by step:

- `00-original.png`: Source image with no overlays
- `10-coarse.png`: RetinaFace detector box and 5-point landmarks
- `20-chip-locate.png`: Projected chip footprint on the source image
- `30-chip.png`: Canonical extracted chip
- `40-fine-chip.png`: Fine 68-point landmarks on the chip
- `50-fine-source.png`: Fine 68-point landmarks projected back into source coordinates

Example:

```bash
faceoffx-diagnostics detect --corpus people --output /tmp/faceoffx-human-detect-stages
open -a Finder /tmp/faceoffx-human-detect-stages
```

The output layout looks like this:

```text
artifacts/diagnostics/detect/
├── manifest.json
├── generic-guy/
│   ├── 00-original.png
│   ├── 10-coarse.png
│   ├── 20-chip-locate.png
│   ├── 30-chip.png
│   ├── 40-fine-chip.png
│   └── 50-fine-source.png
└── person-01/
    ├── 00-original.png
    ├── 10-coarse.png
    ├── 20-chip-locate.png
    ├── 30-chip.png
    ├── 40-fine-chip.png
    └── 50-fine-source.png
```

#### Profile Encoding Goals

PIV encoding is specified by the profile, not by caller-side option bags:

- PIV uses the named `preferred` 22KB hard cap for the card facial image by default.
- PIV also exposes a named `minimum` 12KB target for minimum-capacity card workflows that need room below the SP 800-73 cardholder facial-image container minimum for CBEFF, signing, and wrapping overhead.
- The encoding solver chooses the highest-quality JPEG 2000 candidate that fits under the cap.
- ROI behavior is part of the `EncodingSpecification` attached to the profile.

The `minimum` target caps the raw JP2 profile output. FaceOFFx does not claim to size a final
CBEFF-wrapped or signed card object unless that wrapping is performed and measured by the caller.

#### JPEG 2000 Compression Guidelines

For 420×560 images:

| Rate (bpp) | Approx. Size | Quality Level |
|------------|--------------|---------------|
| 0.36       | 11.6KB       | Small card image |
| 0.68       | 20.6KB       | PIV card output |
| 0.96       | 29.5KB       | More texture detail |
| 1.70       | 49.8KB       | High-detail comparison |
| 4.00       | 82.1KB       | Very high-detail comparison |

## Installation

### As a .NET Global Tool

```bash
# Install from NuGet
dotnet tool install --global FaceOFFx.Cli

# Update to latest version
dotnet tool update --global FaceOFFx.Cli
```

### As a Library (NuGet Package)

```bash
# Package Manager
dotnet add package FaceOFFx

# Package Manager Console
Install-Package FaceOFFx
```

### Requirements

- .NET 8.0, 9.0, or 10.0
- Windows, Linux, or macOS
- No GPU required (CPU inference supported)

## Sample Gallery

These assets are generated from the canonical people corpus with the v3 diagnostics CLI:

```bash
tests/regenerate_docs_images.sh
```

The source images shown here are 420px-wide display thumbnails. Processing uses the full-resolution
test inputs, then writes decoded PNG previews of the actual JPEG 2000 outputs.

| Subject | Source | PIV Output | ROI Visualization | Encoded Size |
|---------|--------|------------|-------------------|--------------|
| Generic Guy | ![Generic Guy Source](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/original/generic_guy_420w.jpg) | ![Generic Guy PIV](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/processed/generic_guy_piv.png) | ![Generic Guy ROI](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/roi/generic_guy_roi.jpg) | 20,612 bytes at 0.68 bpp |
| Bush | ![Bush Source](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/original/bush_420w.jpg) | ![Bush PIV](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/processed/bush_piv.png) | ![Bush ROI](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/roi/bush_roi.jpg) | 20,451 bytes at 0.68 bpp |
| Carter | ![Carter Source](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/original/carter_420w.jpg) | ![Carter PIV](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/processed/carter_piv.png) | ![Carter ROI](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/roi/carter_roi.jpg) | 20,641 bytes at 0.68 bpp |
| Johnson | ![Johnson Source](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/original/johnson_420w.jpg) | ![Johnson PIV](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/processed/johnson_piv.png) | ![Johnson ROI](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/roi/johnson_roi.jpg) | 20,481 bytes at 0.68 bpp |

### File Size Comparison - Keir Starmer

This comparison uses the same PIV crop and ROI. Only the JPEG 2000 rate changes, so the table shows
how small the encoded file can get and what extra bytes buy visually.

| **0.36 bpp** | **0.68 bpp** | **0.96 bpp** |
|--------------|--------------|--------------|
| ![Starmer 0.36 bpp](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/processed/starmer_rate_036.png) | ![Starmer 0.68 bpp](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/processed/starmer_rate_068.png) | ![Starmer 0.96 bpp](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/processed/starmer_rate_096.png) |
| **Size**: 11,648 bytes | **Size**: 20,610 bytes | **Size**: 29,479 bytes |
| Small card image | PIV card output | More texture detail |

| **1.70 bpp** | **4.00 bpp** |
|--------------|--------------|
| ![Starmer 1.70 bpp](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/processed/starmer_rate_170.png) | ![Starmer 4.00 bpp](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/processed/starmer_rate_400.png) |
| **Size**: 49,765 bytes | **Size**: 82,111 bytes |
| High-detail comparison | Very high-detail comparison |

### Document Crop Comparison

FaceOFFx uses the same detected face geometry to render different document crops. The README keeps
this comparison compact so the PIV path remains the main example.

| PIV Card | ICAO Portrait | Canada PR Card |
|----------|---------------|----------------|
| ![Starmer PIV](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/processed/starmer_piv.png) | ![Starmer ICAO](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/processed/starmer_icao.png) | ![Starmer Canada PR](https://raw.githubusercontent.com/mistial-dev/FaceOFFx/master/docs/samples/processed/starmer_canada_pr.png) |
| 420×560, 20,610 bytes | 413×531, 54,493 bytes | 50mm × 70mm crop, 420px display preview |

### Understanding the Visualizations

- **Red Box**: ROI region with highest quality preservation
- **Blue Line (AA)**: Vertical center alignment
- **Green Line (BB)**: Horizontal eye line (should be 55-60% from bottom)
- **Purple Line (CC)**: Head width measurement used by PIV crop solving

### Head Width Measurement (Line CC)

The head width measurement is crucial for PIV compatibility but presents challenges with 68-point facial landmarks:

**What we measure**: The widest points of the face contour (landmarks 0-16), which represent the jawline from ear to
ear. We then create a level line at the average Y-position of these widest points.

**Why this approach**:

- The 68-point landmark model doesn't include true ear positions
- Using the widest jaw points provides a consistent measurement
- Leveling the line improves visual aesthetics while maintaining accurate width

**Limitations**:

- The measurement is typically lower than actual ear level
- True head width at the temples/ears may be wider
- This is a fundamental limitation of the 68-point model

**PIV Compatibility**: The current PIV solver targets the accepted card head-width band on a 420px output.
It tries 235px, 225px, 215px, and 210px candidates in order and keeps the first candidate that also satisfies
eye-line, margin, rotation, and inter-pupillary-distance constraints.

## API Reference

### Profile Encoder

```csharp
var profile = ProfileSpecifications.Piv;
var result = await encoder.ProcessAsync(imageData, profile);

if (result.IsFailure)
{
    Console.WriteLine(result.Error.Message);
    return;
}

await File.WriteAllBytesAsync("piv.jp2", result.Value.ImageData);
```

`ProfileEncodingResult` contains the encoded bytes plus the decisions made by the pipeline:

```csharp
var output = result.Value;
Console.WriteLine(output.Profile.Id);
Console.WriteLine(output.Encoding.FileSize);
Console.WriteLine(output.Encoding.CompressionRate);
Console.WriteLine(output.RotationDegrees);
Console.WriteLine(output.CandidateTraces.Count);
```

## Configuration

### Profile Specifications

Profiles are plain immutable domain records. A profile defines:

- face-selection requirements
- portrait dimensions and crop candidate ladder
- head-width, eye-line, margin, rotation, and IPD constraints
- JPEG 2000 ROI settings
- encoding goal

`ProfileSpecifications.Piv` uses a hard byte cap. The encoding solver tests a bounded, deterministic compression ladder and accepts the first candidate that fits, which is the highest-quality accepted candidate for that ladder.

Other document crops use the same canonical face geometry and their own immutable specifications. The compact crop comparison above shows where ICAO and Canada permanent resident card output differ from the PIV card render.

## CLI Usage

### Primary Commands

```bash
faceoffx piv photo.jpg
faceoffx us-passport photo.jpg
faceoffx us-permanent-resident photo.jpg
faceoffx canada-passport photo.jpg
faceoffx canada-permanent-resident photo.jpg
faceoffx canada-citizenship-grant photo.jpg
faceoffx canada-proof-of-citizenship photo.jpg
faceoffx documents
```

### Variants

```bash
# PIV card image only
faceoffx piv photo.jpg --variant digital

# PIV printed Zone 1F photo only
faceoffx piv photo.jpg --variant print

# U.S. passport digital upload
faceoffx us-passport photo.jpg --variant digital

# U.S. permanent resident digital upload
faceoffx us-permanent-resident photo.jpg --variant digital

# Canadian permanent resident digital upload
faceoffx canada-permanent-resident photo.jpg --variant digital

# Canadian proof of citizenship digital upload
faceoffx canada-proof-of-citizenship photo.jpg --variant digital
```

### Machine-Readable Output

```bash
faceoffx piv photo.jpg --json
faceoffx us-passport photo.jpg --json
faceoffx canada-passport photo.jpg --json
```

`--json` writes a clean JSON job summary to stdout. Rendered artifacts and the provenance file are written to the output directory.

### Provenance and Explanation

```bash
# Write outputs to a specific directory
faceoffx piv photo.jpg --output-dir ./out

# Show the cited clauses used by the workflow
faceoffx piv photo.jpg --explain
```

Each document command writes a provenance JSON file alongside the outputs. The provenance file records the selected document, variant, automated checks, manual checklist items, production defaults, and exact citations used by the workflow.

### Error Handling

```csharp
var result = await encoder.ProcessAsync(imageData, ProfileSpecifications.Piv);
if (result.IsFailure)
{
    Console.WriteLine($"Processing failed: [{result.Error.Code}] {result.Error.Message}");
    return;
}

Console.WriteLine($"Processed size: {result.Value.Encoding.FileSize} bytes");
if (result.Value.Encoding.TargetFileSize.HasValue)
{
    Console.WriteLine($"Target cap was: {result.Value.Encoding.TargetFileSize.Value}");
}
```

## Development

### Building from Source

```bash
# Clone the repository
git clone https://github.com/mistial-dev/FaceOFFx.git
cd FaceOFFx

# Build the solution
dotnet build

# Run tests
dotnet test

# Create NuGet package
dotnet pack --configuration Release
```

### Project Structure

```text
FaceOFFx/
├── src/
│   ├── FaceOFFx/                # Domain models and interfaces
│   ├── FaceOFFx.Infrastructure/ # ONNX implementations
│   ├── FaceOFFx.Models/         # Embedded ONNX models
│   └── FaceOFFx.Cli/           # Command-line interface
├── tests/                      # Unit and integration tests
└── docs/                       # Documentation and samples
```

## Technical Details

### PIV Compatibility (FIPS 201-3)

FaceOFFx ensures compatibility with government standards:

- **Output**: 420×560 pixels (3:4 aspect ratio)
- **Face Width**: PIV card head-width candidate solving for the accepted 210-240px band
- **Eye Position**: 55-60% from bottom of image
- **Rotation**: Maximum ±5° correction
- **Centering**: Face properly centered with margins

### JPEG 2000 ROI Encoding

The library uses advanced ROI (Region of Interest) encoding to optimize quality:

- **Single Facial Region** - Highest quality preservation for the complete facial area
- **Background** - Lower quality for non-facial areas
- **Smooth Transitions** - Level 3 default prevents harsh boundaries

## Neural Network Models

FaceOFFx uses two specialized ONNX models for facial processing, each optimized for specific tasks in the PIV compatibility pipeline.

### Face Detection Model (RetinaFace)

**File**: `FaceDetector.onnx` (104MB, stored with Git LFS)
**Architecture**: RetinaFace single-stage face detector
**Input**: 640×640×3 RGB image, normalized to [0,1]
**Output**: Face bounding boxes with confidence scores and 5 key facial points

The RetinaFace model performs the first stage of human detection and provides the coarse geometry
used to seed the rest of the pipeline:

- **Bounding boxes**: Precise face region coordinates
- **Confidence scores**: Detection confidence (typically >0.8 for processing)
- **5-point landmarks**: Eyes (2), nose tip (1), mouth corners (2)
- **Frontal face filtering**: Optimized for government ID photo orientations

**Pre-processing**: Images are resized to 640×640 with padding to maintain aspect ratio, then
converted into the tensor format expected by the detector.

**Post-processing**: Non-maximum suppression filters overlapping detections. The highest-confidence
usable face becomes the canonical face candidate for downstream chip extraction and fine landmark
solving.

### Landmark Detection Model (PFLD)

**File**: `landmarks_68_pfld.onnx` (2.8MB)
**Architecture**: PFLD (Practical Facial Landmark Detector)
**Input**: 112×112×3 RGB face crop, normalized to [0,1]
**Output**: 136 floats (68 landmarks × 2 coordinates)

The PFLD model extracts precise 68-point facial landmarks using the standard iBUG annotation scheme.
In FaceOFFx it is not run over the whole image. It is run over a normalized 112×112 chip derived
from the coarse RetinaFace solve, and the resulting fine landmarks are then projected back into the
original source-image coordinate system.

#### Why There Are Two Landmark Stages

- **Coarse stage**: RetinaFace gives a detection box plus 5 facial keypoints.
- **Canonical chip stage**: FaceOFFx uses those coarse landmarks to build a stable chip transform.
- **Fine stage**: PFLD runs on that canonical chip and returns precise 68-point landmarks.
- **Back-projection stage**: The chip transform is inverted so the fine landmarks line up with the
  original source image and with all later crop/render transforms.

This split is what the diagnostics visualizer shows in `10-coarse.png`, `20-chip-locate.png`,
`30-chip.png`, `40-fine-chip.png`, and `50-fine-source.png`.

#### Landmark Layout

- **Face outline** (0-16): Jawline from ear to ear
- **Right eyebrow** (17-21): Outer to inner points
- **Left eyebrow** (22-26): Inner to outer points
- **Nose bridge** (27-30): Top to bottom
- **Lower nose** (31-35): Nostrils and tip
- **Right eye** (36-41): Clockwise from outer corner
- **Left eye** (42-47): Clockwise from outer corner
- **Outer mouth** (48-59): Clockwise from left corner
- **Inner mouth** (60-67): Clockwise from left corner

**Coordinate System**: The PFLD output is normalized to the 112×112 chip input. FaceOFFx removes
any chip padding, rescales those points into chip space, and then maps them back into original
image coordinates.

**Precision**: The PFLD model achieves sub-pixel accuracy for facial feature localization, essential for precise PIV alignment and ROI calculation.

### Model Performance Characteristics

| Model      | Inference Time* | Memory Usage | Accuracy            |
|------------|-----------------|--------------|---------------------|
| RetinaFace | ~50ms           | ~200MB       | >95% face detection |
| PFLD       | ~15ms           | ~50MB        | <2px landmark error |

*CPU inference on modern Intel/AMD processors

### ONNX Models Table

| Model                    | Purpose            | Input Size | Framework  |
|--------------------------|--------------------|------------|------------|
| `FaceDetector.onnx`      | Face detection     | 640×640    | RetinaFace |
| `landmarks_68_pfld.onnx` | Landmark detection | 112×112    | PFLD       |

## Image Processing Pipeline

FaceOFFx follows a single profile-encoding pipeline to transform input images into profile-compliant JPEG 2000 files:

### 1. Image Loading and Validation

```
Input Image (any format) → ImageSharp Image<Rgba32>
```

- Supports JPEG, PNG, BMP, TIFF, and other common formats
- Converts to consistent RGBA32 format for processing
- Validates image dimensions and format compatibility

### 2. Coarse Detection Phase

```
Image<Rgba32> → RetinaFace Model → DetectedFace[]
```

- Resize image to 640×640 with aspect-preserving padding
- Convert the padded image into the detector tensor format
- Run ONNX inference to detect faces
- Decode bounding boxes, confidence, and 5-point landmarks
- Filter overlapping detections with non-maximum suppression
- Select the single best face for downstream processing

### 3. Canonical Chip Construction

```
DetectedFace + 5-point landmarks → Canonical chip transform → 112×112 chip
```

- Build a stable chip transform from the coarse face solve
- Extract a normalized 112×112 chip from the original image
- Preserve the forward and inverse transform so chip-space points can be mapped back to source
  space exactly

### 4. Fine Landmark Detection Phase

```
112×112 chip → PFLD Model → 68 chip-space landmarks
```

- Normalize to [0,1] for ONNX inference
- Extract 68-point facial landmarks
- Project the fine landmarks back into full image coordinates

### 5. Canonical Geometry Resolution

```
Coarse detection + chip transform + fine landmarks → CanonicalFaceGeometry
```

- Combine the original-space fine landmarks with the chip transform
- Preserve the exact relationship between source image, chip, and later portrait outputs
- Use this canonical geometry as the single source of truth for later render and validation steps

### 6. Portrait Transformation Sequence

```
CanonicalFaceGeometry + profile spec → PortraitPlanSolver → Rotate → Crop → Resize → output portrait
```

**Critical Order**: Rotation is applied to the full original image first to avoid black borders.
Cropping and resizing follow after the eye line and face placement are solved from the canonical
geometry.

#### Rotation Phase

- Rotate entire source image by calculated angle
- Use high-quality bicubic interpolation
- Maintain full image dimensions during rotation

#### Cropping Phase

- Calculate face position from the canonical original-space landmarks
- Apply the profile-specific crop candidate selected by `PortraitPlanSolver`
- Preserve the exact transform map from source coordinates to output coordinates

#### Resizing Phase

- Scale cropped region to exactly 420×560 pixels
- Use bicubic resampling for optimal quality
- Maintain aspect ratio through padding if needed

### 7. Landmark Transformation

```
Original Landmarks → Transform Matrix → PIV Space Landmarks
```

- Apply same rotation, crop, and scale transforms to landmarks
- Ensure landmarks align with transformed face position
- Validate eye positions and head placement against the requested standard

### 8. ROI Region Calculation

```
PIV Landmarks → Facial Region Analysis → ROI Bounds
```

- Calculate inner facial region encompassing key features
- Include eyes, eyebrows, nose, mouth, and surrounding area
- Apply 1% padding around detected facial features
- Generate rectangular ROI bounds for JPEG 2000 encoding

### 9. JPEG 2000 Encoding with ROI

```
Rendered profile portrait + ROI → EncodingPlanSolver → CoreJ2K → JP2 File
```

- **Single tile encoding**: Use one 420×560 tile for optimal compression
- **ROI priority**: Encode facial region at higher quality (levels 0-3)
- **Background compression**: Apply base compression rate to non-ROI areas
- **PIV encoding goal**: Hard-cap solving for the card facial image

### Processing Flow Diagram

```
Input Image
    ↓
Coarse Detection (RetinaFace 640×640)
    ↓
Canonical Chip Construction (112×112)
    ↓
Fine Landmark Detection (PFLD on chip)
    ↓
Back-Projection To Source Coordinates
    ↓
Canonical Face Geometry
    ↓
Image Transformation (Rotate → Crop → Resize)
    ↓
Landmark Transformation (Match image transforms)
    ↓
ROI Calculation (Facial region bounds)
    ↓
Profile Encoding Solver (Single tile + ROI)
    ↓
Profile-Compatible JP2 Output
```

### Coordinate System Transformations

The pipeline involves multiple coordinate spaces:

1. **Original Image Space**: Source image dimensions (e.g., 1920×1080)
2. **Detection Tensor Space**: 640×640 detector input
3. **Chip Space**: Canonical 112×112 chip coordinates
4. **Fine Landmark Space**: Normalized landmark coordinates relative to the chip
5. **Rotated Image Space**: Original dimensions after rotation
6. **Output Portrait Space**: Final rendered dimensions such as 420×560

Each transform is preserved explicitly so facial features can be mapped forward and backward without
re-solving detection or landmarks. The diagnostics command exists to make those stage boundaries
visible on real inputs.

## Requirements

- **.NET 8.0, 9.0, or 10.0**
- **Dependencies**:
  - Microsoft.ML.OnnxRuntime (CPU inference)
  - SixLabors.ImageSharp (Image processing)
  - CoreJ2K (JPEG 2000 encoding)
  - CSharpFunctionalExtensions (Error handling)

## Contributing

Contributions are welcome! Please read our [Contributing Guide](docs/CONTRIBUTING.md) for details on our code of conduct
and the process for submitting pull requests.

## Security and Supply Chain

### Software Bill of Materials (SBOM)

A complete Software Bill of Materials is available in [sbom/faceoffx-sbom.json](sbom/faceoffx-sbom.json) in CycloneDX
format. This includes:

- All direct and transitive dependencies
- License information for each component
- Version information and checksums

### Security Policy

For security vulnerabilities, please see our [Security Policy](SECURITY.md).

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments & Credits

### Models and Software Used

| Component                      | Description                                                 | License      | Source/Credit                                                                                   |
|--------------------------------|-------------------------------------------------------------|--------------|-------------------------------------------------------------------------------------------------|
| **FaceONNX**                   | Base facial processing library this project is derived from | MIT          | [FaceONNX/FaceONNX](https://github.com/FaceONNX/FaceONNX)                                       |
| **RetinaFace**                 | Face detection model (FaceDetector.onnx)                    | MIT          | [discipleofhamilton/RetinaFace](https://github.com/discipleofhamilton/RetinaFace)               |
| **PFLD**                       | 68-point facial landmark detection (landmarks_68_pfld.onnx) | MIT          | [FaceONNX/FaceONNX.Models](https://github.com/FaceONNX/FaceONNX.Models)                         |
| **ONNX Runtime**               | High-performance inference engine                           | MIT          | [Microsoft/onnxruntime](https://github.com/microsoft/onnxruntime)                               |
| **ImageSharp**                 | Cross-platform 2D graphics library                          | Apache-2.0   | [SixLabors/ImageSharp](https://github.com/SixLabors/ImageSharp)                                 |
| **CoreJ2K**                    | JPEG 2000 encoding with ROI support                         | BSD-2-Clause | [cinderblocks/CoreJ2K](https://github.com/cinderblocks/CoreJ2K)                                 |
| **CSharpFunctionalExtensions** | Functional programming extensions                           | MIT          | [vkhorikov/CSharpFunctionalExtensions](https://github.com/vkhorikov/CSharpFunctionalExtensions) |
| **Spectre.Console**            | Beautiful console applications                              | MIT          | [spectreconsole/spectre.console](https://github.com/spectreconsole/spectre.console)             |

### Standards and Specifications

| Standard            | Description                                                 | Organization |
|---------------------|-------------------------------------------------------------|--------------|
| **FIPS 201-3**      | Personal Identity Verification (PIV) Requirements           | NIST         |
| **INCITS 385-2004** | Face Recognition Format for Data Interchange                | ANSI/INCITS  |
| **SP 800-76-2**     | Biometric Specifications for Personal Identity Verification | NIST         |

### Special Thanks

- **FaceONNX** - This project is derived from FaceONNX, which provides the foundational facial processing capabilities
  and model infrastructure
- The **68-point facial landmark** annotation scheme was originally developed by the iBUG group at Imperial College
  London

### Quote

> "Face... off... No more drugs for that man!" - [Watch Scene](https://www.youtube.com/watch?v=3bdv8MjwzxA)

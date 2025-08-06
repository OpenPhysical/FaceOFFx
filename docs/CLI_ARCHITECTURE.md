# Universal FaceOFFx CLI Architecture

## Overview

FaceOFFx transforms from a specialized PIV compliance tool into a universal facial image processing platform that serves photographers, compliance officers, researchers, and developers with equal excellence.

## Core Design Principles

### 1. Multi-Modal Use Cases
- **Government/Enterprise**: PIV, TWIC, ICAO, CAC compliance
- **Photography**: Portrait enhancement, headshot optimization  
- **Analysis**: Quality assessment without processing
- **Research**: Biometric analysis, dataset processing
- **Custom**: Organization-specific compliance rules

### 2. Progressive Disclosure
```bash
# Simple: Just works with defaults
faceoffx piv photo.jpg

# Intermediate: Common options visible  
faceoffx piv photo.jpg --quality strict --format jpeg

# Advanced: Full control for experts
faceoffx piv photo.jpg \
  --min-symmetry 60 \
  --min-sharpness 50 \
  --roi-level 2 \
  --rate 0.8 \
  --custom-standard my-org.json
```

### 3. Railway-Oriented Programming
- All operations return `Result<T>` - NO exceptions
- Fail-fast with meaningful error messages
- Graceful degradation when possible
- Composable pipeline operations

## Command Structure

### Primary Use Case Commands

#### Government/Enterprise ID Processing
```bash
faceoffx piv input.jpg                    # PIV compliance + encoding
faceoffx twic input.jpg                   # TWIC compliance + encoding  
faceoffx icao input.jpg                   # ICAO passport compliance
faceoffx cac input.jpg                    # CAC military ID compliance
```

#### General Purpose Photo Processing
```bash
faceoffx photo input.jpg                  # General facial photo enhancement
faceoffx portrait input.jpg               # Portrait photography optimization
faceoffx headshot input.jpg               # Professional headshot processing
```

#### Analysis Only (No Encoding)
```bash
faceoffx analyze input.jpg                # Full quality + biometric analysis
faceoffx validate input.jpg               # Quick pass/fail compliance check
faceoffx inspect input.jpg                # Detailed diagnostic report
```

### Workflow-Specific Commands

#### Batch Operations
```bash
faceoffx batch ./photos --standard piv    # Batch process directory
faceoffx review ./results                 # Human review mode for batch results
faceoffx compare before.jpg after.jpg     # Side-by-side quality comparison
```

#### Interactive Modes
```bash
faceoffx guide                            # Step-by-step guided processing
faceoffx train                            # Educational mode with explanations
faceoffx demo                             # Demonstration mode with sample images
```

## Input/Output Flexibility

### Input Sources
```bash
faceoffx piv photo.jpg                    # Single file
faceoffx piv *.jpg                        # Glob patterns
faceoffx piv --stdin                      # Base64 input from stdin
faceoffx piv --url https://...            # Download from URL
faceoffx piv --camera                     # Live camera capture
faceoffx piv --clipboard                  # From clipboard image

# Pre-cropped Images
faceoffx piv cropped.jpg --pre-cropped    # Skip face detection
faceoffx piv face.jpg --face-only         # Image is already face region
```

### Output Formats
```bash
faceoffx piv input.jpg --format jp2       # JPEG 2000 (default)
faceoffx piv input.jpg --format jpeg      # Standard JPEG
faceoffx piv input.jpg --format png       # PNG
faceoffx piv input.jpg --format tiff      # TIFF

# Output Destinations
faceoffx piv input.jpg --output result.jp2   # Specific file
faceoffx piv input.jpg --stdout               # Base64 to stdout
faceoffx piv input.jpg --directory ./out      # Auto-named in directory
faceoffx piv input.jpg --clipboard           # Copy to clipboard
```

## Validation-Only Workflows

### Quick Compliance Check
```bash
faceoffx validate photo.jpg --standard piv
# Output: ✅ PIV COMPLIANT or ❌ REJECTED: 3 violations
```

### Detailed Analysis Report
```bash
faceoffx analyze photo.jpg --report detailed
# Output: Full JSON report + visual overlays + recommendations
```

### Batch Validation (No Processing)
```bash
faceoffx validate *.jpg --standard icao --summary
# Output: CSV summary of all files with pass/fail status
```

## Advanced User Scenarios

### Photography Studios
```bash
# Professional portrait workflow
faceoffx portrait session/*.jpg \
  --enhance-lighting \
  --skin-smoothing \
  --eye-enhancement \
  --batch-report

# Wedding photography batch
faceoffx photo wedding/*.jpg \
  --face-detection-only \
  --export-landmarks \
  --quality-report
```

### Security/Law Enforcement
```bash
# Forensic quality analysis
faceoffx analyze evidence.jpg \
  --forensic-mode \
  --detailed-report \
  --metadata-preservation

# Surveillance image enhancement
faceoffx enhance cctv.jpg \
  --low-light-boost \
  --noise-reduction \
  --face-enhancement
```

### Academic/Research
```bash
# Research dataset analysis
faceoffx research dataset/*.jpg \
  --export-features \
  --statistical-analysis \
  --csv-export

# Biometric study
faceoffx study faces/*.jpg \
  --landmark-precision \
  --symmetry-analysis \
  --demographic-stats
```

## Human-in-the-Loop Excellence

### Interactive Review Modes
```bash
# Guided compliance improvement
faceoffx improve photo.jpg
# Shows: "❌ Too blurry (12%) → Try these camera settings..."
# Shows: "❌ Off-center (15px left) → Crop suggestion: [visual guide]"

# Side-by-side comparison
faceoffx compare original.jpg processed.jpg --interactive
# Shows quality metrics, allows fine-tuning

# Batch review with manual override
faceoffx batch review ./results --interactive
# Human can approve/reject/modify each result
```

### Educational Modes
```bash
# Learn mode for photographers
faceoffx learn photo.jpg
# Explains: "Gabor filters detect texture asymmetry..."
# Shows: Visual examples of good vs bad symmetry

# Training mode for compliance officers
faceoffx train compliance
# Interactive tutorial on ID photo requirements
# Practice mode with sample images
```

## Integration & Extensibility

### API Integration
```bash
# Server mode for web integration
faceoffx serve --port 8080 --api-key xxx
# RESTful API for web applications

# Webhook integration
faceoffx batch *.jpg --webhook https://my-app.com/processed
# Posts results to webhook URL

# Database integration
faceoffx batch *.jpg --database postgres://... --table results
# Stores results directly in database
```

### Custom Standards
```bash
# Organization-specific compliance
faceoffx custom photo.jpg --standard ./my-org-rules.json

# Industry-specific presets
faceoffx healthcare patient.jpg     # HIPAA-compliant processing
faceoffx banking customer.jpg       # KYC compliance
faceoffx aviation pilot.jpg         # FAA requirements
```

## Contextual Help System

### Smart Help Based on Use Case
```bash
faceoffx piv --help                    # PIV-specific options
faceoffx photo --help                  # Photography options
faceoffx research --help               # Research/academic options
```

### Example-Driven Help
```bash
faceoffx examples piv                  # Show PIV use case examples
faceoffx examples batch                # Show batch processing examples
faceoffx examples troubleshooting      # Common problem solutions
```

### Intelligent Defaults
```bash
# Auto-detect optimal settings based on input
faceoffx auto photo.jpg                # Analyzes image, picks best workflow
faceoffx smart batch *.jpg             # Auto-categorizes images, applies appropriate processing

# Context-aware processing
faceoffx piv low-quality.jpg           # Automatically applies enhancement
faceoffx icao high-res.jpg             # Automatically optimizes for passport requirements
```

## Complete Command Tree

```
faceoffx
├── Standards-Based Processing
│   ├── piv, twic, icao, cac         # Government ID compliance
│   ├── photo, portrait, headshot    # General photography
│   └── custom --standard file.json  # Organization-specific
│
├── Analysis-Only Workflows  
│   ├── validate                     # Quick pass/fail
│   ├── analyze                      # Detailed report
│   ├── inspect                      # Diagnostic mode
│   └── compare                      # Side-by-side analysis
│
├── Batch Operations
│   ├── batch                        # Process directories
│   ├── review                       # Human review mode
│   └── monitor                      # Watch directory mode
│
├── Interactive Modes
│   ├── guide                        # Step-by-step workflow
│   ├── improve                      # Compliance coaching
│   ├── train                        # Educational mode
│   └── demo                         # Demonstration mode
│
└── Utility Commands
    ├── examples                     # Use case examples
    ├── serve                        # API server mode
    └── config                       # Configuration management
```

## Benefits

1. **Universal Appeal**: Serves photographers, compliance officers, researchers, developers
2. **Progressive Disclosure**: Simple defaults, advanced control when needed
3. **Contextual Intelligence**: Smart defaults based on use case
4. **Human-Centered**: Interactive guidance and educational modes
5. **Enterprise Ready**: Batch processing, custom standards, API integration
6. **Bulletproof**: Railway-oriented programming with no exceptions
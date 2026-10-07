# FaceOFFx quality system

FaceOFFx retains symmetry, sharpness and landmark geometry measurements as engineering signals supporting PIV image review. The PIV encoding pipeline separately records source-color, native crop, anatomical geometry, regional compression and container-budget evidence.

## Measurements and acceptance

`QualityAssessmentPipeline.AssessAsync(image, face, landmarks, options)` returns a typed `Iso19794Assessment`. It combines symmetry, regional sharpness, geometry, normalized quality scores and individual violations. Assessors can run sequentially or in parallel.

`QualityAssessmentOptions.ForStandard("piv")` selects the supported scoring profile. `Strict`, `Lenient`, and an explicit `MinQualityThreshold` define engineering acceptance policy. `QualityAcceptanceEvaluator` evaluates those options against the assessment.

Score percentages and configured thresholds describe this implementation's quality signals. NIST/INCITS obligations use the dedicated geometry and encoding evidence, with acquisition measurements supplied by the issuing workflow.

## Useful review signals

- Symmetry compares landmark-aligned facial regions and Gabor responses for pose/illumination asymmetry.
- Sharpness measures frequency detail and regional scores; controlled blur inputs test sensitivity and ordering.
- Geometry reports landmark-derived head, eye, centering and framing estimates.
- Backdrop diagnostics accept an explicit retained-head mask/envelope and protect below-chin foreground. Border-connected low-texture regions remain estimated candidates, with unknown labels elsewhere.

The backdrop analyzer reports RGB pixel means, variance, quantized entropy, neighbor gradients, edge density and defined/undefined correlations. Actual JPEG 2000 component and subband evidence comes from the committed codec telemetry.

## PIV pipeline evidence

The normal encoder prepares sRGB samples, uses source-space landmarks, preserves a full-head crop, and builds one fixed face-centered mask. Frame width, source support and uniform scale are checked during planning. True ear-attachment CC and crown DD have explicit measurement bases; jaw and crown proxies retain estimated status.

The fixed region is constructed before balanced allocation, and the complete JP2 cap controls automated encoding acceptance. Regional payload attribution, source capture, pose/expression, anatomical boundary review and the signed object remain issuer verification requirements.

Use [API](API.md) and [CLI architecture](CLI_ARCHITECTURE.md) for product usage, [quality diagnostics](quality-assessment.md) for measurements, and [compression accounting](PIV-COMPRESSION-ACCOUNTING.md) for the regional formula.

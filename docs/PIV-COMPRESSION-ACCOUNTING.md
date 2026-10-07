# PIV compression accounting

FaceOFFx prepares Full Frontal PIV portraits with one face-centered Part 1 Maxshift region.
The balanced baseline uses ROI start level 4, 64×64 code blocks, one tile, one quality layer,
the irreversible 9/7 transform and ICT. Every luminance subband, including LL, has
squared-error utility 1.25; chroma utility is 1. The complete JP2 is bounded by the selected
card-size profile.

Regional compression is measured with FaceOFFx's versioned
`synthesis-energy-decoder-effective-pass-v3` convention. It assigns actual emitted code-block bytes
to the face according to spatial synthesis influence and coding benefit. The convention
provides a reproducible engineering denominator for regional compression review.

## Regional byte arithmetic

For a fixed mask containing `A` pixels in the stored eight-bit, three-component RGB image,
the raw region contains `3*A` bytes. A 24:1 comparison corresponds to `ceil(3*A/24)`
attributed bytes under the chosen method. The estimate is
`3*A / attributedFacePayloadBytes`. The balanced encoder enforces the complete JP2
ceiling and records this estimate for issuer review.

The anatomical mask is constructed independently of the encoding budget. Framing changes
must preserve native source support, required anatomical resolution and the selected facial
coverage. Retain the mask, source and crop measurements with the encoding evidence.

The default `LandmarkFace` region contains the convex hull of all 68 detected facial landmarks,
covering brows, eyes, nose, mouth and jaw, with a fixed localization margin of 3% of jaw width.
The region defines its feature coverage before allocation. Full Frontal framing and optical
head-resolution review apply separately. NIST SP 800-76-2 Table 12 Note 6 permits a face-centered innermost region;
INCITS 385 Annex B.4.2 illustrates one full-image region definition.

## Shared payload measurement

JPEG 2000 inverse-wavelet coefficients can affect face pixels and surrounding pixels.
ISO/IEC 15444-1 Annex H.3.5 permits promoting complete subbands to deliver coarse image
information early. Balanced allocation uses those shared coarse bands to retain face
structure, color and outer detail.

For each coefficient `k`, synthesize its finite 9/7 impulse response with the actual image
geometry, sample parity and boundary extension. Define its spatial face influence as:

```text
a[k] = sum of squared synthesis response inside the face mask
       / sum of squared synthesis response over the image
```

For each emitted positive-rate coding-pass interval `j`, use the positive coefficient
distortion-reduction contributions `D[j,k]` from the Tier1 lookup model. Undo Maxshift,
whole-band promotion and user allocation-utility scaling in these contributions. Set the
gain to zero for ordinary ROI refinement below the decoder-retained integer bit planes;
the Maxshift decoder discards those fractional ROI bits. Background refinement and
normally decoded promoted bands retain their modeled gain. Define:

```text
faceShare[j] = sum(D[j,k] * a[k]) / sum(D[j,k])
faceEstimate = sum(emittedBytes[j] * faceShare[j])
facePayloadBytes = floor(faceEstimate)
outsidePayloadBytes = packetBodyBytes - facePayloadBytes
```

An interval with zero positive coding benefit receives zero face credit. Compute estimates
only for the committed retained prefixes. Rejected allocation candidates and packet
simulations contribute zero bytes. Aggregate fractional face credit before rounding down.

Construct the intervals from byte endpoints independently of allocation utility. Starting
at the committed terminal pass, walk backward and retain an earlier positive endpoint only
when its byte length is strictly smaller than the next retained length. Reverse these
endpoints to obtain increasing intervals. This rule folds equal and nonmonotonic length
estimates deterministically. V2 introduced this byte-only partition, and V3 limits its
benefit model to decoder-retained ROI information. Historical V1 studies used the
allocator's retained distortion-hull partitions.

This convention follows the encoder's diagonal distortion model. Its assumptions are
positive per-coefficient lookup gains and squared synthesis influence; coefficient cross
terms and reconstruction-midpoint effects are represented by that model's approximation.
The assigned bytes express coding benefit under this convention. Record the method version
with every reported regional ratio.

The method has three useful properties: face and outside payload reconcile exactly with
emitted packet-body bytes; shared bands receive spatially computed face credit; and the
measurement formula stays fixed across perceptual weighting experiments. Changing luma
utility can change selected coding passes, while the accounting convention remains the same.

## Headers and byte reconciliation

Regional credit uses code-block payload. Keep packet headers, main and tile headers, the
two-byte end-of-codestream marker and JP2 boxes in separate accounts:

```text
complete JP2 = face payload + outside payload
             + packet headers + main and tile headers + EOC + JP2 boxes
```

Retain per-component and per-subband rows for both allocation categories and regional
credit. They show which frequency bands spend bytes and how much face credit each contributes.
Validate every row and the complete output before accepting a ratio.

The fixed production recipe uses balanced allocation within the JP2 cap. The face ledger
is measured from committed bytes after allocation, retaining the complete fixed mask.
The issuer reviews its regional meaning and the required compression limit.

## Balanced allocation

The encoder optimizes its balanced rate-distortion objective with fixed luma utility1.25
and simulates actual packets against the complete JP2 cap. Face credit is measured after
selection. Historical constrained-allocation studies retain their original masks, method
versions and outcome records separately from the fixed product recipe.

Framing candidates preserve native source support and retain head-resolution evidence.
Byte-target changes keep the face-region construction independent of the budget.

## Card object size

The minimum profile reserves 11,820 bytes for JP2: 12,704 biometric-value bytes minus
46 facial-record bytes, 88 CBEFF bytes and 750 issuer-signature bytes. Six outer-tag bytes
bring the configured object maximum to 12,710 bytes. The signature allowance is an issuer
configuration value. Validate actual FAC, CBEFF, signature and mandatory-tag lengths when
serializing the signed card object.

The preferred profile allows 22,000 JP2 bytes with a correspondingly larger configured
card container. Select a capacity supported by the credential being issued.

## Source color preparation

Prepare source color before face detection and rendering. Embedded ICC profiles take
precedence. The supported converter handles bounded v2/v4 RGB matrix and tone-curve
profiles with XYZ D50 connection space, using relative-colorimetric conversion to sRGB.
Its parametric curves follow the corrected ICC.1:2022 Table 68 equations. Profiles requiring
LUT or multi-process conversion receive an actionable request for a color-managed sRGB
export. Numeric matrix and curve checks identify embedded sRGB profiles.

For sources without an ICC profile, EXIF ColorSpace 1 provides a recorded sRGB basis;
otherwise the automatic sRGB interpretation is recorded as `AssumedSrgb`.
Retain the source-profile hash, preparation method and acquisition-review requirements
with the output. Caller-owned images are cloned before color conversion and orientation
normalization.

## Comparison and enrollment evidence

Gallery comparisons freeze the watermarked source and fixed recipe while changing only
the file-size target. Decode the actual JP2 independently, inspect native face and outer
detail, compare intended printed-credential readability and retain recognition results.
Record the method estimate beside actual bytes and review requirements.

Capture evidence establishes optical resolution, anatomical head dimensions, pose and
camera-profile conversion to sRGB. Encoding evidence establishes the byte cap, JPEG 2000
profile and regional estimate under the recorded FaceOFFx method. Issuance evidence establishes
the complete signed object and enrollment procedure.

NIST SP 800-76-2 Table 12 Notes 4–8 specify Full Frontal images, compression, optical
resolution and sRGB conversion. NIST SP 800-85B AS05.03.05 specifies the face-centered
on-card 24:1 limit, and VE05.03.05.01 requires enrollment and retention documentation.
INCITS 385-2004 Annex B.4 illustrates face-based inner and outer budgets. FaceOFFx's shared
payload formula supplies a documented engineering convention; the enrollment workflow
records acceptance of that convention and completes the normative regional review.

Sources: [NIST SP 800-76-2](https://nvlpubs.nist.gov/nistpubs/SpecialPublications/NIST.SP.800-76-2.pdf),
[NIST SP 800-85B](https://nvlpubs.nist.gov/nistpubs/legacy/sp/nistspecialpublication800-85b.pdf),
ISO/IEC 15444-1:2000 Annex H.3 and J.12, ANSI INCITS 385-2004 Annex B.4, and NIST SP 800-73-5 Table 14.

Color conversion references: [ICC.1:2022](https://www.color.org/specification/ICC.1-2022-05.pdf)
sections 8.10 and 10.18, and the [ICC sRGB registry](https://registry.color.org/rgb-registry/srgbprofiles).

using CSharpFunctionalExtensions;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>
/// Immutable face-selection requirements for a profile.
/// </summary>
[PublicAPI]
public sealed record FaceSelectionSpecification(
    float MinimumFaceConfidence,
    bool RequireSingleFace,
    int MinimumFaceSizePixels);

/// <summary>
/// Immutable portrait geometry requirements for a profile.
/// </summary>
[PublicAPI]
public sealed record PortraitSpecification(
    ImageDimensions OutputDimensions,
    IReadOnlyList<float> HeadWidthCandidateRatios,
    float MinimumHeadWidthRatio,
    float MaximumHeadWidthRatio,
    float TargetEyeLineFromTopRatio,
    float MaximumRotationDegrees,
    int MinimumInterPupillaryDistance,
    int MinimumTopMargin,
    int MinimumSideMargin)
{
    /// <summary>Apply PIV native-resolution, full-head geometry, and source-support checks.</summary>
    public bool EnforcePivGeometry { get; init; }
    /// <summary>Defines the face-centered protected region before encoding or byte measurement.</summary>
    public PivFaceRegion FaceRegion { get; init; } = PivFaceRegion.LandmarkFace;
}

/// <summary>
/// Base type for profile encoding goals.
/// </summary>
[PublicAPI]
public abstract record EncodingGoal;

/// <summary>
/// Named file-size target for profile encoding.
/// </summary>
[PublicAPI]
public sealed record ProfileFileSizeTarget(
    string ProfileId,
    string Id,
    string DisplayName,
    int MaxBytes,
    string Description)
{
    /// <summary>Issuer and container reservations used to derive this JP2 allowance.</summary>
    public PivCardImageBudget? CardBudget { get; init; }
}

/// <summary>
/// Hard-cap encoding goal.
/// </summary>
[PublicAPI]
public sealed record MaxFileSizeGoal(int MaxBytes) : EncodingGoal;

/// <summary>
/// Named hard-cap encoding goal.
/// </summary>
[PublicAPI]
public sealed record NamedFileSizeGoal(ProfileFileSizeTarget Target) : EncodingGoal;

/// <summary>Fixed balanced JP2 media type and complete-image byte target.</summary>
[PublicAPI]
public sealed record EncodingSpecification(
    string MimeType,
    EncodingGoal Goal);

/// <summary>
/// Complete immutable profile definition.
/// </summary>
[PublicAPI]
public sealed record ProfileSpecification(
    string Id,
    string DisplayName,
    FaceSelectionSpecification FaceSelection,
    PortraitSpecification Portrait,
    EncodingSpecification Encoding)
{
    /// <summary>Derives a PIV image allowance from the configured card and issuer reservations.</summary>
    public Result<ProfileSpecification, PipelineError> WithCardImageBudget(PivCardImageBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        return string.Equals(Id, "piv", StringComparison.OrdinalIgnoreCase)
            ? WithFileSizeTarget(new ProfileFileSizeTarget("piv", "issuer-budget", "Issuer Card Image Budget",
                budget.MaximumJpeg2000Bytes, "Card capacity and issuer signature reservation.") { CardBudget = budget })
            : Result.Failure<ProfileSpecification, PipelineError>(new ConfigurationError("An issuer card-image budget requires the PIV profile.", Id));
    }

    /// <summary>
    /// Returns this profile with the supplied named file-size target applied.
    /// </summary>
    public Result<ProfileSpecification, PipelineError> WithFileSizeTarget(ProfileFileSizeTarget target) =>
        string.Equals(Id, target.ProfileId, StringComparison.OrdinalIgnoreCase)
            ? Result.Success<ProfileSpecification, PipelineError>(
                this with { Encoding = Encoding with { Goal = new NamedFileSizeGoal(target) } })
            : Result.Failure<ProfileSpecification, PipelineError>(
                new ConfigurationError(
                    $"File size target '{target.Id}' is for profile '{target.ProfileId}', not '{Id}'.",
                    Id));
}

/// <summary>
/// Built-in named file-size targets for profile encoding.
/// </summary>
[PublicAPI]
public static class ProfileFileSizeTargets
{
    /// <summary>Preferred higher-quality PIV card image target.</summary>
    public static ProfileFileSizeTarget PivPreferred { get; } = new(
        "piv",
        "preferred",
        "Preferred PIV Card Image",
        22_000,
        "Higher-quality PIV card image target for systems with larger facial-image containers.")
    {
        CardBudget = new PivCardImageBudget(maximumBiometricValueBytes: 22_884, maximumObjectBytes: 22_890)
    };

    /// <summary>Minimum-capacity PIV card image target with room for wrapping overhead.</summary>
    public static ProfileFileSizeTarget PivMinimum { get; } = new(
        "piv",
        "minimum",
        "Minimum PIV Card Image",
        new PivCardImageBudget().MaximumJpeg2000Bytes,
        "Minimum-capacity target reserving FAC, CBEFF and 750 issuer-signature bytes within the 12,704-byte biometric value and 12,710-byte object.")
    {
        CardBudget = new PivCardImageBudget()
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, ProfileFileSizeTarget>> Targets =
        new Dictionary<string, IReadOnlyDictionary<string, ProfileFileSizeTarget>>(StringComparer.OrdinalIgnoreCase)
        {
            ["piv"] = new Dictionary<string, ProfileFileSizeTarget>(StringComparer.OrdinalIgnoreCase)
            {
                [PivPreferred.Id] = PivPreferred,
                [PivMinimum.Id] = PivMinimum
            }
        };

    /// <summary>
    /// Resolves a named target for a profile. Unknown or blank target identifiers fail.
    /// </summary>
    public static Result<ProfileFileSizeTarget, PipelineError> ResolveForProfile(string profileId, string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId))
        {
            return Result.Failure<ProfileFileSizeTarget, PipelineError>(
                new ConfigurationError("File size target id is required.", profileId));
        }

        if (!Targets.TryGetValue(profileId, out var profileTargets))
        {
            return Result.Failure<ProfileFileSizeTarget, PipelineError>(
                new ConfigurationError($"Profile '{profileId}' does not define file size targets.", profileId));
        }

        return profileTargets.TryGetValue(targetId, out var target)
            ? Result.Success<ProfileFileSizeTarget, PipelineError>(target)
            : Result.Failure<ProfileFileSizeTarget, PipelineError>(
                new ConfigurationError(
                    $"Unknown file size target '{targetId}' for profile '{profileId}'. Valid targets: {string.Join(", ", profileTargets.Keys)}.",
                    profileId));
    }
}

/// <summary>
/// Built-in profile specifications.
/// </summary>
[PublicAPI]
public static class ProfileSpecifications
{
    private static readonly FaceSelectionSpecification DefaultSingleFaceSelection = new(
        MinimumFaceConfidence: 0.8f,
        RequireSingleFace: true,
        MinimumFaceSizePixels: 50);

    private static readonly PortraitSpecification CardPortrait = new(
        new ImageDimensions(480, 640),
        new[] { 225f / 420f, 215f / 420f, 235f / 420f, 210f / 420f },
        MinimumHeadWidthRatio: 210f / 420f,
        MaximumHeadWidthRatio: 240f / 420f,
        TargetEyeLineFromTopRatio: 0.40f,
        MaximumRotationDegrees: 5f,
        MinimumInterPupillaryDistance: 90,
        MinimumTopMargin: 24,
        MinimumSideMargin: 12)
    { EnforcePivGeometry = true, FaceRegion = PivFaceRegion.LandmarkFace };

    /// <summary>Federal PIV card candidate using balanced ROI allocation and explicit regional verification.</summary>
    public static ProfileSpecification Piv { get; } = new(
        "piv",
        "PIV",
        DefaultSingleFaceSelection,
        CardPortrait,
        new EncodingSpecification("image/jp2", new NamedFileSizeGoal(ProfileFileSizeTargets.PivMinimum)));
}

/// <summary>
/// Candidate evaluation trace for portrait solving.
/// </summary>
[PublicAPI]
public sealed record CandidateTrace(
    float RequestedHeadWidthRatio,
    Maybe<float> AcceptedHeadWidthRatio,
    string Summary);

/// <summary>
/// Resolved portrait plan.
/// </summary>
[PublicAPI]
public sealed record PortraitPlan(
    ImageDimensions OutputDimensions,
    ImageDimensions RotatedSourceDimensions,
    Rectangle CropRectangle,
    float RotationDegrees,
    float AcceptedHeadWidthRatio,
    float AcceptedEyeLineFromTopRatio,
    RenderTransformMap TransformMap,
    IReadOnlyList<CandidateTrace> CandidateTraces)
{
    /// <summary>Source/output PIV candidate measurements and remaining verification requirements.</summary>
    public PivGeometryEvidence? GeometryEvidence { get; init; }
    /// <summary>Fixed face-region definition selected for this source-supported crop.</summary>
    public PivFaceRegion FaceRegion { get; init; } = PivFaceRegion.LandmarkFace;
}

/// <summary>
/// Rendered portrait plus transformed landmarks and ROI.
/// </summary>
[PublicAPI]
public sealed record RenderedPortrait(
    Image<Rgba32> Image,
    FaceLandmarks68 Landmarks,
    FacialRoiSet RoiSet,
    PortraitPlan Plan);

/// <summary>
/// Encoding outcome details.
/// </summary>
[PublicAPI]
public sealed record EncodingDecision(
    float CompressionRate,
    int FileSize,
    Maybe<int> TargetFileSize,
    IReadOnlyList<float> AttemptedRates,
    Maybe<string> TargetFileSizeId = default)
{
    /// <summary>Measured allocation and protected-payload counters.</summary>
    public Jpeg2000EncodingResult? CodecEvidence { get; init; }
    /// <summary>Configured issuer and container reservations.</summary>
    public PivCardImageBudget? CardBudget { get; init; }
    /// <summary>Regional compression arithmetic and its external attribution requirement for a PIV card candidate.</summary>
    public RegionalCompressionVerification? RegionalCompressionVerification { get; init; }
}

/// <summary>
/// Final encoded profile artifact.
/// </summary>
[PublicAPI]
public sealed record ProfileEncodingResult(
    byte[] ImageData,
    string MimeType,
    ProfileSpecification Profile,
    ImageDimensions OutputDimensions,
    FaceLandmarks68 OutputLandmarks,
    float RotationDegrees,
    float FaceConfidence,
    EncodingDecision Encoding,
    IReadOnlyList<CandidateTrace> CandidateTraces)
{
    /// <summary>Recorded source color interpretation or profile conversion to sRGB.</summary>
    public PivSourceColorEvidence? SourceColorEvidence { get; init; }
    /// <summary>Recorded PIV candidate geometry and remaining verification requirements.</summary>
    public PivGeometryEvidence? GeometryEvidence { get; init; }
    /// <summary>Defined facial-mask construction and remaining anatomical coverage review.</summary>
    public FacialRoiCoverage? RoiCoverage { get; init; }
}

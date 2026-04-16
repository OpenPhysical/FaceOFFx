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
    int MinimumSideMargin);

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
    string Description);

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

/// <summary>
/// Single-rate encoding goal.
/// </summary>
[PublicAPI]
public sealed record ExplicitRateGoal(float BitsPerPixel) : EncodingGoal;

/// <summary>
/// Immutable encoding requirements for a profile.
/// </summary>
[PublicAPI]
public sealed record EncodingSpecification(
    string MimeType,
    bool EnableRoi,
    int RoiStartLevel,
    bool RoiAlign,
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
        "Higher-quality PIV card image target for systems with larger facial-image containers.");

    /// <summary>Minimum-capacity PIV card image target with room for wrapping overhead.</summary>
    public static ProfileFileSizeTarget PivMinimum { get; } = new(
        "piv",
        "minimum",
        "Minimum PIV Card Image",
        12_000,
        "Minimum-capacity PIV card image target that leaves room below the guaranteed container size for CBEFF, signing, and wrapping overhead.");

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
        new ImageDimensions(420, 560),
        new[] { 235f / 420f, 225f / 420f, 215f / 420f, 210f / 420f },
        MinimumHeadWidthRatio: 210f / 420f,
        MaximumHeadWidthRatio: 240f / 420f,
        TargetEyeLineFromTopRatio: 0.40f,
        MaximumRotationDegrees: 15f,
        MinimumInterPupillaryDistance: 90,
        MinimumTopMargin: 20,
        MinimumSideMargin: 10);

    private static readonly PortraitSpecification StandardPortrait420x560 = new(
        new ImageDimensions(420, 560),
        new[] { 0.70f },
        MinimumHeadWidthRatio: 0.50f,
        MaximumHeadWidthRatio: 0.75f,
        TargetEyeLineFromTopRatio: 0.45f,
        MaximumRotationDegrees: 15f,
        MinimumInterPupillaryDistance: 90,
        MinimumTopMargin: 0,
        MinimumSideMargin: 0);

    private static readonly PortraitSpecification StandardPortrait413x531 = new(
        new ImageDimensions(413, 531),
        new[] { 0.70f },
        MinimumHeadWidthRatio: 0.50f,
        MaximumHeadWidthRatio: 0.75f,
        TargetEyeLineFromTopRatio: 0.45f,
        MaximumRotationDegrees: 15f,
        MinimumInterPupillaryDistance: 90,
        MinimumTopMargin: 0,
        MinimumSideMargin: 0);

    /// <summary>Federal PIV card profile.</summary>
    public static ProfileSpecification Piv { get; } = new(
        "piv",
        "PIV",
        DefaultSingleFaceSelection,
        CardPortrait,
        new EncodingSpecification("image/jp2", true, 3, false, new NamedFileSizeGoal(ProfileFileSizeTargets.PivPreferred)));

    /// <summary>TWIC card profile.</summary>
    public static ProfileSpecification Twic { get; } = new(
        "twic",
        "TWIC",
        DefaultSingleFaceSelection,
        CardPortrait,
        new EncodingSpecification("image/jp2", true, 3, false, new MaxFileSizeGoal(14000)));

    /// <summary>Common Access Card profile.</summary>
    public static ProfileSpecification Cac { get; } = new(
        "cac",
        "CAC",
        DefaultSingleFaceSelection,
        StandardPortrait420x560,
        new EncodingSpecification("image/jp2", true, 3, false, new ExplicitRateGoal(0.7f)));

    /// <summary>ICAO portrait profile.</summary>
    public static ProfileSpecification Icao { get; } = new(
        "icao",
        "ICAO",
        DefaultSingleFaceSelection with { RequireSingleFace = false },
        StandardPortrait413x531,
        new EncodingSpecification("image/jp2", false, 2, false, new ExplicitRateGoal(2.0f)));
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
    IReadOnlyList<CandidateTrace> CandidateTraces);

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
    Maybe<string> TargetFileSizeId = default);

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
    IReadOnlyList<CandidateTrace> CandidateTraces);

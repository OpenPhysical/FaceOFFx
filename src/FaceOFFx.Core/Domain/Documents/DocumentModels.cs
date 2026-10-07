namespace FaceOFFx.Core.Domain.Documents;

/// <summary>Source-capture suitability controls used by the PIV geometry pipeline.</summary>
/// <param name="Id">Capture profile identifier.</param>
/// <param name="DisplayName">Readable profile name.</param>
/// <param name="MinimumFaceConfidence">Minimum accepted detection confidence.</param>
/// <param name="RequireSingleFace">Whether exactly one usable face is required.</param>
/// <param name="MaxRollDegrees">Maximum estimated source eye-line rotation.</param>
public sealed record InputProfileDefinition(
    string Id,
    string DisplayName,
    float MinimumFaceConfidence,
    bool RequireSingleFace,
    float MaxRollDegrees);

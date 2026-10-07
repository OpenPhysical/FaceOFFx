using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;

namespace FaceOFFx.Core.Domain.Documents;

/// <summary>PIV source-capture controls for automated geometry analysis.</summary>
public static class DocumentCatalog
{
    /// <summary>Single-face detection and estimated eye-line rotation controls for PIV input.</summary>
    public static InputProfileDefinition PivCapture { get; } = new("piv-capture", "PIV Capture", 0.8f, true, 5f);

    /// <summary>Resolves the PIV capture profile and reports unsupported profile identifiers.</summary>
    public static Result<InputProfileDefinition, PipelineError> GetInputProfile(string profileId) =>
        string.Equals(profileId, PivCapture.Id, StringComparison.OrdinalIgnoreCase)
            ? Result.Success<InputProfileDefinition, PipelineError>(PivCapture)
            : Result.Failure<InputProfileDefinition, PipelineError>(
                new ConfigurationError($"Capture profile '{profileId}' requires a supported PIV profile identifier.", profileId));
}

using JetBrains.Annotations;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;

namespace FaceOFFx.Models;

/// <summary>
/// Registry of ONNX models used in PIV processing pipeline.
/// </summary>
public static class ModelRegistry
{
    /// <summary>
    /// RetinaFace model for face detection.
    /// </summary>
    public const string FaceDetector = "FaceOFFx.Models.Resources.FaceDetector.onnx";

    /// <summary>
    /// PFLD model for 68-point facial landmark detection.
    /// </summary>
    public const string FaceLandmarks68 = "FaceOFFx.Models.Resources.landmarks_68_pfld.onnx";

    /// <summary>
    /// Gets a model as a byte array from embedded resources.
    /// </summary>
    public static Result<byte[], PipelineError> TryGetModel(string modelName)
    {
        var assembly = typeof(ModelRegistry).Assembly;
        using var stream = assembly.GetManifestResourceStream(modelName);
        if (stream is null)
        {
            return Result.Failure<byte[], PipelineError>(
                new ConfigurationError($"Model {modelName} not found in embedded resources", modelName));
        }

        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return Result.Success<byte[], PipelineError>(memoryStream.ToArray());
    }

    /// <summary>
    /// Checks if a model exists in the registry
    /// </summary>
    [PublicAPI]
    public static bool ModelExists(string modelName)
    {
        var assembly = typeof(ModelRegistry).Assembly;
        return assembly.GetManifestResourceNames().Contains(modelName);
    }
}

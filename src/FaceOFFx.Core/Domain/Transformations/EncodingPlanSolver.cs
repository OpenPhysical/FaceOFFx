using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>
/// Solves profile encoding decisions against explicit profile goals.
/// </summary>
[PublicAPI]
public static class EncodingPlanSolver
{
    /// <summary>
    /// Encodes a rendered portrait according to the supplied profile encoding specification.
    /// </summary>
    public static Result<(byte[] ImageData, EncodingDecision Decision), PipelineError> Encode(
        Image<Rgba32> image,
        FacialRoiSet roiSet,
        IJpeg2000Encoder encoder,
        EncodingSpecification specification)
    {
        return specification.Goal switch
        {
            MaxFileSizeGoal maxFileSizeGoal => EncodeForMaxFileSize(image, roiSet, encoder, specification, maxFileSizeGoal.MaxBytes, Maybe<string>.None),
            NamedFileSizeGoal namedFileSizeGoal => EncodeForMaxFileSize(
                image,
                roiSet,
                encoder,
                specification,
                namedFileSizeGoal.Target.MaxBytes,
                Maybe<string>.From(namedFileSizeGoal.Target.Id)),
            ExplicitRateGoal explicitRateGoal => EncodeForRate(image, roiSet, encoder, specification, explicitRateGoal),
            _ => Result.Failure<(byte[] ImageData, EncodingDecision Decision), PipelineError>(
                new ConfigurationError($"Unsupported encoding goal '{specification.Goal.GetType().Name}'."))
        };
    }

    private static Result<(byte[] ImageData, EncodingDecision Decision), PipelineError> EncodeForRate(
        Image<Rgba32> image,
        FacialRoiSet roiSet,
        IJpeg2000Encoder encoder,
        EncodingSpecification specification,
        ExplicitRateGoal goal)
    {
        using var imageForEncoding = image.Clone();
        return encoder.EncodeWithRoi(
                imageForEncoding,
                roiSet,
                goal.BitsPerPixel,
                specification.RoiStartLevel,
                specification.EnableRoi,
                specification.RoiAlign)
            .Map(data => (
                data,
                new EncodingDecision(
                    goal.BitsPerPixel,
                    data.Length,
                    Maybe<int>.None,
                    new[] { goal.BitsPerPixel } as IReadOnlyList<float>)));
    }

    private static Result<(byte[] ImageData, EncodingDecision Decision), PipelineError> EncodeForMaxFileSize(
        Image<Rgba32> image,
        FacialRoiSet roiSet,
        IJpeg2000Encoder encoder,
        EncodingSpecification specification,
        int maxBytes,
        Maybe<string> targetId)
    {
        var targetWithMargin = (int)(maxBytes * 0.95f);
        var expectedRate = CompressionMapping.GetRateForTargetSize(targetWithMargin);
        var expectedIndex = CompressionMapping.GetIndexForRate(expectedRate);
        var allRates = CompressionMapping.GetAllRates();
        var attemptedRates = new List<float>();
        var maxAttempts = Math.Max(1, 3);
        var upperAttempts = (int)Math.Floor(maxAttempts / 2.0);
        var lowerAttempts = (int)Math.Ceiling(maxAttempts / 2.0);
        var candidateRates = new List<float>();

        for (var i = 1; i <= upperAttempts; i++)
        {
            candidateRates.Add(allRates[Math.Min(expectedIndex + i, allRates.Length - 1)]);
        }

        for (var i = 0; i < lowerAttempts; i++)
        {
            candidateRates.Add(allRates[Math.Max(expectedIndex - i, 0)]);
        }

        foreach (var rate in candidateRates.Distinct())
        {
            attemptedRates.Add(rate);
            using var imageForEncoding = image.Clone();
            var result = encoder.EncodeWithRoi(
                imageForEncoding,
                roiSet,
                rate,
                specification.RoiStartLevel,
                specification.EnableRoi,
                specification.RoiAlign);
            if (result.IsFailure)
            {
                continue;
            }

            if (result.Value.Length <= maxBytes)
            {
                return Result.Success<(byte[] ImageData, EncodingDecision Decision), PipelineError>((
                    result.Value,
                    new EncodingDecision(rate, result.Value.Length, Maybe<int>.From(maxBytes), attemptedRates, targetId)));
            }
        }

        return Result.Failure<(byte[] ImageData, EncodingDecision Decision), PipelineError>(
            new ValidationError(
                $"Cannot encode the portrait within {maxBytes} bytes using the available compression ladder.",
                "profile-encoding"));
    }
}

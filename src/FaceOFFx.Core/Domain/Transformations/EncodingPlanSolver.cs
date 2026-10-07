using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>Encodes a fixed PIV face region using balanced allocation and a complete-JP2 ceiling.</summary>
[PublicAPI]
public static class EncodingPlanSolver
{
    /// <summary>Preserves hard-cap and payload reconciliation checks while recording regional measurements for review.</summary>
    public static Result<(byte[] ImageData, EncodingDecision Decision), PipelineError> Encode(
        Image<Rgba32> image, FacialRoiSet roiSet, IJpeg2000Encoder encoder, EncodingSpecification specification)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(roiSet);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(specification);
        if (!string.Equals(specification.MimeType, "image/jp2", StringComparison.Ordinal))
            return Failure("The fixed PIV encoder emits the image/jp2 media type.");

        int maxBytes;
        var targetId = Maybe<string>.None;
        PivCardImageBudget? cardBudget = null;
        switch (specification.Goal)
        {
            case MaxFileSizeGoal maximum:
                maxBytes = maximum.MaxBytes;
                break;
            case NamedFileSizeGoal named when named.Target is not null:
                cardBudget = named.Target.CardBudget;
                maxBytes = cardBudget is null ? named.Target.MaxBytes :
                    Math.Min(named.Target.MaxBytes, cardBudget.MaximumJpeg2000Bytes);
                targetId = Maybe<string>.From(named.Target.Id);
                break;
            default:
                return Failure("Supply a complete-JP2 byte target.");
        }
        if (maxBytes <= 0) return Failure("Supply a positive complete-JP2 byte target.");
        if (roiSet.Mask is null || roiSet.PixelCount <= 0)
            return Failure("The PIV candidate requires an anatomy-derived face mask.");

        var encoded = encoder.Encode(image, roiSet, new Jpeg2000EncodingOptions { MaximumOutputBytes = maxBytes });
        if (encoded.IsFailure)
            return Result.Failure<(byte[] ImageData, EncodingDecision Decision), PipelineError>(encoded.Error);
        var evidence = encoded.Value;
        if (evidence.Data.Length > maxBytes)
            return Failure($"Encoder output {evidence.Data.Length} exceeds the {maxBytes}-byte allowance.");
        if (evidence.RoiPixelCount != roiSet.PixelCount)
            return Failure("Encoder evidence must cover the complete PIV face mask.");
        if (evidence.SharedPayloadAttribution is null || evidence.PayloadTelemetry is null)
            return Failure("PIV encoding requires committed shared-payload attribution and complete-output telemetry.");
        if (evidence.PayloadTelemetry.TotalOutputBytes != evidence.Data.Length ||
            evidence.PayloadTelemetry.CodestreamBytes != evidence.CodestreamBytes ||
            evidence.PayloadTelemetry.ContainerBytes != evidence.ContainerBytes)
            return Failure("PIV payload evidence must reconcile with the actual complete JP2 output.");

        RegionalCompressionVerification regional;
        try
        {
            regional = RegionalCompressionVerification.MeasuredPiv(
                roiSet.PixelCount, evidence.SharedPayloadAttribution, evidence.PayloadTelemetry);
        }
        catch (ArgumentException ex)
        {
            return Failure($"PIV regional evidence must match the complete face mask and committed payload. {ex.Message}");
        }
        // The ledger is an operational measurement convention. Its ratio is an issuer review input;
        // allocation follows the fixed balanced recipe without a regional byte quota.
        var effectiveRate = (float)(evidence.Data.Length * 8.0 / ((long)image.Width * image.Height));
        var decision = new EncodingDecision(effectiveRate, evidence.Data.Length, Maybe<int>.From(maxBytes),
            Array.Empty<float>(), targetId)
        {
            CodecEvidence = evidence,
            CardBudget = cardBudget,
            RegionalCompressionVerification = regional
        };
        return Result.Success<(byte[] ImageData, EncodingDecision Decision), PipelineError>((evidence.Data, decision));
    }

    private static Result<(byte[] ImageData, EncodingDecision Decision), PipelineError> Failure(string message) =>
        Result.Failure<(byte[] ImageData, EncodingDecision Decision), PipelineError>(new ValidationError(message, "profile-encoding"));
}

using CoreJ2K.FaceOFFx;
using CoreJ2K.FaceOFFx.j2k.encoder;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Transformations;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Security.Cryptography;
using CodecRoiRegion = CoreJ2K.FaceOFFx.j2k.encoder.RoiRegion;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>Encodes three-component RGB JP2 images with the internal fixed balanced encoder.</summary>
[PublicAPI]
public class Jpeg2000EncoderService(ILogger<Jpeg2000EncoderService> logger) : IJpeg2000Encoder
{
    /// <summary>Uses one tile, one quality layer and optional face-centered Part 1 Maxshift ROI.</summary>
    public Result<Jpeg2000EncodingResult, PipelineError> Encode(
        Image<Rgba32> image,
        FacialRoiSet roiSet,
        Jpeg2000EncodingOptions options)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(image);
            ArgumentNullException.ThrowIfNull(roiSet);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumOutputBytes);
            var region = roiSet.Mask is { } mask
                ? CodecRoiRegion.FromMask(RoiMask.FromBytes(mask.Width, mask.Height, mask.ToArray()))
                : CodecRoiRegion.Rectangle(roiSet.InnerRegion.BoundingBox.X, roiSet.InnerRegion.BoundingBox.Y,
                    roiSet.InnerRegion.BoundingBox.Width, roiSet.InnerRegion.BoundingBox.Height);

            using var rgb = image.CloneAs<Rgb24>();
            var result = BalancedEncoder.Encode(rgb, options.MaximumOutputBytes, region);
            if (result.SharedPayloadAttribution is { } measured &&
                !string.Equals(measured.MethodId, Jpeg2000SharedPayloadAttribution.MethodIdentifier, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The codec attribution method '{measured.MethodId}' requires a matching FaceOFFx adapter.");
            }
            logger.LogInformation("Encoded RGB JPEG 2000: {Bytes} bytes, ROI {Pixels} pixels, protected payload {ProtectedBytes} bytes",
                result.Data.Length, result.RoiPixelCount, result.ProtectedPayloadBytes);
            return Result.Success<Jpeg2000EncodingResult, PipelineError>(new Jpeg2000EncodingResult(
                result.Data, result.CodestreamBytes, result.ContainerBytes, result.RoiPixelCount,
                result.ProtectedPayloadBytes, result.ProtectedPayloadAccounting,
                result.AllocationDiagnostics?.AllocationCandidateCount ?? 0,
                result.AllocationDiagnostics?.AllocationSimulationCount ?? 0)
            {
                RoiMaskSha256 = roiSet.Mask is { } encodedMask
                    ? Convert.ToHexString(SHA256.HashData(encodedMask.Pixels)).ToLowerInvariant()
                    : null,
                PayloadTelemetry = result.PayloadTelemetry is { } payload
                    ? new Jpeg2000PayloadTelemetry(payload.Subbands.Select(row => new Jpeg2000SubbandPayload(
                        row.Tile, row.Component, row.ResolutionLevel, (Jpeg2000WaveletSubband)row.Subband,
                        row.EmittedCodeBlockCount, row.PayloadBytes, row.ProtectedMaxshiftPhaseBytes,
                        row.WholeBandPromotedBytes, row.CodeBlockPromotedBytes, row.OutsideRoiCodeBlockBytes,
                        row.MixedOrRefinementBytes, row.ProtectedPhaseBytesInMixedCodeBlocks)),
                        payload.CodestreamBytes, payload.ContainerBytes, payload.MainAndTileHeaderBytes,
                        payload.PacketHeaderBytes, payload.EndOfCodestreamBytes, payload.EmittedPacketCount)
                    : null,
                SharedPayloadAttribution = result.SharedPayloadAttribution is { } attribution
                    ? new Jpeg2000SharedPayloadAttribution(attribution.Subbands.Select(row => new Jpeg2000SubbandAttribution(
                        row.Tile, row.Component, row.ResolutionLevel, (Jpeg2000WaveletSubband)row.Subband,
                        row.PayloadBytes, row.FacePayloadByteEstimate)), attribution.RoiPixelCount,
                        attribution.PacketBodyBytes, attribution.FacePayloadByteEstimate, attribution.AttributedFacePayloadBytes)
                    : null
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "JPEG 2000 encoding failed");
            return Result.Failure<Jpeg2000EncodingResult, PipelineError>(
                new RenderError($"JPEG 2000 encoding failed: {ex.Message}", "jpeg2000"));
        }
    }

}

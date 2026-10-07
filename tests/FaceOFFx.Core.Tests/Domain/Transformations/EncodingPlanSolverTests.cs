using AwesomeAssertions;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public class EncodingPlanSolverTests
{
    [Test]
    public void ByteCap_UsesOneBalancedRequestAndPreservesMeasuredEvidence()
    {
        using var image = new Image<Rgba32>(64, 80);
        var encoder = new RecordingEncoder();
        var result = EncodingPlanSolver.Encode(image, CreateMaskRoi(), encoder, Specification(250));
        result.IsSuccess.Should().BeTrue();
        encoder.Calls.Should().Be(1);
        encoder.Options!.MaximumOutputBytes.Should().Be(250);
        result.Value.Decision.AttemptedRates.Should().BeEmpty();
        result.Value.Decision.RegionalCompressionVerification!.MeasuredCompressionRatio.Should().Be(24);
        typeof(Jpeg2000EncodingOptions).GetProperties().Select(property => property.Name)
            .Should().Equal(nameof(Jpeg2000EncodingOptions.MaximumOutputBytes));
    }

    [Test]
    public void NamedCardBudget_ReservesRecordAndIssuerOverhead()
    {
        using var image = new Image<Rgba32>(64, 80);
        var budget = new PivCardImageBudget(maximumSignatureBytes: 658);
        var profile = ProfileSpecifications.Piv.WithCardImageBudget(budget).Value;
        var encoder = new RecordingEncoder();
        var result = EncodingPlanSolver.Encode(image, CreateMaskRoi(), encoder, profile.Encoding);
        result.IsSuccess.Should().BeTrue();
        encoder.Options!.MaximumOutputBytes.Should().Be(11_912);
        result.Value.Decision.CardBudget.Should().Be(budget);
        result.Value.Decision.TargetFileSizeId.Value.Should().Be("issuer-budget");
        var regional = result.Value.Decision.RegionalCompressionVerification!;
        regional.RoiPixelCount.Should().Be(320);
        regional.UncompressedRegionBytes.Should().Be(960);
        regional.MinimumAttributedPayloadBytes.Should().Be(40);
        regional.AttributedPayloadBytes.Should().Be(40);
    }

    [Test]
    public void MissingFaceMask_RejectsBeforeEncodingForEveryByteTarget()
    {
        using var image = new Image<Rgba32>(64, 80);
        var encoder = new RecordingEncoder();
        var roi = CreateMaskRoi() with { Mask = null };
        EncodingPlanSolver.Encode(image, roi, encoder, ProfileSpecifications.Piv.Encoding).IsFailure.Should().BeTrue();
        EncodingPlanSolver.Encode(image, roi, encoder, Specification(250)).IsFailure.Should().BeTrue();
        encoder.Calls.Should().Be(0);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void NonpositiveCap_RejectsBeforeEncoding(int cap)
    {
        using var image = new Image<Rgba32>(64, 80);
        var encoder = new RecordingEncoder();
        EncodingPlanSolver.Encode(image, CreateMaskRoi(), encoder, Specification(cap)).IsFailure.Should().BeTrue();
        encoder.Calls.Should().Be(0);
    }

    [Test]
    public void UnsupportedMediaType_RejectsBeforeEncoding()
    {
        using var image = new Image<Rgba32>(64, 80);
        var encoder = new RecordingEncoder();
        var result = EncodingPlanSolver.Encode(image, CreateMaskRoi(), encoder,
            Specification(250) with { MimeType = "image/jpeg" });
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("image/jp2");
        encoder.Calls.Should().Be(0);
    }

    [Test]
    public void OperationalRatioAboveLimit_IsRecordedWithoutChangingBalancedAllocation()
    {
        using var image = new Image<Rgba32>(64, 80);
        var roi = CreateMaskRoi();
        var maskBefore = roi.Mask!.ToArray();
        var encoder = new RecordingEncoder { Output = MeasuredEvidence(320, 39) };
        var result = EncodingPlanSolver.Encode(image, roi, encoder, ProfileSpecifications.Piv.Encoding);
        result.IsSuccess.Should().BeTrue();
        encoder.Calls.Should().Be(1);
        result.Value.Decision.RegionalCompressionVerification!.Status
            .Should().Be(RegionalCompressionVerificationStatus.ExceedsLimit);
        result.Value.Decision.RegionalCompressionVerification.MeasuredCompressionRatio.Should().BeGreaterThan(24);
        roi.Mask.ToArray().Should().Equal(maskBefore);
    }

    [Test]
    public void RegionalArithmeticAboveCompleteImageLength_IsReviewEvidence()
    {
        using var image = new Image<Rgba32>(64, 80);
        var roi = CreateMaskRoi();
        var allPixels = Enumerable.Repeat((byte)1, 64 * 80).ToArray();
        roi = roi with { Mask = FacialRoiMask.FromBytes(64, 80, allPixels) };
        var encoder = new RecordingEncoder { Output = MeasuredEvidence(roi.PixelCount, 39) };
        var result = EncodingPlanSolver.Encode(image, roi, encoder, Specification(200));
        result.IsSuccess.Should().BeTrue();
        result.Value.Decision.RegionalCompressionVerification!.MinimumAttributedPayloadBytes.Should().Be(640);
        result.Value.Decision.RegionalCompressionVerification.Status.Should().Be(RegionalCompressionVerificationStatus.ExceedsLimit);
    }

    [Test]
    public void MissingSharedPayload_RejectsUnverifiableEncoderEvidence()
    {
        using var image = new Image<Rgba32>(64, 80);
        var encoder = new RecordingEncoder { Output = MeasuredEvidence(320, 40) with { SharedPayloadAttribution = null } };
        var result = EncodingPlanSolver.Encode(image, CreateMaskRoi(), encoder, ProfileSpecifications.Piv.Encoding);
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("shared-payload attribution");
    }

    [Test]
    public void Evidence_MustReconcileWithTheCompleteOutput()
    {
        using var image = new Image<Rgba32>(64, 80);
        var encoder = new RecordingEncoder { Output = MeasuredEvidence(320, 40) with { Data = new byte[201] } };
        var result = EncodingPlanSolver.Encode(image, CreateMaskRoi(), encoder, ProfileSpecifications.Piv.Encoding);
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("actual complete JP2");
    }

    [Test]
    public void Attribution_MustMatchEveryCommittedBand()
    {
        using var image = new Image<Rgba32>(64, 80);
        var changed = new Jpeg2000SharedPayloadAttribution(new[]
        {
            new Jpeg2000SubbandAttribution(0, 0, 1, Jpeg2000WaveletSubband.HL, 150, 40)
        }, 320, 150, 40, 40);
        var encoder = new RecordingEncoder { Output = MeasuredEvidence(320, 40) with { SharedPayloadAttribution = changed } };
        var result = EncodingPlanSolver.Encode(image, CreateMaskRoi(), encoder, Specification(250));
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("every committed wavelet-band");
    }

    [TestCase(251, 320)]
    [TestCase(200, 319)]
    public void Output_MustMeetCapAndCompleteFaceMask(int bytes, long pixels)
    {
        using var image = new Image<Rgba32>(64, 80);
        var encoder = new RecordingEncoder { Output = MeasuredEvidence(pixels, 40) with { Data = new byte[bytes] } };
        EncodingPlanSolver.Encode(image, CreateMaskRoi(), encoder, Specification(250)).IsFailure.Should().BeTrue();
    }

    private static EncodingSpecification Specification(int cap) => new("image/jp2", new MaxFileSizeGoal(cap));

    private static FacialRoiSet CreateMaskRoi()
    {
        var pixels = new byte[64 * 80];
        for (var y = 20; y < 40; y++)
        for (var x = 24; x < 40; x++) pixels[y * 64 + x] = 1;
        return new FacialRoiSet(new RoiRegion("Reviewed face", 3, new RoiBoundingBox(24, 20, 16, 20), new[] { 0 }))
        { Mask = FacialRoiMask.FromBytes(64, 80, pixels) };
    }

    private static Jpeg2000EncodingResult MeasuredEvidence(long pixels, long faceBytes)
    {
        var telemetry = new Jpeg2000PayloadTelemetry(new[]
        {
            new Jpeg2000SubbandPayload(0, 0, 0, Jpeg2000WaveletSubband.LL, 1, 150, 0, 150, 0, 0, 0, 0)
        }, 180, 20, 20, 8, 2, 1);
        return new Jpeg2000EncodingResult(new byte[200], 180, 20, pixels, null, "Measured test phase", 1, 1)
        {
            PayloadTelemetry = telemetry,
            SharedPayloadAttribution = new Jpeg2000SharedPayloadAttribution(new[]
            {
                new Jpeg2000SubbandAttribution(0, 0, 0, Jpeg2000WaveletSubband.LL, 150, faceBytes)
            }, pixels, 150, faceBytes, faceBytes)
        };
    }

    private sealed class RecordingEncoder : IJpeg2000Encoder
    {
        public int Calls { get; private set; }
        public Jpeg2000EncodingOptions? Options { get; private set; }
        public Jpeg2000EncodingResult? Output { get; init; }

        public Result<Jpeg2000EncodingResult, PipelineError> Encode(Image<Rgba32> image, FacialRoiSet roiSet, Jpeg2000EncodingOptions options)
        {
            Calls++;
            Options = options;
            return Result.Success<Jpeg2000EncodingResult, PipelineError>(Output ?? MeasuredEvidence(roiSet.PixelCount, 40));
        }
    }
}

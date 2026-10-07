using AwesomeAssertions;
using System.Buffers.Binary;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using FaceOFFx.Tests.Common;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Tests.Services;

[TestFixture]
[NonParallelizable]
public class ProfileEncoderTests : IntegrationTestBase
{
    private ILoggerFactory _loggerFactory = null!;
    private OnnxFacialProcessingServiceFactory _processingServiceFactory = null!;
    private FaceGeometryPipeline _faceGeometryPipeline = null!;
    private ProfileEncoder _profileEncoder = null!;

    [OneTimeSetUp]
    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();

        _loggerFactory = LoggerFactory.Create(builder => { });
        _processingServiceFactory = new OnnxFacialProcessingServiceFactory(_loggerFactory);
        _faceGeometryPipeline = new FaceGeometryPipeline(
            _processingServiceFactory,
            Substitute.For<ILogger<FaceGeometryPipeline>>());
        _profileEncoder = new ProfileEncoder(
            _faceGeometryPipeline,
            _processingServiceFactory,
            _loggerFactory.CreateLogger<ProfileEncoder>());
    }

    [OneTimeTearDown]
    public override void OneTimeTearDown()
    {
        _processingServiceFactory?.Dispose();
        _loggerFactory?.Dispose();
        base.OneTimeTearDown();
    }

    [Test]
    public async Task ProcessAsync_WithPivProfile_ReturnsEncodedArtifact()
    {
        var profile = ProfileSpecifications.Piv.WithFileSizeTarget(ProfileFileSizeTargets.PivPreferred).Value;

        var result = await _profileEncoder.ProcessAsync(File.ReadAllBytes(PeopleCorpus.WatermarkedCardholderSource()), profile);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Profile.Id.Should().Be(profile.Id);
        result.Value.ImageData.Should().NotBeEmpty();
        result.Value.OutputDimensions.Should().Be(profile.Portrait.OutputDimensions);
        result.Value.CandidateTraces.Should().NotBeEmpty();
    }

    [Test]
    public async Task ProcessAsync_UntaggedFeaturelessSourceReachesFaceDetectionWithoutChangingBytes()
    {
        using var image = new Image<Rgba32>(64, 80, new Rgba32(32, 64, 96));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        var bytes = stream.ToArray();
        var original = bytes.ToArray();
        var result = await _profileEncoder.ProcessAsync(bytes, ProfileSpecifications.Piv);
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("No face");
        bytes.Should().Equal(original);
    }

    [Test]
    public void ProcessAsync_PreCanceledRequestPreservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() => _profileEncoder.ProcessAsync(
            new byte[] { 1 }, ProfileSpecifications.Piv, cancellation.Token));
    }

    [Test]
    public async Task ProcessAsync_OverLimitHeaderRejectsBeforePixelDecode()
    {
        var bytes = new byte[54];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), 8_001);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), 8_000);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 24);
        var information = Image.Identify(bytes);
        information.Width.Should().Be(8_001);
        information.Height.Should().Be(8_000);
        var result = await _profileEncoder.ProcessAsync(bytes, ProfileSpecifications.Piv);
        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("64000000 pixels");
    }

    [Test]
    public async Task ProcessAsync_PivPreferred_StaysWithinByteCapAndRetainsRegionalEvidence()
    {
        var profile = ProfileSpecifications.Piv.WithFileSizeTarget(ProfileFileSizeTargets.PivPreferred).Value;
        var result = await _profileEncoder.ProcessAsync(File.ReadAllBytes(PeopleCorpus.WatermarkedCardholderSource()), profile);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Encoding.TargetFileSize.HasValue.Should().BeTrue();
        result.Value.Encoding.TargetFileSize.Value.Should().Be(ProfileFileSizeTargets.PivPreferred.MaxBytes);
        result.Value.Encoding.TargetFileSizeId.HasValue.Should().BeTrue();
        result.Value.Encoding.TargetFileSizeId.Value.Should().Be("preferred");
        result.Value.Encoding.FileSize.Should().BeLessThanOrEqualTo(ProfileFileSizeTargets.PivPreferred.MaxBytes);
        result.Value.GeometryEvidence!.OriginalPixelSupportVerified.Should().BeTrue();
        result.Value.GeometryEvidence.SourceToOutputScale.Should().BeLessThanOrEqualTo(1);
        result.Value.GeometryEvidence.VerificationRequirements.Should().NotBeEmpty();
        result.Value.SourceColorEvidence.Should().NotBeNull();
        result.Value.Encoding.RegionalCompressionVerification!.Status.Should().NotBe(
            RegionalCompressionVerificationStatus.PendingSharedBandAttribution);
        result.Value.Encoding.RegionalCompressionVerification.AttributedPayloadBytes.Should().BeGreaterThan(0);
        result.Value.Encoding.RegionalCompressionVerification.MeasuredCompressionRatio.Should().BeGreaterThan(0);
        result.Value.RoiCoverage!.Status.Should().Be(FaceOFFx.Core.Domain.Detection.GeometryEvidenceStatus.Estimated);
        result.Value.Encoding.CodecEvidence!.PayloadTelemetry!.TotalOutputBytes.Should().Be(
            result.Value.ImageData.Length);
    }

    [Test]
    public async Task ProcessAsync_ImpossibleByteCapReturnsAnEncodingError()
    {
        var profile = ProfileSpecifications.Piv with
        {
            Encoding = new EncodingSpecification("image/jp2", new MaxFileSizeGoal(100))
        };
        var result = await _profileEncoder.ProcessAsync(File.ReadAllBytes(PeopleCorpus.WatermarkedCardholderSource()), profile);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Test]
    public async Task ProcessAsync_CustomByteTargetUsesBalancedEncodingAndMeasuredEvidence()
    {
        var profile = ProfileSpecifications.Piv with
        {
            Encoding = new EncodingSpecification("image/jp2", new MaxFileSizeGoal(16_000))
        };
        var result = await _profileEncoder.ProcessAsync(File.ReadAllBytes(PeopleCorpus.WatermarkedCardholderSource()), profile);

        result.IsSuccess.Should().BeTrue();
        result.Value.Encoding.TargetFileSize.Value.Should().Be(16_000);
        result.Value.Encoding.TargetFileSizeId.HasValue.Should().BeFalse();
        result.Value.ImageData.Length.Should().BeLessThanOrEqualTo(16_000);
        result.Value.Encoding.CodecEvidence!.PayloadTelemetry!.TotalOutputBytes.Should().Be(result.Value.ImageData.Length);
        result.Value.Encoding.RegionalCompressionVerification.Should().NotBeNull();
    }
}

using AwesomeAssertions;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using FaceOFFx.Tests.Common;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace FaceOFFx.Infrastructure.Tests.Services;

[TestFixture]
[NonParallelizable]
public class ProfileEncoderTests : IntegrationTestBase
{
    private ILoggerFactory _loggerFactory = null!;
    private OnnxFacialProcessingServiceFactory _processingServiceFactory = null!;
    private FaceGeometryPipeline _faceGeometryPipeline = null!;
    private ProfileEncoder _profileEncoder = null!;
    private byte[] _genericGuyBytes = null!;

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
            Substitute.For<ILogger<ProfileEncoder>>());
        _genericGuyBytes = File.ReadAllBytes(PeopleCorpus.SubjectSource("generic-guy", "png"));
    }

    [OneTimeTearDown]
    public override void OneTimeTearDown()
    {
        _processingServiceFactory?.Dispose();
        _loggerFactory?.Dispose();
        base.OneTimeTearDown();
    }

    [TestCase("piv")]
    [TestCase("twic")]
    [TestCase("cac")]
    [TestCase("icao")]
    public async Task ProcessAsync_WithBuiltInProfiles_ReturnsEncodedArtifact(string profileId)
    {
        var profile = profileId switch
        {
            "twic" => ProfileSpecifications.Twic,
            "cac" => ProfileSpecifications.Cac,
            "icao" => ProfileSpecifications.Icao,
            _ => ProfileSpecifications.Piv,
        };

        var result = await _profileEncoder.ProcessAsync(_genericGuyBytes, profile);

        result.IsSuccess.Should().BeTrue();
        result.Value.Profile.Id.Should().Be(profile.Id);
        result.Value.ImageData.Should().NotBeEmpty();
        result.Value.OutputDimensions.Should().Be(profile.Portrait.OutputDimensions);
        result.Value.CandidateTraces.Should().NotBeEmpty();
    }

    [Test]
    public async Task ProcessAsync_Piv_StaysWithinConfiguredByteCap()
    {
        var result = await _profileEncoder.ProcessAsync(_genericGuyBytes, ProfileSpecifications.Piv);

        result.IsSuccess.Should().BeTrue();
        result.Value.Encoding.TargetFileSize.HasValue.Should().BeTrue();
        result.Value.Encoding.TargetFileSize.Value.Should().Be(22000);
        result.Value.Encoding.TargetFileSizeId.HasValue.Should().BeTrue();
        result.Value.Encoding.TargetFileSizeId.Value.Should().Be("preferred");
        result.Value.Encoding.FileSize.Should().BeLessThanOrEqualTo(22000);
    }

    [Test]
    public async Task ProcessAsync_PivMinimumTarget_StaysWithinConfiguredByteCap()
    {
        var profileResult = ProfileSpecifications.Piv.WithFileSizeTarget(ProfileFileSizeTargets.PivMinimum);

        profileResult.IsSuccess.Should().BeTrue();
        var result = await _profileEncoder.ProcessAsync(_genericGuyBytes, profileResult.Value);

        result.IsSuccess.Should().BeTrue();
        result.Value.Encoding.TargetFileSize.HasValue.Should().BeTrue();
        result.Value.Encoding.TargetFileSize.Value.Should().Be(12000);
        result.Value.Encoding.TargetFileSizeId.HasValue.Should().BeTrue();
        result.Value.Encoding.TargetFileSizeId.Value.Should().Be("minimum");
        result.Value.Encoding.FileSize.Should().BeLessThanOrEqualTo(12000);
    }

    [Test]
    public async Task ProcessAsync_Icao_UsesExplicitRateGoal()
    {
        var result = await _profileEncoder.ProcessAsync(_genericGuyBytes, ProfileSpecifications.Icao);

        result.IsSuccess.Should().BeTrue();
        result.Value.Encoding.TargetFileSize.HasValue.Should().BeFalse();
        result.Value.Encoding.TargetFileSizeId.HasValue.Should().BeFalse();
        result.Value.Encoding.CompressionRate.Should().Be(2.0f);
        result.Value.Encoding.AttemptedRates.Should().Equal(new[] { 2.0f });
    }
}

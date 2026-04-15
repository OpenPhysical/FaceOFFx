using AwesomeAssertions;
using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Infrastructure.Services;
using FaceOFFx.Tests.Common;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;

namespace FaceOFFx.Infrastructure.Tests.Services;

[TestFixture]
[NonParallelizable]
public class DocumentJobRunnerTests : IntegrationTestBase
{
    private RetinaFaceDetector _faceDetector = null!;
    private OnnxLandmarkExtractor _landmarkExtractor = null!;
    private Jpeg2000EncoderService _jpeg2000Encoder = null!;
    private PassportPhotoRenderService _passportPhotoRenderService = null!;
    private FaceGeometryPipeline _faceGeometryPipeline = null!;
    private DocumentRenderService _documentRenderService = null!;
    private DocumentJobRunner _runner = null!;

    private string _genericGuyPath = null!;
    [OneTimeSetUp]
    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();

        _faceDetector = new RetinaFaceDetector(Substitute.For<ILogger<RetinaFaceDetector>>());
        _landmarkExtractor = new OnnxLandmarkExtractor(Substitute.For<ILogger<OnnxLandmarkExtractor>>());
        _jpeg2000Encoder = new Jpeg2000EncoderService(Substitute.For<ILogger<Jpeg2000EncoderService>>());
        _passportPhotoRenderService = new PassportPhotoRenderService(
            _faceDetector,
            _landmarkExtractor,
            Substitute.For<ILogger<PassportPhotoRenderService>>());
        _faceGeometryPipeline = new FaceGeometryPipeline(
            _faceDetector,
            _landmarkExtractor,
            Substitute.For<ILogger<FaceGeometryPipeline>>());
        _documentRenderService = new DocumentRenderService(
            _faceGeometryPipeline,
            _passportPhotoRenderService,
            _jpeg2000Encoder,
            Substitute.For<ILogger<DocumentRenderService>>());
        _runner = new DocumentJobRunner(
            _documentRenderService,
            Substitute.For<ILogger<DocumentJobRunner>>());

        _genericGuyPath = PeopleCorpus.SubjectSource("generic-guy", "png");
    }

    [OneTimeTearDown]
    public override void OneTimeTearDown()
    {
        _landmarkExtractor?.Dispose();
        _faceDetector?.Dispose();
        base.OneTimeTearDown();
    }

    [Test]
    public async Task RunAsync_PivStandard_WritesBothArtifactsAndProvenance()
    {
        var result = await _runner.RunAsync(new DocumentJobRequest(
            _genericGuyPath,
            "piv",
            OutputDirectory: TempDirectory));

        result.IsSuccess.Should().BeTrue();
        result.Value.Deliverables.Should().HaveCount(2);
        result.Value.Deliverables.Select(d => Path.GetExtension(d.OutputPath)).Should().Contain(new[] { ".jp2", ".jpg" });
        File.Exists(result.Value.ProvenancePath).Should().BeTrue();
    }

    [Test]
    public async Task RunAsync_CanadaPermanentResident_Digital_WritesSupportingInfoAndOriginalityAdvisory()
    {
        var result = await _runner.RunAsync(new DocumentJobRequest(
            _genericGuyPath,
            "canada-permanent-resident",
            VariantId: "digital",
            OutputDirectory: TempDirectory));

        result.IsSuccess.Should().BeTrue();
        result.Value.Deliverables.Should().ContainSingle();

        var deliverable = result.Value.Deliverables[0];
        Path.GetExtension(deliverable.OutputPath).Should().Be(".jpeg");
        deliverable.SupportingInfoPath.Should().NotBeNull();
        File.Exists(deliverable.SupportingInfoPath!).Should().BeTrue();
        deliverable.OriginalFileRequirementSatisfied.Should().BeFalse();
        deliverable.Checks.Should().Contain(check =>
            check.Id == "canada-digital-originality"
            && check.Disposition == Core.Domain.Documents.DocumentCheckDisposition.Advisory
            && !check.Passed);
    }

    [Test]
    public async Task RunAsync_CanadaProofOfCitizenship_Digital_WritesSupportingInfoAndArtifact()
    {
        var result = await _runner.RunAsync(new DocumentJobRequest(
            _genericGuyPath,
            "canada-proof-of-citizenship",
            VariantId: "digital",
            OutputDirectory: TempDirectory));

        result.IsSuccess.Should().BeTrue();
        result.Value.Deliverables.Should().ContainSingle();

        var deliverable = result.Value.Deliverables[0];
        Path.GetExtension(deliverable.OutputPath).Should().Be(".jpeg");
        deliverable.SupportingInfoPath.Should().NotBeNull();
        File.Exists(deliverable.OutputPath).Should().BeTrue();
        File.Exists(deliverable.SupportingInfoPath!).Should().BeTrue();
    }

    [TestCaseSource(nameof(RepresentativePortraitInputs))]
    public async Task RunAsync_RepresentativePortraitInputs_ReturnSuccessAndWriteProvenance(string inputPath)
    {
        var outputDir = Path.Combine(TempDirectory, Path.GetFileNameWithoutExtension(inputPath));
        Directory.CreateDirectory(outputDir);

        var result = await _runner.RunAsync(new DocumentJobRequest(
            inputPath,
            "us-passport",
            VariantId: "print",
            OutputDirectory: outputDir));

        result.IsSuccess.Should().BeTrue();
        File.Exists(result.Value.ProvenancePath).Should().BeTrue();

        if (result.Value.InputPassed)
        {
            result.Value.Deliverables.Should().ContainSingle();
            File.Exists(result.Value.Deliverables[0].OutputPath).Should().BeTrue();
        }
    }

    private static IEnumerable<string> RepresentativePortraitInputs()
    {
        yield return PeopleCorpus.SubjectSource("person-01", "jpg");
        yield return PeopleCorpus.SubjectSource("person-02", "jpg");
        yield return PeopleCorpus.SubjectSource("person-03", "jpg");
    }
}

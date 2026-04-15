using AwesomeAssertions;
using FaceOFFx.Core.Domain.Documents;
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
public class PassportPhotoRenderServiceTests : IntegrationTestBase
{
    private RetinaFaceDetector _faceDetector = null!;
    private OnnxLandmarkExtractor _landmarkExtractor = null!;
    private PassportPhotoRenderService _service = null!;

    [OneTimeSetUp]
    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();
        _faceDetector = new RetinaFaceDetector(Substitute.For<ILogger<RetinaFaceDetector>>());
        _landmarkExtractor = new OnnxLandmarkExtractor(Substitute.For<ILogger<OnnxLandmarkExtractor>>());
        _service = new PassportPhotoRenderService(
            _faceDetector,
            _landmarkExtractor,
            Substitute.For<ILogger<PassportPhotoRenderService>>());
    }

    [OneTimeTearDown]
    public override void OneTimeTearDown()
    {
        _landmarkExtractor?.Dispose();
        _faceDetector?.Dispose();
        base.OneTimeTearDown();
    }

    [Test]
    public async Task AlignAsync_Person01CanadaCitizenshipGrant_RoundTripsProjectedLandmarks()
    {
        using var image = await Image.LoadAsync<Rgba32>(PeopleCorpus.SubjectSource("person-01", "jpg"));
        var document = DocumentCatalog.GetDocumentOrThrow("canada-citizenship-grant");
        var deliverable = document.Variants["digital"].Deliverables[0];
        var spec = DocumentCatalog.GetPassportPhotoSpecOrThrow(deliverable.ProductionDefaults["spec"]);

        var result = await _service.AlignAsync(image, spec);

        result.IsSuccess.Should().BeTrue();
        using var alignment = result.Value;

        foreach (var point in alignment.Landmarks.Points)
        {
            var sourcePoint = alignment.TransformMap.MapOutputToSource(point);
            var roundTripped = alignment.TransformMap.MapSourceToOutput(sourcePoint);
            roundTripped.X.Should().BeApproximately(point.X, 0.05f);
            roundTripped.Y.Should().BeApproximately(point.Y, 0.05f);

            sourcePoint.X.Should().BeGreaterThanOrEqualTo(0f);
            sourcePoint.Y.Should().BeGreaterThanOrEqualTo(0f);
            sourcePoint.X.Should().BeLessThanOrEqualTo(image.Width);
            sourcePoint.Y.Should().BeLessThanOrEqualTo(image.Height);
        }
    }
}

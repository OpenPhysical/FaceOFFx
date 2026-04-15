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
    private ILoggerFactory _loggerFactory = null!;
    private OnnxFacialProcessingServiceFactory _processingServiceFactory = null!;
    private PassportPhotoRenderService _service = null!;

    [OneTimeSetUp]
    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();
        _loggerFactory = LoggerFactory.Create(builder => { });
        _processingServiceFactory = new OnnxFacialProcessingServiceFactory(_loggerFactory);
        _service = new PassportPhotoRenderService(
            _processingServiceFactory,
            Substitute.For<ILogger<PassportPhotoRenderService>>());
    }

    [OneTimeTearDown]
    public override void OneTimeTearDown()
    {
        _processingServiceFactory?.Dispose();
        _loggerFactory?.Dispose();
        base.OneTimeTearDown();
    }

    [Test]
    public async Task AlignAsync_Person01CanadaCitizenshipGrant_RoundTripsProjectedLandmarks()
    {
        using var image = await Image.LoadAsync<Rgba32>(PeopleCorpus.SubjectSource("person-01", "jpg"));
        var document = DocumentCatalog.GetDocument("canada-citizenship-grant").Value;
        var deliverable = document.Variants["digital"].Deliverables[0];
        var spec = DocumentCatalog.GetPassportPhotoSpec(deliverable.ProductionDefaults["spec"]).Value;

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

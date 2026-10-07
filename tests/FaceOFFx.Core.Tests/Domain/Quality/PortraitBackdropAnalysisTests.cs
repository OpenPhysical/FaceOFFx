using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Quality;

[TestFixture]
public class PortraitBackdropAnalysisTests
{
    private static PortraitRegionPixelStatistics[] Statistics()
    {
        var component = new RgbPixelComponentStatistics(120, 0, 0, 0, 0);
        var correlation = new RgbPixelCorrelation(PixelCorrelationStatus.Undefined, 0);
        return Enum.GetValues<PortraitRegionLabel>().Select(label => new PortraitRegionPixelStatistics(
            label, 1, component, component, component, correlation, correlation, correlation)).ToArray();
    }

    [Test]
    public void OwnsLabelAndStatisticSnapshots()
    {
        byte[] labels = [0, 1, 2, 3];
        var statistics = Statistics();
        var result = new PortraitBackdropAnalysis(new ImageDimensions(2, 2), labels,
            PortraitBackdropPresenceStatus.EstimatedCandidate, statistics, 0.9);
        labels[3] = 0;
        statistics[0] = statistics[0] with { PixelCount = 0 };
        var copy = result.CopyCandidateMask();
        copy[3] = 0;
        Assert.That(result.CandidatePixelCount, Is.EqualTo(1));
        Assert.That(result.CandidateFraction, Is.EqualTo(0.25));
        Assert.That(result.GetLabel(1, 1), Is.EqualTo(PortraitRegionLabel.BackdropCandidate));
        Assert.That(result.Statistics[0].PixelCount, Is.EqualTo(1));
        Assert.That(result.CopyCandidateMask()[3], Is.EqualTo(1));
    }

    [TestCase("mean")]
    [TestCase("correlation")]
    [TestCase("count")]
    [TestCase("label")]
    [TestCase("presence")]
    public void RequiresFiniteMeasurementsMatchingTheLabelSnapshot(string invalid)
    {
        byte[] labels = [0, 1, 2, 3];
        var statistics = Statistics();
        var presence = PortraitBackdropPresenceStatus.EstimatedCandidate;
        switch (invalid)
        {
            case "mean": statistics[0] = statistics[0] with { Red = statistics[0].Red with { Mean = double.NaN } }; break;
            case "correlation": statistics[0] = statistics[0] with { RedBlueCorrelation = new RgbPixelCorrelation(PixelCorrelationStatus.Undefined, 1) }; break;
            case "count": statistics[0] = statistics[0] with { PixelCount = 2 }; break;
            case "label": labels[0] = 255; break;
            case "presence": presence = PortraitBackdropPresenceStatus.Undetermined; break;
        }
        Assert.Throws<ArgumentException>(() => new PortraitBackdropAnalysis(new ImageDimensions(2, 2), labels, presence, statistics, 0.9));
    }
}

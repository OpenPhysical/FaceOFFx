using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Infrastructure.Services;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Infrastructure.Tests.Services;

[TestFixture]
public class PortraitBackdropAnalyzerTests
{
    private static FacialRoiMask Mask(int width, int height, Rectangle region)
    {
        var pixels = new byte[width * height];
        for (var y = region.Top; y < region.Bottom; y++)
        for (var x = region.Left; x < region.Right; x++) pixels[y * width + x] = 1;
        return FacialRoiMask.FromBytes(width, height, pixels);
    }

    private static PortraitForegroundProtection Protection() => new(55)
    { RetainedHeadEnvelope = new Rectangle(18, 10, 28, 45) };

    [Test]
    public void UniformBackdropHasFourConservativeLabelsAndPreservesTheSource()
    {
        using var image = new Image<Rgba32>(64, 80, new Rgba32(200, 200, 200, 255));
        for (var y = 10; y < 55; y++)
        for (var x = 18; x < 46; x++) image[x, y] = new Rgba32(100, 80, 60, 255);
        var original = new byte[64 * 80 * 4];
        image.CopyPixelDataTo(original);
        var result = PortraitBackdropAnalyzer.Analyze(image, Mask(64, 80, new Rectangle(24, 25, 16, 25)), Protection());
        Assert.That(result.IsSuccess, Is.True);
        var analysis = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(analysis.Presence, Is.EqualTo(PortraitBackdropPresenceStatus.EstimatedCandidate));
            Assert.That(analysis.CandidatePixelCount, Is.GreaterThan(16));
            Assert.That(analysis.GetLabel(0, 0), Is.EqualTo(PortraitRegionLabel.BackdropCandidate));
            Assert.That(analysis.GetLabel(32, 12), Is.EqualTo(PortraitRegionLabel.RetainedForeground));
            Assert.That(analysis.GetLabel(32, 32), Is.EqualTo(PortraitRegionLabel.Face));
            Assert.That(analysis.GetLabel(0, 70), Is.EqualTo(PortraitRegionLabel.RetainedForeground));
            Assert.That(analysis.GetLabel(17, 35), Is.EqualTo(PortraitRegionLabel.Unknown));
            Assert.That(analysis.Statistics.Sum(region => region.PixelCount), Is.EqualTo(64 * 80));
            Assert.That(analysis.MeasurementBasis, Does.Contain("RGB pixel channels"));
        });
        var remaining = new byte[original.Length];
        image.CopyPixelDataTo(remaining);
        Assert.That(remaining, Is.EqualTo(original));
        var labels = analysis.CopyLabels();
        var mask = analysis.CopyCandidateMask();
        labels[0] = 0;
        mask[0] = 0;
        Assert.That(analysis.GetLabel(0, 0), Is.EqualTo(PortraitRegionLabel.BackdropCandidate));
        Assert.That(analysis.CopyCandidateMask()[0], Is.EqualTo(1));
    }

    [Test]
    public void ConstantPixelRegionsHaveFiniteStatisticsAndExplicitUndefinedCorrelation()
    {
        using var image = new Image<Rgba32>(64, 80, new Rgba32(200, 120, 80, 255));
        var result = PortraitBackdropAnalyzer.Analyze(image, Mask(64, 80, new Rectangle(24, 25, 16, 25)), Protection());
        Assert.That(result.IsSuccess, Is.True);
        foreach (var region in result.Value.Statistics)
        {
            foreach (var component in new[] { region.Red, region.Green, region.Blue })
            {
                Assert.That(double.IsFinite(component.Mean), Is.True);
                Assert.That(component.Variance, Is.Zero);
                Assert.That(component.QuantizedEntropyBits, Is.Zero);
                Assert.That(component.MeanNeighborGradient, Is.Zero);
                Assert.That(component.EdgeDensity, Is.Zero);
            }
            foreach (var correlation in new[] { region.RedGreenCorrelation, region.RedBlueCorrelation, region.GreenBlueCorrelation })
            {
                Assert.That(correlation.Status, Is.EqualTo(PixelCorrelationStatus.Undefined));
                Assert.That(correlation.Value, Is.Zero);
            }
        }
    }

    [Test]
    public void NativeGradientsAvoidArtificialSceneMaskEdges()
    {
        using var image = new Image<Rgba32>(64, 80, new Rgba32(170, 180, 190, 255));
        var result = PortraitBackdropAnalyzer.Analyze(image, Mask(64, 80, new Rectangle(24, 25, 16, 25)), Protection());
        var backdrop = result.Value.Statistics.Single(region => region.Label == PortraitRegionLabel.BackdropCandidate);
        Assert.That(backdrop.PixelCount, Is.GreaterThan(0));
        Assert.That(backdrop.Red.MeanNeighborGradient, Is.Zero);
        Assert.That(backdrop.Green.EdgeDensity, Is.Zero);
        Assert.That(backdrop.Blue.Mean, Is.EqualTo(190));
    }

    [Test]
    public void BusyBackdropKeepsItsSceneEvidenceUndetermined()
    {
        using var image = new Image<Rgba32>(64, 80);
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var value = (byte)((x + y) % 2 == 0 ? 0 : 255);
            image[x, y] = new Rgba32(value, value, value, 255);
        }
        var result = PortraitBackdropAnalyzer.Analyze(image, Mask(64, 80, new Rectangle(24, 25, 16, 25)), Protection());
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Presence, Is.EqualTo(PortraitBackdropPresenceStatus.Undetermined));
        Assert.That(result.Value.CandidatePixelCount, Is.Zero);
        Assert.That(result.Value.GetLabel(0, 0), Is.EqualTo(PortraitRegionLabel.Unknown));
    }

    [TestCase((byte)0)]
    [TestCase((byte)128)]
    public void TranslucentScenePixelsRetainUndeterminedColorEvidence(byte alpha)
    {
        using var image = new Image<Rgba32>(64, 80, new Rgba32(200, 200, 200, alpha));
        var result = PortraitBackdropAnalyzer.Analyze(image, Mask(64, 80, new Rectangle(24, 25, 16, 25)), Protection());
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Presence, Is.EqualTo(PortraitBackdropPresenceStatus.Undetermined));
        Assert.That(result.Value.CandidatePixelCount, Is.Zero);
        Assert.That(result.Value.GetLabel(0, 0), Is.EqualTo(PortraitRegionLabel.Unknown));
        Assert.That(image[0, 0].A, Is.EqualTo(alpha));
    }

    [Test]
    public void FullyProtectedAndTinySourcesReturnUndeterminedEvidence()
    {
        foreach (var size in new[] { 2, 64 })
        {
            using var image = new Image<Rgba32>(size, size, new Rgba32(220, 220, 220, 255));
            var face = Mask(size, size, new Rectangle(0, 0, 1, 1));
            var protection = new PortraitForegroundProtection(size)
            { RetainedHeadEnvelope = new Rectangle(0, 0, size, size) };
            var result = PortraitBackdropAnalyzer.Analyze(image, face, protection);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Presence, Is.EqualTo(PortraitBackdropPresenceStatus.Undetermined));
            Assert.That(result.Value.CandidatePixelCount, Is.Zero);
            Assert.That(result.Value.CandidateColorConsistency, Is.Zero);
        }
    }

    [Test]
    public void ExplicitHeadAndShoulderMasksExtendRetainedForeground()
    {
        using var image = new Image<Rgba32>(64, 80, new Rgba32(200, 200, 200, 255));
        var protection = new PortraitForegroundProtection(55)
        {
            RetainedHeadMask = Mask(64, 80, new Rectangle(18, 10, 28, 45)),
            ShoulderMask = Mask(64, 80, new Rectangle(2, 40, 10, 20))
        };
        var result = PortraitBackdropAnalyzer.Analyze(image, Mask(64, 80, new Rectangle(24, 25, 16, 25)), protection);
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.GetLabel(18, 11), Is.EqualTo(PortraitRegionLabel.RetainedForeground));
        Assert.That(result.Value.GetLabel(3, 42), Is.EqualTo(PortraitRegionLabel.RetainedForeground));
        Assert.That(result.Value.GetLabel(0, 0), Is.EqualTo(PortraitRegionLabel.BackdropCandidate));
    }

    [Test]
    public void CorrelationsUseChannelVarianceAndMarkConstantChannelsUndefined()
    {
        using var image = new Image<Rgba32>(64, 80);
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++) image[x, y] = new Rgba32((byte)(x * 3), (byte)(x * 2), 128, 255);
        var result = PortraitBackdropAnalyzer.Analyze(image, Mask(64, 80, new Rectangle(24, 25, 16, 25)), Protection());
        var face = result.Value.Statistics.Single(region => region.Label == PortraitRegionLabel.Face);
        Assert.That(face.RedGreenCorrelation.Status, Is.EqualTo(PixelCorrelationStatus.Defined));
        Assert.That(face.RedGreenCorrelation.Value, Is.EqualTo(1).Within(1e-12));
        Assert.That(face.RedBlueCorrelation.Status, Is.EqualTo(PixelCorrelationStatus.Undefined));
        Assert.That(face.Red.QuantizedEntropyBits, Is.GreaterThan(0));
    }

    [TestCase("gradient")]
    [TestCase("variance")]
    [TestCase("color")]
    [TestCase("edge")]
    [TestCase("fraction")]
    [TestCase("budget")]
    public void InvalidControlsFailBeforeAnalysis(string control)
    {
        using var image = new Image<Rgba32>(64, 80);
        var options = new PortraitBackdropOptions();
        options = control switch
        {
            "gradient" => options with { MaximumNeighborGradient = double.NaN },
            "variance" => options with { MaximumLocalVariance = double.PositiveInfinity },
            "color" => options with { MaximumColorDistance = -1 },
            "edge" => options with { EdgeDifferenceThreshold = 0 },
            "fraction" => options with { MinimumCandidateFraction = 2 },
            "budget" => options with { MaximumPixelCount = 100 },
            _ => throw new ArgumentOutOfRangeException(nameof(control))
        };
        var result = PortraitBackdropAnalyzer.Analyze(image, Mask(64, 80, new Rectangle(24, 25, 16, 25)), Protection(), options);
        Assert.That(result.IsFailure, Is.True);
    }

    [TestCase("missing")]
    [TestCase("both")]
    [TestCase("dimensions")]
    [TestCase("chin")]
    [TestCase("bounds")]
    public void ForegroundProtectionRequiresOneSourceSupportedBoundary(string invalid)
    {
        using var image = new Image<Rgba32>(64, 80);
        var protection = Protection();
        protection = invalid switch
        {
            "missing" => protection with { RetainedHeadEnvelope = null },
            "both" => protection with { RetainedHeadMask = Mask(64, 80, new Rectangle(18, 10, 28, 45)) },
            "dimensions" => protection with { RetainedHeadEnvelope = null, RetainedHeadMask = Mask(8, 8, new Rectangle(0, 0, 8, 8)) },
            "chin" => protection with { ChinY = float.NaN },
            "bounds" => protection with { RetainedHeadEnvelope = new Rectangle(18, 10, int.MaxValue, 45) },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };
        var result = PortraitBackdropAnalyzer.Analyze(image, Mask(64, 80, new Rectangle(24, 25, 16, 25)), protection);
        Assert.That(result.IsFailure, Is.True);
    }
}

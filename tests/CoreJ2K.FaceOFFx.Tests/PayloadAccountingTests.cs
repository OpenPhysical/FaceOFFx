using CoreJ2K.FaceOFFx.j2k.encoder;
using CoreJ2K.FaceOFFx.j2k.entropy.encoder;
using CoreJ2K.FaceOFFx.j2k.wavelet;
using CoreJ2K.FaceOFFx.j2k.wavelet.analysis;
using CoreJ2K.FaceOFFx.j2k.wavelet.synthesis;
using NUnit.Framework;

namespace CoreJ2K.FaceOFFx.Tests;

[TestFixture]
public class PayloadAccountingTests
{
    [Test]
    public void ZeroGainIntervalsReceiveZeroFaceCreditAndOnlyAggregateIsFloored()
    {
        var block = new CBlkRateDistStats
        {
            nVldTrunc = 3, nTotTrunc = 3, truncIdxs = new[] { 0, 1, 2 }, truncRates = new[] { 3, 8, 11 },
            SynthesisTotalGainsByPass = new[] { 2d, 2d, 6d }, SynthesisFaceGainsByPass = new[] { 1d, 1d, 2d }
        };
        Assert.That(EBCOTRateAllocator.AttributeSelectedPrefix(block, 2), Is.EqualTo(2.25));
        var rows = new[]
        {
            new SubbandPayloadAttribution(0, 0, 0, WaveletSubband.LL, 11, 2.25),
            new SubbandPayloadAttribution(0, 1, 0, WaveletSubband.LL, 11, 2.75)
        };
        var report = new SharedPayloadAttribution(rows, 100, 22);
        Assert.That(report.AttributedFacePayloadBytes, Is.EqualTo(5));
        rows[0] = new SubbandPayloadAttribution(0, 0, 0, WaveletSubband.LL, 11, 0);
        Assert.That(report.Subbands[0].FacePayloadByteEstimate, Is.EqualTo(2.25));
        Assert.That(((IList<SubbandPayloadAttribution>)report.Subbands).IsReadOnly, Is.True);
    }

    [TestCase(0, 0)]
    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(1, 1)]
    [TestCase(3, 5)]
    public void FiniteSynthesisEnergyMatchesIndependentTwoDimensionalImpulseReconstruction(int originX, int originY)
    {
        var root = Tree(25, 27, originX, originY, 3);
        var roi = new RoiOptions { Regions = new[] { RoiRegion.Rectangle(0, 0, 11, 27), RoiRegion.Rectangle(18, 7, 5, 8) } };
        var influence = new SynthesisEnergyInfluence(roi, 25, 27, originX, originY);
        foreach (var band in Leaves(root))
        {
            var energy = influence.GetBand(band);
            foreach (var (x, y) in new[] { (0, 0), (band.w - 1, band.h - 1), (band.w / 2, band.h / 2) }.Distinct())
            {
                var data = new float[root.w * root.h];
                data[(band.uly + y) * root.w + band.ulx + x] = 1;
                Reconstruct(root, data, root.w);
                double total = 0, face = 0;
                for (int yy = 0; yy < root.h; yy++)
                    for (int xx = 0; xx < root.w; xx++)
                    {
                        double value = data[yy * root.w + xx];
                        double squared = value * value;
                        total += squared;
                        if (roi.Regions.Any(region => region.Contains(xx, yy))) face += squared;
                    }
                int index = y * band.w + x;
                Assert.That(energy.Total[index], Is.EqualTo(total).Within(total * 2e-6 + 1e-12));
                Assert.That(energy.Face[index], Is.EqualTo(face).Within(total * 2e-6 + 1e-12));
            }
        }
    }

    [Test]
    public void FullRoiHasFullBasisEnergyAndPositivePassCreditIgnoresPerceptualUtility()
    {
        var root = Tree(17, 19, 0, 0, 2);
        var band = (SubbandAn)root.LL.LL;
        var roi = new RoiOptions { Regions = new[] { RoiRegion.Rectangle(0, 0, 17, 19) } };
        var influence = new SynthesisEnergyInfluence(roi, 17, 19, 0, 0);
        var energy = influence.GetBand(band);
        for (int i = 0; i < energy.Total.Length; i++)
            Assert.That(energy.Face[i], Is.EqualTo(energy.Total[i]).Within(energy.Total[i] * 1e-12));
        var block = new CBlkWTDataInt { sb = band, w = 1, h = 1, scanw = 1, ulx = band.ulx, uly = band.uly };
        var neutral = new SynthesisPassGainAccumulator(influence);
        var weighted = new SynthesisPassGainAccumulator(influence);
        neutral.Reset(block); weighted.Reset(block);
        neutral.Add(1, 100, 0); weighted.Add(1, 100, 0);
        neutral.Add(1, -40, 0); weighted.Add(1, -40, 0);
        neutral.EndPass(0, 4); weighted.EndPass(0, 4);
        Assert.That(weighted.TotalByPass[0], Is.EqualTo(neutral.TotalByPass[0]));
        Assert.That(weighted.FaceByPass[0], Is.EqualTo(neutral.FaceByPass[0]).Within(neutral.TotalByPass[0] * 1e-12));
        Assert.That(neutral.TotalByPass[0], Is.EqualTo(400 * energy.Total[0]));
    }

    [Test]
    public void CachedBandLimitPreservesExistingGeometryAndRejectsAdditionalIdentities()
    {
        var roi = new RoiOptions { Regions = new[] { RoiRegion.Rectangle(0, 0, 2, 1) } };
        var influence = new SynthesisEnergyInfluence(roi, 2, 1, 0, 0, maximumCachedBands: 1);
        var first = influence.GetBand(Tree(1, 1, 0, 0, 0));
        Assert.That(influence.GetBand(Tree(1, 1, 0, 0, 0)), Is.SameAs(first));
        Assert.Throws<NotSupportedException>(() => influence.GetBand(Tree(1, 1, 1, 0, 0)));
        Assert.That(influence.GetBand(Tree(1, 1, 0, 0, 0)), Is.SameAs(first));
    }

    [Test]
    public void CanonicalPartitionsAreIndependentOfCandidateHullAndHandleEqualOrDecreasingRates()
    {
        var block = new CBlkRateDistStats
        {
            nTotTrunc = 6, nVldTrunc = 2, truncIdxs = new[] { 0, 5 }, truncRates = new[] { 2, 4, 4, 3, 7, 9 },
            SynthesisTotalGainsByPass = new[] { 2d, 4d, 6d, 8d, 10d, 12d },
            SynthesisFaceGainsByPass = new[] { 1d, 1d, 2d, 4d, 5d, 6d }
        };
        double first = EBCOTRateAllocator.AttributeSelectedPrefix(block, 1);
        block.truncIdxs = Enumerable.Range(0, 6).ToArray(); block.nVldTrunc = 6;
        Assert.That(EBCOTRateAllocator.AttributeSelectedPrefix(block, 5), Is.EqualTo(first));
        // Canonical endpoints are passes0,3,4,5 with byte increments2,1,4,2.
        Assert.That(first, Is.EqualTo(1 + 0.5 + 2 + 1));
    }

    [Test]
    public void DecoderDiscardedRoiRefinementHasZeroGainWhileBackgroundRefinementRemains()
    {
        var root = Tree(17, 19, 0, 0, 2);
        var band = (SubbandAn)root.LL.LL;
        var roi = new RoiOptions { Regions = new[] { RoiRegion.Rectangle(0, 0, 17, 19) } };
        var influence = new SynthesisEnergyInfluence(roi, 17, 19, 0, 0);
        var energy = influence.GetBand(band);
        var block = new CBlkWTDataInt
        {
            sb = band, w = 2, h = 1, scanw = 2, ulx = band.ulx, uly = band.uly,
            RoiShift = 2, nROIbp = 3
        };
        var gains = new SynthesisPassGainAccumulator(influence);
        gains.Reset(block, roiIntegerPassCount: 1);
        gains.Add(1 << 28, 160, 0);
        gains.EndPass(0, 1);
        double integerGain = gains.TotalByPass[0];
        Assert.That(integerGain, Is.EqualTo(10 * energy.Total[0]));
        gains.Add(1 << 28, 160, 0);
        gains.Add((1 << 28) | int.MinValue, 160, 0);
        gains.EndPass(1, 0.25);
        Assert.That(gains.TotalByPass[1], Is.EqualTo(integerGain));
        Assert.That(gains.FaceByPass[1], Is.EqualTo(gains.FaceByPass[0]));
        gains.Add(15 << 24, 40, 1);
        gains.EndPass(2, 0.25);
        Assert.That(gains.TotalByPass[2], Is.EqualTo(integerGain + 10 * energy.Total[1]));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void PromotionKeepsNormallyDecodedRefinementEvenAfterIntegerPhaseCounter(bool wholeBand, bool aligned)
    {
        var root = Tree(17, 19, 0, 0, 2);
        var band = (SubbandAn)root.LL.LL;
        var roi = new RoiOptions { Regions = new[] { RoiRegion.Rectangle(0, 0, 17, 19) } };
        var influence = new SynthesisEnergyInfluence(roi, 17, 19, 0, 0);
        var block = new CBlkWTDataInt
        {
            sb = band, w = 1, h = 1, scanw = 1, ulx = band.ulx, uly = band.uly,
            RoiShift = 2, nROIbp = 3, RoiWholeBandPromotion = wholeBand,
            RoiBlockAlignedPromotion = aligned, RoiPromotionScale = 16
        };
        var gains = new SynthesisPassGainAccumulator(influence);
        gains.Reset(block, roiIntegerPassCount: 0);
        gains.Add(1 << 28, 160, 0); gains.EndPass(0, 1);
        Assert.That(gains.TotalByPass[0], Is.GreaterThan(0));
        gains.Add(1 << 28, 160, 0); gains.EndPass(1, 0.25);
        Assert.That(gains.TotalByPass[1], Is.GreaterThan(gains.TotalByPass[0]));
    }

    private static SubbandAn Tree(int width, int height, int originX, int originY, int levels) => new(width, height,
        originX, originY, levels, new WaveletFilter[] { new AnWTFilterFloatLift9x7() }, new WaveletFilter[] { new AnWTFilterFloatLift9x7() });

    private static IEnumerable<Subband> Leaves(Subband band)
    {
        if (!band.isNode) { if (band.w > 0 && band.h > 0) yield return band; yield break; }
        foreach (var child in new[] { band.LL, band.HL, band.LH, band.HH })
            foreach (var leaf in Leaves(child)) yield return leaf;
    }

    private static void Reconstruct(Subband band, float[] data, int stride)
    {
        if (!band.isNode) return;
        Reconstruct(band.LL, data, stride);
        var filter = new SynWTFilterFloatLift9x7();
        var input = new float[Math.Max(band.w, band.h)];
        for (int y = 0; y < band.h; y++)
        {
            int offset = (band.uly + y) * stride + band.ulx;
            Array.Copy(data, offset, input, 0, band.w);
            if ((band.ulcx & 1) == 0)
                filter.synthetize_lpf(input, 0, (band.w + 1) / 2, 1, input, (band.w + 1) / 2, band.w / 2, 1, data, offset, 1);
            else
                filter.synthetize_hpf(input, 0, band.w / 2, 1, input, band.w / 2, (band.w + 1) / 2, 1, data, offset, 1);
        }
        for (int x = 0; x < band.w; x++)
        {
            int offset = band.uly * stride + band.ulx + x;
            for (int y = 0; y < band.h; y++) input[y] = data[offset + y * stride];
            if ((band.ulcy & 1) == 0)
                filter.synthetize_lpf(input, 0, (band.h + 1) / 2, 1, input, (band.h + 1) / 2, band.h / 2, 1, data, offset, stride);
            else
                filter.synthetize_hpf(input, 0, band.h / 2, 1, input, band.h / 2, (band.h + 1) / 2, 1, data, offset, stride);
        }
    }
}

using AwesomeAssertions;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public class Jpeg2000SharedPayloadAttributionTests
{
    [Test]
    public void Snapshot_PreservesFractionalCreditAndConservesWholeBytes()
    {
        var rows = new[] { Row(0, Jpeg2000WaveletSubband.LL, 30, 20.5), Row(1, Jpeg2000WaveletSubband.HL, 70, 19.9) };
        var evidence = new Jpeg2000SharedPayloadAttribution(rows, 320, 100, 40.4, 40);
        rows[0] = default;

        evidence.Subbands[0].PayloadBytes.Should().Be(30);
        evidence.AttributedFacePayloadBytes.Should().Be(40);
        evidence.OutsidePayloadBytes.Should().Be(60);
        (evidence.AttributedFacePayloadBytes + evidence.OutsidePayloadBytes).Should().Be(100);
        evidence.MethodId.Should().Be("synthesis-energy-decoder-effective-pass-v3");
        ((IList<Jpeg2000SubbandAttribution>)evidence.Subbands).IsReadOnly.Should().BeTrue();
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    [TestCase(-1)]
    [TestCase(101)]
    public void Credit_RequiresFiniteBoundedEstimate(double estimate)
    {
        Action action = () => new Jpeg2000SharedPayloadAttribution(new[] { Row(0, Jpeg2000WaveletSubband.LL, 100, estimate) },
            320, 100, estimate, 40);
        action.Should().Throw<ArgumentException>();
    }

    [TestCase(39)]
    [TestCase(41)]
    public void Credit_RequiresSingleDownwardRounding(long bytes)
    {
        Action action = () => new Jpeg2000SharedPayloadAttribution(new[] { Row(0, Jpeg2000WaveletSubband.LL, 100, 40.4) },
            320, 100, 40.4, bytes);
        action.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Rows_RequireUniqueKeysAndExactPayloadClosure()
    {
        var row = Row(0, Jpeg2000WaveletSubband.LL, 100, 40);
        Action duplicate = () => new Jpeg2000SharedPayloadAttribution(new[] { row, row }, 320, 200, 80, 80);
        Action payload = () => new Jpeg2000SharedPayloadAttribution(new[] { row }, 320, 101, 40, 40);
        Action estimate = () => new Jpeg2000SharedPayloadAttribution(new[] { row }, 320, 100, 41, 41);
        duplicate.Should().Throw<ArgumentException>();
        payload.Should().Throw<ArgumentException>();
        estimate.Should().Throw<ArgumentException>();
    }

    [TestCase(-1, 0, 0, Jpeg2000WaveletSubband.LL)]
    [TestCase(0, 3, 0, Jpeg2000WaveletSubband.LL)]
    [TestCase(0, 0, 17, Jpeg2000WaveletSubband.HH)]
    [TestCase(0, 0, 1, Jpeg2000WaveletSubband.LL)]
    [TestCase(0, 0, 0, Jpeg2000WaveletSubband.HL)]
    public void Rows_RequireValidBandKeys(int tile, int component, int resolution, Jpeg2000WaveletSubband band)
    {
        var row = new Jpeg2000SubbandAttribution(tile, component, resolution, band, 100, 40);
        Action action = () => new Jpeg2000SharedPayloadAttribution(new[] { row }, 320, 100, 40, 40);
        action.Should().Throw<ArgumentException>();
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Mask_RequiresPositiveArea(long pixels)
    {
        Action action = () => new Jpeg2000SharedPayloadAttribution(Array.Empty<Jpeg2000SubbandAttribution>(), pixels, 0, 0, 0);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [TestCase(39, false)]
    [TestCase(40, true)]
    [TestCase(41, true)]
    public void RegionalRatio_UsesConservedFaceCreditAtExactBoundary(long faceBytes, bool meetsLimit)
    {
        var report = new Jpeg2000SharedPayloadAttribution(new[] { Row(0, Jpeg2000WaveletSubband.LL, 100, faceBytes) },
            320, 100, faceBytes, faceBytes);
        var verification = RegionalCompressionVerification.MeasuredPiv(320, report, Telemetry(100));

        verification.MeetsLimit.Should().Be(meetsLimit);
        verification.Status.Should().Be(meetsLimit ? RegionalCompressionVerificationStatus.MeetsLimit : RegionalCompressionVerificationStatus.ExceedsLimit);
        verification.MeasuredCompressionRatio.Should().Be(960.0 / faceBytes);
        verification.AttributionMethod.Should().Be(report.MethodId);
        verification.AttributedPayloadBytes.Should().Be(faceBytes);
    }

    [Test]
    public void ZeroCredit_KeepsRatioSerializableAndFailsLimit()
    {
        var report = new Jpeg2000SharedPayloadAttribution(new[] { Row(0, Jpeg2000WaveletSubband.LL, 100, 0) }, 320, 100, 0, 0);
        var verification = RegionalCompressionVerification.MeasuredPiv(320, report, Telemetry(100));

        verification.MeetsLimit.Should().BeFalse();
        verification.Status.Should().Be(RegionalCompressionVerificationStatus.ExceedsLimit);
        verification.MeasuredCompressionRatio.Should().BeNull();
    }

    [Test]
    public void RegionalMeasurement_RequiresMatchingMaskAndBandPayload()
    {
        var report = new Jpeg2000SharedPayloadAttribution(new[] { Row(0, Jpeg2000WaveletSubband.LL, 100, 40) }, 320, 100, 40, 40);
        Action mask = () => RegionalCompressionVerification.MeasuredPiv(321, report, Telemetry(100));
        Action payload = () => RegionalCompressionVerification.MeasuredPiv(320, report, Telemetry(101));
        var otherBand = new Jpeg2000PayloadTelemetry(new[]
        {
            new Jpeg2000SubbandPayload(0, 1, 0, Jpeg2000WaveletSubband.LL, 1, 100, 0, 100, 0, 0, 0, 0)
        }, 122, 85, 10, 10, 2, 1);
        Action band = () => RegionalCompressionVerification.MeasuredPiv(320, report, otherBand);

        mask.Should().Throw<ArgumentException>();
        payload.Should().Throw<ArgumentException>();
        band.Should().Throw<ArgumentException>();
    }

    private static Jpeg2000SubbandAttribution Row(int resolution, Jpeg2000WaveletSubband band, long bytes, double face) =>
        new(0, 0, resolution, band, bytes, face);

    private static Jpeg2000PayloadTelemetry Telemetry(int body) => new(new[]
    {
        new Jpeg2000SubbandPayload(0, 0, 0, Jpeg2000WaveletSubband.LL, 1, body, 0, body, 0, 0, 0, 0)
    }, body + 22, 85, 10, 10, 2, 1);
}

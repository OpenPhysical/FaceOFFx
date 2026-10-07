using AwesomeAssertions;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public class Jpeg2000PayloadTelemetryTests
{
    [Test]
    public void Accounting_ReconcilesDisjointPayloadAndCompleteOutput()
    {
        var telemetry = Create(ValidRow());

        telemetry.PacketBodyBytes.Should().Be(100);
        telemetry.ProtectedMaxshiftPhaseBytes.Should().Be(10);
        telemetry.WholeBandPromotedBytes.Should().Be(70);
        telemetry.CodeBlockPromotedBytes.Should().Be(5);
        telemetry.OutsideRoiCodeBlockBytes.Should().Be(8);
        telemetry.MixedOrRefinementBytes.Should().Be(7);
        telemetry.ProtectedPhaseBytesInMixedCodeBlocks.Should().Be(6);
        telemetry.TotalOutputBytes.Should().Be(200);
        telemetry.EmittedCodeBlockCount.Should().Be(3);
    }

    [Test]
    public void Accounting_OwnsReadOnlyRows()
    {
        var rows = new[] { ValidRow() };
        var telemetry = new Jpeg2000PayloadTelemetry(rows, 115, 85, 10, 3, 2, 1);
        rows[0] = rows[0] with { PayloadBytes = 0 };

        telemetry.Subbands[0].PayloadBytes.Should().Be(100);
        Action mutation = () => ((IList<Jpeg2000SubbandPayload>)telemetry.Subbands)[0] = ValidRow();
        mutation.Should().Throw<NotSupportedException>();
    }

    [Test]
    public void Accounting_RequiresExactCategoryAndCodestreamClosure()
    {
        Action categoryMismatch = () => Create(ValidRow() with { WholeBandPromotedBytes = 69 });
        Action headerMismatch = () => new Jpeg2000PayloadTelemetry(new[] { ValidRow() }, 114, 85, 10, 3, 2, 1);
        Action repeatedKey = () => new Jpeg2000PayloadTelemetry(new[] { ValidRow(), ValidRow() }, 215, 85, 10, 3, 2, 1);

        categoryMismatch.Should().Throw<ArgumentException>();
        headerMismatch.Should().Throw<ArgumentException>();
        repeatedKey.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Accounting_RequiresValidCountersAndMixedPhaseSubset()
    {
        Action negativeCount = () => Create(ValidRow() with { EmittedCodeBlockCount = -1 });
        Action invalidSubset = () => Create(ValidRow() with { ProtectedPhaseBytesInMixedCodeBlocks = 11 });
        Action invalidBand = () => Create(ValidRow() with { Subband = Jpeg2000WaveletSubband.HL });
        Action invalidEndMarker = () => new Jpeg2000PayloadTelemetry(new[] { ValidRow() }, 114, 85, 10, 3, 1, 1);

        negativeCount.Should().Throw<ArgumentException>();
        invalidSubset.Should().Throw<ArgumentException>();
        invalidBand.Should().Throw<ArgumentException>();
        invalidEndMarker.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static Jpeg2000SubbandPayload ValidRow() =>
        new(0, 0, 0, Jpeg2000WaveletSubband.LL, 3, 100, 10, 70, 5, 8, 7, 6);

    private static Jpeg2000PayloadTelemetry Create(Jpeg2000SubbandPayload row) =>
        new(new[] { row }, 115, 85, 10, 3, 2, 1);
}

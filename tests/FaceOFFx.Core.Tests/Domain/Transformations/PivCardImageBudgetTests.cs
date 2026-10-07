using AwesomeAssertions;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public class PivCardImageBudgetTests
{
    [Test]
    public void DefaultBudget_ReservesEveryRecordLayer()
    {
        var budget = new PivCardImageBudget();
        budget.MaximumJpeg2000Bytes.Should().Be(11_820);
        (budget.MaximumJpeg2000Bytes + budget.FacialRecordBytes + budget.CbeffBytes + budget.MaximumSignatureBytes)
            .Should().Be(12_704);
        budget.FitsSerializedObject(12_704, 12_710).Should().BeTrue();
        budget.FitsSerializedObject(12_705, 12_711).Should().BeFalse();
    }

    [Test]
    public void IssuerSignatureLength_ChangesTheExactImageAllowance()
    {
        new PivCardImageBudget(maximumSignatureBytes: 658).MaximumJpeg2000Bytes.Should().Be(11_912);
        new PivCardImageBudget(maximumSignatureBytes: 1_500).MaximumJpeg2000Bytes.Should().Be(11_070);
    }

    [Test]
    public void SmallerObjectCapacity_IsTheBindingLimit()
    {
        new PivCardImageBudget(maximumObjectBytes: 10_000).MaximumJpeg2000Bytes.Should().Be(9_110);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(12_600)]
    public void SignatureReservation_RequiresPositiveRemainingImageSpace(int signatureBytes)
    {
        Action construct = () => _ = new PivCardImageBudget(maximumSignatureBytes: signatureBytes);
        construct.Should().Throw<ArgumentException>();
    }
}

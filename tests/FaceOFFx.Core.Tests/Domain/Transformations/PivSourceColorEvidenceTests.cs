using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public class PivSourceColorEvidenceTests
{
    [Test]
    public void EvidenceRecordsTheProfileDigestAndOwnsItsRequirements()
    {
        var evidence = new PivSourceColorEvidence(PivSourceColorStatus.ConvertedMatrixRgb, new string('A', 64));
        Assert.That(evidence.EmbeddedProfileSha256, Is.EqualTo(new string('a', 64)));
        Assert.That(evidence.PixelsConverted, Is.True);
        Assert.That(evidence.VerificationRequirements, Has.Count.EqualTo(1));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)evidence.VerificationRequirements)[0] = "changed");
    }

    [Test]
    public void EmbeddedEvidenceRequiresAValidDigestAndDeclarationsUseTheirOwnBasis()
    {
        Assert.Throws<ArgumentException>(() => new PivSourceColorEvidence(PivSourceColorStatus.EmbeddedSrgb));
        Assert.Throws<ArgumentException>(() => new PivSourceColorEvidence(PivSourceColorStatus.EmbeddedSrgb, new string('z', 64)));
        Assert.Throws<ArgumentException>(() => new PivSourceColorEvidence(PivSourceColorStatus.AssumedSrgb, new string('a', 64)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PivSourceColorEvidence((PivSourceColorStatus)100));
        var assumed = new PivSourceColorEvidence(PivSourceColorStatus.AssumedSrgb);
        Assert.That(assumed.PixelsConverted, Is.False);
        Assert.That(assumed.Method, Is.EqualTo("untagged-srgb-assumption-v1"));
        Assert.That(assumed.VerificationRequirements.Single(), Does.Contain("assumption"));
    }
}

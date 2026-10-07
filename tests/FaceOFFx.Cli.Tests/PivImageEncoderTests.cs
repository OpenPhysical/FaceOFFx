using AwesomeAssertions;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;
using FaceOFFx.Tests.Common;

namespace FaceOFFx.Cli.Tests;

[TestFixture]
public class PivImageEncoderTests
{
    [Test]
    public void NamedTargets_AreImmutableCompleteJp2Ceilings()
    {
        PivFileSizeTarget.Minimum.MaximumBytes.Should().Be(11_820);
        PivFileSizeTarget.Preferred.MaximumBytes.Should().Be(22_000);
        PivFileSizeTarget.FromBytes(11_820).Should().BeSameAs(PivFileSizeTarget.Minimum);
        PivFileSizeTarget.FromBytes(22_000).Should().BeSameAs(PivFileSizeTarget.Preferred);
        PivFileSizeTarget.FromBytes(12_500).MaximumBytes.Should().Be(12_500);
        typeof(PivFileSizeTarget).GetProperties().Where(property => property.SetMethod is not null)
            .Should().BeEmpty();
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(int.MaxValue)]
    public void InvalidTarget_FailsBeforeEncoding(int bytes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PivFileSizeTarget.FromBytes(bytes));

    [Test]
    public void CustomTarget_AllowsTheExactWrapperArithmeticBoundary()
    {
        PivFileSizeTarget.FromBytes(int.MaxValue - 892).MaximumBytes.Should().Be(int.MaxValue - 892);
        PivFileSizeTarget.FromBytes(int.MaxValue - 892).RequiredObjectBytes.Should().Be(int.MaxValue);
        Assert.Throws<ArgumentOutOfRangeException>(() => PivFileSizeTarget.FromBytes(int.MaxValue - 891));
    }

    [TestCase(11_820, 12_704, 12_710)]
    [TestCase(22_000, 22_884, 22_890)]
    [TestCase(64_651, 65_535, 65_541)]
    [TestCase(64_652, 65_536, 65_543)]
    [TestCase(16_776_331, 16_777_215, 16_777_222)]
    [TestCase(16_776_332, 16_777_216, 16_777_224)]
    public void CapacityReservation_AccountsForBerLengthThresholds(int cap, int biometricBytes, int objectBytes)
    {
        var target = PivFileSizeTarget.FromBytes(cap);
        target.RequiredBiometricValueBytes.Should().Be(biometricBytes);
        target.RequiredObjectBytes.Should().Be(objectBytes);
    }

    [Test]
    public void PublicEntryPoints_AcceptOnlyImageTargetAndCancellation()
    {
        foreach (var method in typeof(PivImageEncoder).GetMethods().Where(method =>
                     method.Name is nameof(PivImageEncoder.EncodeAsync) or nameof(PivImageEncoder.EncodeFileAsync)))
        {
            method.GetParameters().Select(parameter => parameter.ParameterType).Should().Equal(
                method.Name == nameof(PivImageEncoder.EncodeAsync) ? typeof(byte[]) : typeof(string),
                typeof(PivFileSizeTarget), typeof(CancellationToken));
            method.GetParameters()[1].IsOptional.Should().BeTrue();
        }
        typeof(PivImageEncoder).Assembly.GetType("FaceOFFx.PivEncodingOptions").Should().BeNull();
        typeof(PivImageEncoder).Assembly.GetType("FaceOFFx.PivEncodingTweaks").Should().BeNull();
        typeof(PivEncodingResult).GetProperty("Profile").Should().BeNull();
        typeof(PivEncodingResult).GetProperties().Should().OnlyContain(property => property.SetMethod == null);
    }

    [TestCase(11_820)]
    [TestCase(22_000)]
    [TestCase(12_500)]
    [NonParallelizable]
    public async Task Library_UsesTheFixedRecipeAndRecordsCapacityAndColorReview(int maximumBytes)
    {
        var source = await File.ReadAllBytesAsync(PeopleCorpus.WatermarkedCardholderSource());
        var before = source.ToArray();
        using var encoder = new PivImageEncoder();
        var target = PivFileSizeTarget.FromBytes(maximumBytes);
        var result = await encoder.EncodeAsync(source, target);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        var value = result.Value;
        value.ImageData.Length.Should().BeLessThanOrEqualTo(maximumBytes);
        value.FileSizeTarget.Should().BeSameAs(target);
        value.MimeType.Should().Be("image/jp2");
        value.Encoding.TargetFileSize.Value.Should().Be(maximumBytes);
        value.Encoding.CardBudget!.MaximumBiometricValueBytes.Should().Be(maximumBytes + 884);
        value.Encoding.CardBudget.MaximumObjectBytes.Should().Be(maximumBytes + 890);
        value.SourceColorEvidence!.Status.Should().Be(PivSourceColorStatus.AssumedSrgb);
        value.Encoding.RegionalCompressionVerification.Should().NotBeNull();
        value.VerificationRequirements.Should().Contain(requirement => requirement.Contains("issuer signature"));
        value.VerificationRequirements.Should().Contain(requirement => requirement.Contains("operational shared-wavelet"));
        source.Should().Equal(before);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)value.VerificationRequirements)[0] = "changed");
    }

    [Test]
    public async Task EmptyInput_ReturnsInputErrorBeforeModelInitialization()
    {
        using var encoder = new PivImageEncoder();
        (await encoder.EncodeAsync([])).IsFailure.Should().BeTrue();
        (await encoder.EncodeFileAsync(string.Empty)).IsFailure.Should().BeTrue();
    }

    [Test]
    public void Cancellation_PropagatesBeforeSourceProcessing()
    {
        using var encoder = new PivImageEncoder();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(() =>
            encoder.EncodeAsync([1], cancellationToken: cancellation.Token));
        Assert.ThrowsAsync<OperationCanceledException>(() =>
            encoder.EncodeFileAsync("source.jpg", cancellationToken: cancellation.Token));
    }

    [Test]
    public async Task InvalidFilePath_ReturnsTypedInputError()
    {
        using var encoder = new PivImageEncoder();
        (await encoder.EncodeFileAsync("source\0.jpg")).IsFailure.Should().BeTrue();
    }

    [Test]
    public void Dispose_ReleasesOwnedServicesAndSupportsRepeatedCalls()
    {
        var encoder = new PivImageEncoder();
        encoder.Dispose();
        encoder.Dispose();
        Assert.ThrowsAsync<ObjectDisposedException>(() => encoder.EncodeAsync([1]));
    }

    [Test]
    [NonParallelizable]
    public async Task ConcurrentDisposal_AllowsQueuedCallsToUnwindThroughTheFacade()
    {
        var bytes = await File.ReadAllBytesAsync(PeopleCorpus.WatermarkedCardholderSource());
        var encoder = new PivImageEncoder();
        var calls = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await encoder.EncodeAsync(bytes, PivFileSizeTarget.Preferred); }
            catch (ObjectDisposedException exception) { exception.ObjectName.Should().Contain(nameof(PivImageEncoder)); }
        })).ToArray();
        var disposals = Enumerable.Range(0, 4).Select(_ => Task.Run(encoder.Dispose)).ToArray();
        await Task.WhenAll(calls.Concat(disposals));
        encoder.Dispose();
    }
}

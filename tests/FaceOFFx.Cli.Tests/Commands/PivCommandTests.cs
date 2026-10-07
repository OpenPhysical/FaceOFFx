using System.Security.Cryptography;
using System.Text.Json;
using AwesomeAssertions;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Tests.Common;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests.Commands;

[TestFixture]
[NonParallelizable]
public class PivCommandTests : IntegrationTestBase
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task PrimaryAndAlias_WriteBoundedJp2AndRegionalEvidence(bool compatibilityAlias)
    {
        var source = PeopleCorpus.WatermarkedCardholderSource();
        var output = Path.Combine(TempDirectory, "portrait.jp2");
        var arguments = new List<string>();
        if (compatibilityAlias) arguments.Add("piv");
        arguments.AddRange([source, "--filesize-target", "preferred", "--output", output, "--json"]);
        var result = await CliProcessHarness.Run(arguments.ToArray());
        result.ExitCode.Should().Be(0, result.Stderr);
        var encoded = await File.ReadAllBytesAsync(output);
        encoded.Length.Should().BeLessThanOrEqualTo(22_000);
        using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(output + ".json"));
        var root = report.RootElement;
        root.GetProperty("Profile").GetString().Should().Be("piv");
        root.GetProperty("AutomatedEncodingPassed").GetBoolean().Should().BeTrue();
        root.GetProperty("SourceSha256").GetString().Should().Be(
            Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(source))).ToLowerInvariant());
        root.GetProperty("ImageSha256").GetString().Should().Be(
            Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant());
        root.GetProperty("VerificationRequirements").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("RoiCoverage").GetProperty("Algorithm").GetString().Should().Be("landmark-face-hull-v1");
        root.GetProperty("SourceColorEvidence").GetProperty("Status").GetInt32().Should().Be((int)PivSourceColorStatus.AssumedSrgb);
        root.GetProperty("FileSizeTarget").GetProperty("MaximumBytes").GetInt32().Should().Be(22_000);
        var evidence = root.GetProperty("EncodingEvidence");
        var regional = evidence.GetProperty("RegionalCompressionVerification");
        regional.GetProperty("MeasuredCompressionRatio").GetDouble().Should().BeGreaterThan(0);
        regional.GetProperty("AttributionMethod").GetString().Should().NotBeNullOrEmpty();
        var codec = evidence.GetProperty("CodecEvidence");
        codec.TryGetProperty("Data", out _).Should().BeFalse();
        codec.GetProperty("PayloadTelemetry").GetProperty("TotalOutputBytes").GetInt64().Should().Be(encoded.Length);
        using var stdout = JsonDocument.Parse(result.Stdout);
        stdout.RootElement.GetProperty("AutomatedEncodingPassed").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task ExistingOutput_RequiresExplicitOverwriteAndPreservesSource()
    {
        var source = PeopleCorpus.WatermarkedCardholderSource();
        var output = Path.Combine(TempDirectory, "existing.jp2");
        byte[] sentinel = [1, 2, 3, 4];
        await File.WriteAllBytesAsync(output, sentinel);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(source));
        var result = await CliProcessHarness.Run(source, "--output", output);
        result.ExitCode.Should().NotBe(0);
        (await File.ReadAllBytesAsync(output)).Should().Equal(sentinel);
        SHA256.HashData(await File.ReadAllBytesAsync(source)).Should().Equal(before);
        File.Exists(output + ".json").Should().BeFalse();
    }

    [TestCase("--block-size", "16")]
    [TestCase("--start-level", "6")]
    [TestCase("--luma-utility", "0")]
    [TestCase("--size-profile", "other")]
    [TestCase("--face-region", "other")]
    [TestCase("--source-color", "other")]
    [TestCase("--filesize-target", "other")]
    [TestCase("--filesize-target", "0")]
    [TestCase("--filesize-target", "-1")]
    [TestCase("--filesize-target", "12,500")]
    [TestCase("--filesize-target", "12500.0")]
    [TestCase("--filesize-target", "2147483647")]
    public async Task InvalidEncodingControl_LeavesOutputUntouched(string option, string value)
    {
        var output = Path.Combine(TempDirectory, "invalid.jp2");
        var result = await CliProcessHarness.Run(PeopleCorpus.SubjectSource("carter", "jpg"),
            "--output", output, option, value);
        result.ExitCode.Should().NotBe(0);
        File.Exists(output).Should().BeFalse();
    }

    [Test]
    public async Task MinimumTarget_EncodesUntaggedSourceAndRetainsRegionalReview()
    {
        var output = Path.Combine(TempDirectory, "minimum.jp2");
        var result = await CliProcessHarness.Run(PeopleCorpus.WatermarkedCardholderSource(), "--output", output, "--json");
        result.ExitCode.Should().Be(0, result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("SourceColorEvidence").GetProperty("Status").GetInt32()
            .Should().Be((int)PivSourceColorStatus.AssumedSrgb);
        json.RootElement.GetProperty("VerificationRequirements").EnumerateArray().Select(value => value.GetString())
            .Should().Contain(value => value != null && value.Contains("operational shared-wavelet"));
        (await File.ReadAllBytesAsync(output)).Length.Should().BeLessThanOrEqualTo(11_820);
        File.Exists(output + ".json").Should().BeTrue();
    }

    [Test]
    public async Task NumericTarget_UsesTheSameLibraryByteCeiling()
    {
        var output = Path.Combine(TempDirectory, "custom.jp2");
        var result = await CliProcessHarness.Run(PeopleCorpus.WatermarkedCardholderSource(),
            "--filesize-target", "12500", "--output", output, "--json");
        result.ExitCode.Should().Be(0, result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("FileSizeTarget").GetProperty("MaximumBytes").GetInt32().Should().Be(12_500);
        (await File.ReadAllBytesAsync(output)).Length.Should().BeLessThanOrEqualTo(12_500);
    }
}

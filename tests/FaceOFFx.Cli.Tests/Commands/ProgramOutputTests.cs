using System.Text.Json;
using AwesomeAssertions;
using FaceOFFx.Tests.Common;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests.Commands;

[TestFixture]
[NonParallelizable]
public class ProgramOutputTests : IntegrationTestBase
{
    [Test]
    public async Task JsonOutput_ContainsOneUtf8ObjectAndRetainsSourceReviewRequirements()
    {
        var output = Path.Combine(TempDirectory, "json-output.jp2");
        var result = await CliProcessHarness.Run(PeopleCorpus.WatermarkedCardholderSource(),
            "--output", output, "--filesize-target", "preferred", "--json");
        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().NotBeEmpty();
        result.Stdout[0].Should().Be((byte)'{');
        result.Stdout.Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        using var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("Profile").GetString().Should().Be("piv");
        json.RootElement.GetProperty("AutomatedEncodingPassed").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("VerificationRequirements").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Test]
    public async Task JsonValidationFailure_ProducesAnErrorObject()
    {
        var result = await CliProcessHarness.Run(Path.Combine(TempDirectory, "missing.jpg"), "--json");
        result.ExitCode.Should().NotBe(0);
        using var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("Error").GetString().Should().Contain("source");
    }
}

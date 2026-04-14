using System.Text.Json;
using FaceOFFx.Cli.Commands;
using FaceOFFx.Cli.Tests;
using FaceOFFx.Tests.Common;
using FluentAssertions;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests.Commands;

[TestFixture]
public class QualityCommandTests : IntegrationTestBase
{
    private string _testImagePath = null!;
    private string _lowQualityImagePath = null!;

    [OneTimeSetUp]
    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();

        var currentDir = TestContext.CurrentContext.TestDirectory;
        var searchDir = new DirectoryInfo(currentDir);

        while (searchDir != null && !File.Exists(Path.Combine(searchDir.FullName, "FaceOFFx.sln")))
        {
            searchDir = searchDir.Parent;
        }

        if (searchDir == null)
        {
            throw new InvalidOperationException("Could not find solution root");
        }

        _testImagePath = Path.Combine(searchDir.FullName, "tests", "sample_images", "generic_guy.png");
        _lowQualityImagePath = Path.Combine(searchDir.FullName, "tests", "sample_images", "generic_guy.jpg");
    }

    [Test]
    public async Task QualityCommand_WithValidImage_ReturnsStructuredOutput()
    {
        var app = CliTestHarness.Create();

        var result = await app.RunAsync(new[] { "quality", "--input", _testImagePath });

        result.ExitCode.Should().Be(0);
        var output = result.Output;
        output.Should().Contain("Status:");
        output.Should().Contain("Overall Quality:");
        output.Should().Contain("Native Compliance:");
    }

    [Test]
    public async Task QualityCommand_WithJsonFormat_ReturnsValidJson()
    {
        var app = CliTestHarness.Create();

        var result = await app.RunAsync(new[] { "quality", "--input", _testImagePath, "--format", "json" });

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("Accepted").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("IsCompliant").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("OverallScore").GetDouble().Should().BeInRange(0, 1);
        json.RootElement.GetProperty("Scores").Should().NotBeNull();
        json.RootElement.GetProperty("Violations").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Test]
    public async Task QualityCommand_WithDetailedFormat_ShowsExtendedInfo()
    {
        var app = CliTestHarness.Create();

        var result = await app.RunAsync(new[] { "quality", "--input", _testImagePath, "--format", "detailed" });

        result.ExitCode.Should().Be(0);
        var output = result.Output;
        output.Should().Contain("Detailed Analysis");
        output.Should().Contain("Image Dimensions");
        output.Should().Contain("Assessment completed at");
    }

    [Test]
    public async Task QualityCommand_WithDifferentStandards_AppliesCorrectCriteria()
    {
        var standards = new[] { "piv", "twic", "icao", "cac" };

        foreach (var standard in standards)
        {
            var app = CliTestHarness.Create();

            var result = await app.RunAsync(new[] { "quality", "--input", _testImagePath, "--standard", standard });

            result.ExitCode.Should().Be(0);
            result.Output.Should().Contain("Overall Quality:");
        }
    }

    [Test]
    public async Task QualityCommand_WithThreshold_EnforcesMinimumScore()
    {
        var app = CliTestHarness.Create();

        var result = await app.RunAsync(new[] { "quality", "--input", _testImagePath, "--threshold", "0.9", "--format", "json" });

        result.ExitCode.Should().Be(1);
        var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("Accepted").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("OverallScore").GetDouble().Should().BeLessThan(0.9);
    }

    [Test]
    public async Task QualityCommand_WithStrictMode_AppliesStrictSettings()
    {
        var app = CliTestHarness.Create();

        var result = await app.RunAsync(new[] { "quality", "--input", _testImagePath, "--strict", "--format", "json" });

        result.ExitCode.Should().Be(1);
        var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("Accepted").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("IsCompliant").GetBoolean().Should().BeFalse();
    }

    [Test]
    public async Task QualityCommand_WithStrictIcao_PreservesRequestedStandardGeometry()
    {
        var app = CliTestHarness.Create();

        var result = await app.RunAsync(new[]
        {
            "quality", "--input", _testImagePath, "--strict", "--standard", "icao", "--format", "detailed"
        });

        result.Output.Should().Contain("Expected: 413×531");
    }

    [Test]
    public async Task QualityCommand_WithRequestedThreshold_ShowsRequestedGateFailureInTextOutput()
    {
        var app = CliTestHarness.Create();

        var result = await app.RunAsync(new[]
        {
            "quality", "--input", _testImagePath, "--threshold", "0.9"
        });

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Requested Gate:");
    }

    [Test]
    public async Task QualityCommand_WithOutputPath_SavesReportToFile()
    {
        var app = CliTestHarness.Create();
        var outputPath = Path.Combine(TempDirectory, "quality-report.json");

        var result = await app.RunAsync(new[] { "quality", "--input", _testImagePath, "--format", "json", "--output", outputPath });

        result.ExitCode.Should().Be(0);
        File.Exists(outputPath).Should().BeTrue();

        var fileContent = await File.ReadAllTextAsync(outputPath);
        var json = JsonDocument.Parse(fileContent);
        json.RootElement.GetProperty("Accepted").GetBoolean().Should().BeTrue();
        json.RootElement.GetProperty("IsCompliant").GetBoolean().Should().BeFalse();
    }

    [Test]
    public async Task QualityCommand_WithNonExistentFile_ReturnsError()
    {
        var app = CliTestHarness.Create();

        var result = await app.RunAsync(new[] { "quality", "--input", "non-existent-file.jpg" });

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Error: Input file not found");
    }

    [Test]
    public async Task QualityCommand_ShowsViolations_WhenNativeQualityIssuesExist()
    {
        var app = CliTestHarness.Create();

        var result = await app.RunAsync(new[] { "quality", "--input", _lowQualityImagePath });

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("Native Compliance: Fail");
        result.Output.Should().Contain("Violations:");
    }
}

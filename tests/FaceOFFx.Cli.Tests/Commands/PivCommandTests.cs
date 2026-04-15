using System.Diagnostics;
using System.Text.Json;
using AwesomeAssertions;
using FaceOFFx.Tests.Common;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests.Commands;

[TestFixture]
[NonParallelizable]
public class PivCommandTests : IntegrationTestBase
{
    private string _cliAssemblyPath = null!;
    private string _testImagePath = null!;

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

        _cliAssemblyPath = Path.Combine(
            searchDir.FullName,
            "artifacts",
            "bin",
            "FaceOFFx.Cli",
            "Debug",
            "net8.0",
            "faceoffx.dll");

        _testImagePath = PeopleCorpus.SubjectSource("generic-guy", "png");
    }

    [Test]
    public async Task PivRecipe_WithValidImage_WritesArtifactAndProvenance()
    {
        var outputDir = Path.Combine(TempDirectory, "piv-job");
        Directory.CreateDirectory(outputDir);

        var exitCode = await RunCliCommand(
            $"piv \"{_testImagePath}\" --output-dir \"{outputDir}\"");

        exitCode.Should().Be(0);
        File.Exists(Path.Combine(outputDir, "source.piv.jp2")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "source.piv.print.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "source.piv.provenance.json")).Should().BeTrue();

        var provenance = await File.ReadAllTextAsync(
            Path.Combine(outputDir, "source.piv.provenance.json"));

        provenance.Should().Contain("sp800-76-2-table12-note4");
        provenance.Should().Contain("fips201-3-4.2.3.1");
        provenance.Should().Contain("piv-output-geometry");
    }

    [Test]
    public async Task PivRecipe_WithJson_WritesMachineReadableStdout()
    {
        var outputDir = Path.Combine(TempDirectory, "piv-json-job");
        Directory.CreateDirectory(outputDir);

        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{_cliAssemblyPath}\" piv \"{_testImagePath}\" --output-dir \"{outputDir}\" --json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo)
            ?? throw new InvalidOperationException("Failed to start process");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        process.ExitCode.Should().Be(0);
        stdout.Should().NotContain("PIV · ICAO · TWIC Biometrics");
        using var json = JsonDocument.Parse(stdout);
        json.RootElement.GetProperty("Document").GetString().Should().Be("piv");
        json.RootElement.GetProperty("ProvenancePath").GetString().Should().Contain(".provenance.json");
        json.RootElement.GetProperty("Deliverables")[0].GetProperty("Passed").GetBoolean().Should().BeTrue();
    }

    [Test]
    public void DocumentsCommand_ListsSupportedDocumentWorkflows()
    {
        var tester = CliTestHarness.Create();

        var result = tester.Run("documents");

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("piv");
        result.Output.Should().Contain("us-passport");
        result.Output.Should().Contain("canada-perman");
        result.Output.Should().Contain("canada-proof-");
    }

    private async Task<int> RunCliCommand(string arguments)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{_cliAssemblyPath}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process =
            Process.Start(processInfo)
            ?? throw new InvalidOperationException("Failed to start process");

        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}

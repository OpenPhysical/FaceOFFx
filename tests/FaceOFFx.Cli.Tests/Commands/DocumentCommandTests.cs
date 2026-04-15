using System.Diagnostics;
using System.Text.Json;
using AwesomeAssertions;
using FaceOFFx.Tests.Common;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests.Commands;

[TestFixture]
[NonParallelizable]
public class DocumentCommandTests : IntegrationTestBase
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

    [TestCase("us-passport", null, "source.us-passport.print.jpeg")]
    [TestCase("us-permanent-resident", null, "source.us-permanent-resident.print.jpeg")]
    [TestCase("canada-passport", null, "source.canada-passport.print.jpeg")]
    [TestCase("canada-permanent-resident", null, "source.canada-permanent-resident.print.jpeg")]
    [TestCase("canada-citizenship-grant", null, "source.canada-citizenship-grant.print.jpeg")]
    [TestCase("canada-proof-of-citizenship", null, "source.canada-proof-of-citizenship.print.jpeg")]
    [TestCase("us-passport", "digital", "source.us-passport.digital.jpeg")]
    [TestCase("us-permanent-resident", "digital", "source.us-permanent-resident.digital.jpeg")]
    [TestCase("canada-permanent-resident", "digital", "source.canada-permanent-resident.digital.jpeg")]
    [TestCase("canada-citizenship-grant", "digital", "source.canada-citizenship-grant.digital.jpeg")]
    [TestCase("canada-proof-of-citizenship", "digital", "source.canada-proof-of-citizenship.digital.jpeg")]
    public async Task DocumentCommand_WritesExpectedArtifactAndProvenance(
        string command,
        string? variant,
        string expectedArtifactName)
    {
        var outputDir = Path.Combine(TempDirectory, $"{command}-{variant ?? "default"}");
        Directory.CreateDirectory(outputDir);

        var arguments = variant == null
            ? $"{command} \"{_testImagePath}\" --output-dir \"{outputDir}\""
            : $"{command} \"{_testImagePath}\" --output-dir \"{outputDir}\" --variant {variant}";

        var exitCode = await RunCliCommand(arguments);

        exitCode.Should().Be(0);
        File.Exists(Path.Combine(outputDir, expectedArtifactName)).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, $"source.{command}.provenance.json")).Should().BeTrue();

        if (command.StartsWith("canada-", StringComparison.OrdinalIgnoreCase) && string.Equals(variant, "digital", StringComparison.OrdinalIgnoreCase))
        {
            File.Exists(Path.Combine(outputDir, Path.GetFileNameWithoutExtension(expectedArtifactName) + ".supporting-info.txt")).Should().BeTrue();
        }
    }

    [Test]
    public async Task DocumentsCommand_ListsVariants()
    {
        var result = await RunCliCapture("documents");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("digital");
        result.Stdout.Should().Contain("standard");
        result.Stdout.Should().Contain("canada-proof-");
    }

    [TestCase("process")]
    [TestCase("quality")]
    [TestCase("validate")]
    public async Task RemovedExpertCommands_AreNoLongerAvailable(string command)
    {
        var result = await RunCliCapture(command);

        result.ExitCode.Should().NotBe(0);
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

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunCliCapture(string arguments)
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

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout, stderr);
    }
}

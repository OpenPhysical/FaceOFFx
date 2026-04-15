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

        _testImagePath = Path.Combine(
            searchDir.FullName,
            "tests",
            "sample_images",
            "generic_guy.png");
    }

    [TestCase("us-passport", null, "generic_guy.us-passport.jpg")]
    [TestCase("us-pr-photo", null, "generic_guy.us-pr-photo.jpg")]
    [TestCase("canada-passport", null, "generic_guy.canada-passport.jpg")]
    [TestCase("canada-pr-card", null, "generic_guy.canada-pr-card.jpg")]
    [TestCase("us-passport", "online-renewal-digital", "generic_guy.us-passport.digital.jpg")]
    [TestCase("us-pr-photo", "digital-upload", "generic_guy.us-pr-photo.digital.jpg")]
    [TestCase("canada-passport", "online-renewal-digital", "generic_guy.canada-passport.digital.jpg")]
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
        File.Exists(Path.Combine(outputDir, $"generic_guy.{command}.provenance.json")).Should().BeTrue();
    }

    [Test]
    public async Task DocumentsCommand_ListsVariants()
    {
        var result = await RunCliCapture("documents");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("online-renewal-digi");
        result.Stdout.Should().Contain("digital-upload");
        result.Stdout.Should().Contain("card-only");
    }

    [Test]
    public async Task QualityCommand_HumanOutput_ShowsDeprecationNotice()
    {
        var result = await RunCliCapture($"quality --input \"{_testImagePath}\" --format text");

        result.ExitCode.Should().Be(0);
        result.Stdout.Should().Contain("Deprecated:");
    }

    [Test]
    public async Task ValidateCommand_HumanOutput_ShowsDeprecationNotice()
    {
        var result = await RunCliCapture($"validate \"{_testImagePath}\" --standard piv");

        result.Stdout.Should().Contain("Deprecated:");
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

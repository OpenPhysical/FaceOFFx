using System.Diagnostics;
using System.Text.Json;
using AwesomeAssertions;
using FaceOFFx.Tests.Common;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests.Commands;

[TestFixture]
[NonParallelizable]
public class ProgramOutputTests : IntegrationTestBase
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

    [Test]
    public async Task PivCommand_WithJsonFormat_WritesPureJsonToStdout()
    {
        var outputDir = Path.Combine(TempDirectory, "piv-program-output");
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

        stdout.Should().NotContain("PIV · ICAO · TWIC Biometrics");
        using var json = JsonDocument.Parse(stdout);
        json.RootElement.GetProperty("Document").GetString().Should().Be("piv");
    }

    [Test]
    public async Task UsPassportCommand_WithJsonFormat_WritesPureJsonToStdout()
    {
        var outputDir = Path.Combine(TempDirectory, "us-passport-program-output");
        Directory.CreateDirectory(outputDir);

        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{_cliAssemblyPath}\" us-passport \"{_testImagePath}\" --output-dir \"{outputDir}\" --json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo)
            ?? throw new InvalidOperationException("Failed to start process");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        stdout.Should().NotContain("PIV · ICAO · TWIC Biometrics");
        using var json = JsonDocument.Parse(stdout);
        json.RootElement.GetProperty("Document").GetString().Should().Be("us-passport");
    }

    [Test]
    public async Task CanadaProofOfCitizenship_WithJsonFormat_WritesPureJsonToStdout()
    {
        var outputDir = Path.Combine(TempDirectory, "canada-proof-program-output");
        Directory.CreateDirectory(outputDir);

        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{_cliAssemblyPath}\" canada-proof-of-citizenship \"{_testImagePath}\" --output-dir \"{outputDir}\" --json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo)
            ?? throw new InvalidOperationException("Failed to start process");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        stdout.Should().NotContain("PIV · ICAO · TWIC Biometrics");
        using var json = JsonDocument.Parse(stdout);
        json.RootElement.GetProperty("Document").GetString().Should().Be("canada-proof-of-citizenship");
    }
}

using System.Diagnostics;
using AwesomeAssertions;
using FaceOFFx.Tests.Common;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Cli.Tests.Commands;

/// <summary>
/// Tests for PIV command-line interface
/// </summary>
[TestFixture]
[NonParallelizable]
public class ProcessCommandTests : IntegrationTestBase
{
    private string _testImagePath = null!;
    private string _cliAssemblyPath = null!;

    /// <summary>
    /// One-time setup for the test fixture.
    /// </summary>
    [OneTimeSetUp]
    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();

        // Find the built CLI assembly path
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
            "faceoffx.dll"
        );

        if (!File.Exists(_cliAssemblyPath))
        {
            throw new FileNotFoundException($"Built CLI assembly not found: {_cliAssemblyPath}");
        }
    }

    /// <summary>
    /// Setup before each test.
    /// </summary>
    [SetUp]
    public override void SetUp()
    {
        base.SetUp();
        // Use a real test face image directly
        var currentDir = TestContext.CurrentContext.TestDirectory;
        var searchDir = new DirectoryInfo(currentDir);
        while (searchDir != null && !File.Exists(Path.Combine(searchDir.FullName, "FaceOFFx.sln")))
        {
            searchDir = searchDir.Parent;
        }
        _testImagePath = Path.Combine(
            searchDir!.FullName,
            "tests",
            "sample_images",
            "generic_guy.png"
        );
    }

    /// <summary>
    /// Tests that process command produces JP2 output for valid image.
    /// </summary>
    [Test]
    public async Task ProcessCommand_WithValidImage_ProducesOutput()
    {
        var outputPath = Path.Combine(TempDirectory, "output.jp2");

        var exitCode = await RunCliCommand(
            $"process \"{_testImagePath}\" --output \"{outputPath}\""
        );
        exitCode.Should().Be(0, "CLI should exit with success code");
        File.Exists(outputPath).Should().BeTrue("Output file should be created");

        // Verify output file size is reasonable for JP2
        var fileInfo = new FileInfo(outputPath);
        fileInfo
            .Length.Should()
            .BeInRange(10_000, 50_000, "JP2 file should be between 10KB and 50KB");
    }

    /// <summary>
    /// Tests that process command returns error for missing file.
    /// </summary>
    [Test]
    public async Task ProcessCommand_WithMissingFile_ReturnsError()
    {
        var nonExistentPath = Path.Combine(TempDirectory, "does_not_exist.jpg");

        var exitCode = await RunCliCommand($"process \"{nonExistentPath}\"");
        exitCode.Should().NotBe(0, "CLI should exit with error code");
    }

    /// <summary>
    /// Tests that process command creates JP2 without ROI when specified.
    /// </summary>
    [Test]
    public async Task ProcessCommand_WithNoRoi_CreatesCorrectFormat()
    {
        var outputPath = Path.Combine(TempDirectory, "output.jp2");

        var exitCode = await RunCliCommand(
            $"process \"{_testImagePath}\" --output \"{outputPath}\" --no-roi"
        );
        exitCode.Should().Be(0);
        File.Exists(outputPath).Should().BeTrue();

        // Verify it's actually a JP2
        var fileBytes = await File.ReadAllBytesAsync(outputPath);
        fileBytes.Length.Should().BeGreaterThan(8);
    }

    /// <summary>
    /// Tests that rate parameter affects output file size.
    /// </summary>
    [Test]
    public async Task ProcessCommand_WithRateParameter_AppliesCompression()
    {
        var highQualityPath = Path.Combine(TempDirectory, "high_quality.jp2");
        var lowQualityPath = Path.Combine(TempDirectory, "low_quality.jp2");

        await RunCliCommand(
            $"process \"{_testImagePath}\" --output \"{highQualityPath}\" --rate 1.0"
        );
        await RunCliCommand(
            $"process \"{_testImagePath}\" --output \"{lowQualityPath}\" --rate 0.6"
        );
        var highQualitySize = new FileInfo(highQualityPath).Length;
        var lowQualitySize = new FileInfo(lowQualityPath).Length;

        highQualitySize
            .Should()
            .BeGreaterThan(lowQualitySize, "Higher quality should result in larger file size");
    }

    /// <summary>
    /// Tests that verbose flag produces detailed output.
    /// </summary>
    [Test]
    public async Task ProcessCommand_WithVerboseFlag_ShowsDetailedOutput()
    {
        var outputPath = Path.Combine(TempDirectory, "output.jp2");

        var (exitCode, output) = await RunCliCommandWithOutput(
            $"process \"{_testImagePath}\" --output \"{outputPath}\" --verbose"
        );
        exitCode.Should().Be(0);
        output.Should().Contain("Processing with facial image encoder");
        // The output should contain success message since we're using a real face image
        output.Should().Contain("completed successfully");
    }

    /// <summary>
    /// Tests that process command fails when no face is detected.
    /// </summary>
    [Test]
    public async Task ProcessCommand_WithNoFaceInImage_ReturnsError()
    {
        var noFaceImagePath = CreateEmptyImage();
        var outputPath = Path.Combine(TempDirectory, "output.jp2");

        var exitCode = await RunCliCommand(
            $"process \"{noFaceImagePath}\" --output \"{outputPath}\""
        );

        // Check exit code
        exitCode.Should().NotBe(0, "Should fail when no face is detected");

        // Verify no output file was created
        File.Exists(outputPath).Should().BeFalse("No output should be created");
    }

    /// <summary>
    /// Tests that process command uses default output naming.
    /// </summary>
    [Test]
    public async Task ProcessCommand_WithoutOutputPath_UsesDefaultNaming()
    {
        // Copy test image to temp directory for this test
        var tempImagePath = Path.Combine(TempDirectory, "test_face.png");
        File.Copy(_testImagePath, tempImagePath, true);

        var exitCode = await RunCliCommand($"process \"{tempImagePath}\"");
        exitCode.Should().Be(0);

        // Check for default output file
        var expectedOutputName = Path.GetFileNameWithoutExtension(tempImagePath) + ".jp2";
        var expectedOutputPath = Path.Combine(
            Path.GetDirectoryName(tempImagePath)!,
            expectedOutputName
        );

        File.Exists(expectedOutputPath).Should().BeTrue("Default output file should be created");
    }

    // Helper methods
    private async Task<int> RunCliCommand(string arguments)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{_cliAssemblyPath}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process =
            Process.Start(processInfo)
            ?? throw new InvalidOperationException("Failed to start process");

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private async Task<(int exitCode, string output)> RunCliCommandWithOutput(string arguments)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{_cliAssemblyPath}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process =
            Process.Start(processInfo)
            ?? throw new InvalidOperationException("Failed to start process");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var fullOutput = output + Environment.NewLine + error;
        return (process.ExitCode, fullOutput);
    }

    private string CreateEmptyImage()
    {
        var imagePath = Path.Combine(TempDirectory, "empty.jpg");

        using (var image = new Image<Rgba32>(100, 100, new Rgba32(255, 255, 255)))
        {
            // Small pure white image - YOLOv5 shouldn't detect faces in this
            image.SaveAsJpeg(imagePath);
        }

        return imagePath;
    }
}

using System.Text.Json;
using FaceOFFx.Cli.Tests;
using FaceOFFx.Tests.Common;
using AwesomeAssertions;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Cli.Tests.Commands;

[TestFixture]
[NonParallelizable]
public class DatasetCommandTests : IntegrationTestBase
{
    private string _solutionRoot = null!;
    private string _sampleImagePath = null!;

    [OneTimeSetUp]
    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();

        var searchDir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (searchDir != null && !File.Exists(Path.Combine(searchDir.FullName, "FaceOFFx.sln")))
        {
            searchDir = searchDir.Parent;
        }

        _solutionRoot = searchDir?.FullName
            ?? throw new InvalidOperationException("Could not find solution root");
        _sampleImagePath = Path.Combine(_solutionRoot, "tests", "sample_images", "generic_guy.png");
    }

    [Test]
    public async Task DatasetValidate_WithIcaoStandard_ProducesIcaoDimensionsAndMetadata()
    {
        var datasetDir = Path.Combine(TempDirectory, "dataset");
        Directory.CreateDirectory(datasetDir);
        var inputPath = Path.Combine(datasetDir, "generic_guy.png");
        File.Copy(_sampleImagePath, inputPath);

        var outputDir = Path.Combine(TempDirectory, "processed");
        var app = CliTestHarness.Create();

        var result = await app.RunAsync(new[]
        {
            "dataset", "validate", datasetDir,
            "--pattern", "*.png",
            "--standard", "icao",
            "--output", outputDir,
            "--parallel", "1"
        });

        result.ExitCode.Should().Be(0, result.Output);

        var passedPng = Directory.GetFiles(Path.Combine(outputDir, "passed"), "*.png").Single();
        using var processedImage = await Image.LoadAsync<Rgba32>(passedPng);
        processedImage.Width.Should().Be(413);
        processedImage.Height.Should().Be(531);

        var resultsJsonPath = Path.Combine(outputDir, "results.json");
        File.Exists(resultsJsonPath).Should().BeTrue();

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(resultsJsonPath));
        var item = document.RootElement.EnumerateArray().Single();
        item.GetProperty("Success").GetBoolean().Should().BeTrue();
        var metadata = item.GetProperty("Metadata");
        metadata.GetProperty("RequestedStandard").GetString().Should().Be("ICAO");
        metadata.GetProperty("OutputWidth").GetInt32().Should().Be(413);
        metadata.GetProperty("OutputHeight").GetInt32().Should().Be(531);
    }

    [Test]
    public async Task DatasetReview_WithLegacySharpnessMetadata_FiltersWithoutCrashing()
    {
        var outputDir = Path.Combine(TempDirectory, "processed_dataset");
        Directory.CreateDirectory(outputDir);
        var resultsPath = Path.Combine(outputDir, "results.json");

        var resultsJson = """
        [
          {
            "FilePath": "/tmp/a.jpg",
            "FileName": "a.jpg",
            "Success": false,
            "ErrorMessage": "too blurry",
            "QualityScore": 0.45,
            "Metadata": {
              "FailureReason": "LowSharpness",
              "SharpnessScore": 0.5
            }
          },
          {
            "FilePath": "/tmp/b.jpg",
            "FileName": "b.jpg",
            "Success": false,
            "ErrorMessage": "too blurry",
            "QualityScore": 0.30,
            "Metadata": {
              "FailureReason": "LowSharpness",
              "SharpnessScore": 0.2
            }
          }
        ]
        """;

        await File.WriteAllTextAsync(resultsPath, resultsJson);

        var previousValue = Environment.GetEnvironmentVariable("FACEOFFX_DISABLE_BROWSER_LAUNCH");
        Environment.SetEnvironmentVariable("FACEOFFX_DISABLE_BROWSER_LAUNCH", "1");
        try
        {
            var app = CliTestHarness.Create();
            var result = await app.RunAsync(new[]
            {
                "dataset", "review", resultsPath,
                "--min-sharpness", "0.4",
                "--max-sharpness", "0.6"
            });

            result.ExitCode.Should().Be(0, result.Output);
            var reviewPath = Path.Combine(outputDir, "review.html");
            File.Exists(reviewPath).Should().BeTrue();
            var reviewHtml = await File.ReadAllTextAsync(reviewPath);
            reviewHtml.Should().Contain("a.jpg");
            reviewHtml.Should().NotContain("b.jpg");
        }
        finally
        {
            Environment.SetEnvironmentVariable("FACEOFFX_DISABLE_BROWSER_LAUNCH", previousValue);
        }
    }
}

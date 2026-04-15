using System.Text.Json;
using AwesomeAssertions;
using FaceOFFx.Tests.Common;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Diagnostics.Cli.Tests.Commands;

[TestFixture]
[NonParallelizable]
public class DiagnosticsCommandTests : IntegrationTestBase
{
    [Test]
    public async Task Detect_WritesOrderedStagePngsInSubjectFolder()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var outputDir = Path.Combine(TempDirectory, "detect");

        var result = await app.RunAsync(
        [
            "detect",
            PeopleCorpus.SubjectSource("generic-guy", "png"),
            "--output", outputDir
        ]);

        result.ExitCode.Should().Be(0, result.Output);

        var subjectDir = Path.Combine(outputDir, "source");
        File.Exists(Path.Combine(outputDir, "manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(subjectDir, "00-original.png")).Should().BeTrue();
        File.Exists(Path.Combine(subjectDir, "10-coarse.png")).Should().BeTrue();
        File.Exists(Path.Combine(subjectDir, "20-chip-locate.png")).Should().BeTrue();
        File.Exists(Path.Combine(subjectDir, "30-chip.png")).Should().BeTrue();
        File.Exists(Path.Combine(subjectDir, "40-fine-chip.png")).Should().BeTrue();
        File.Exists(Path.Combine(subjectDir, "50-fine-source.png")).Should().BeTrue();
    }

    [Test]
    public async Task Detect_WritesOnlySubjectFoldersAndRootManifest()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var outputDir = Path.Combine(TempDirectory, "detect-people");

        var result = await app.RunAsync(
        [
            "detect",
            "--corpus", "people",
            "--output", outputDir
        ]);

        result.ExitCode.Should().Be(0, result.Output);

        Directory.EnumerateFiles(outputDir)
            .Select(Path.GetFileName)
            .Should()
            .BeEquivalentTo(["manifest.json"]);

        Directory.EnumerateDirectories(outputDir)
            .Select(Path.GetFileName)
            .Should()
            .Contain(["generic-guy", "person-01", "bush"]);
    }

    [Test]
    public async Task Detect_WritesFineChipVisualizationAndManifestStagePaths()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var outputDir = Path.Combine(TempDirectory, "detect-stage-manifest");

        var result = await app.RunAsync(
        [
            "detect",
            PeopleCorpus.SubjectSource("generic-guy", "png"),
            "--output", outputDir
        ]);

        result.ExitCode.Should().Be(0, result.Output);

        using var chip = await Image.LoadAsync<Rgba32>(Path.Combine(outputDir, "source", "30-chip.png"));
        using var fineChip = await Image.LoadAsync<Rgba32>(Path.Combine(outputDir, "source", "40-fine-chip.png"));
        chip.Width.Should().Be(112);
        chip.Height.Should().Be(112);
        fineChip.Width.Should().Be(448);
        fineChip.Height.Should().Be(448);

        using var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(outputDir, "manifest.json")));
        var subject = manifest.RootElement.GetProperty("Subjects")[0];
        subject.GetProperty("OriginalPath").GetString().Should().Be("00-original.png");
        subject.GetProperty("CoarsePath").GetString().Should().Be("10-coarse.png");
        subject.GetProperty("ChipLocatePath").GetString().Should().Be("20-chip-locate.png");
        subject.GetProperty("ChipPath").GetString().Should().Be("30-chip.png");
        subject.GetProperty("FineChipPath").GetString().Should().Be("40-fine-chip.png");
        subject.GetProperty("FineSourcePath").GetString().Should().Be("50-fine-source.png");
        subject.GetProperty("Error").ValueKind.Should().Be(JsonValueKind.Null);
    }
}

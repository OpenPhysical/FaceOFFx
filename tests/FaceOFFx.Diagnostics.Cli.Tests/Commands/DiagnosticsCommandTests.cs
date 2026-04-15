using AwesomeAssertions;
using FaceOFFx.Tests.Common;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;

namespace FaceOFFx.Diagnostics.Cli.Tests.Commands;

[TestFixture]
[NonParallelizable]
public class DiagnosticsCommandTests : IntegrationTestBase
{
    [Test]
    public async Task Detect_WithDefaultProfiles_WritesManifestAndOverlays()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var outputDir = Path.Combine(TempDirectory, "detect");

        var result = await app.RunAsync(new[]
        {
            "detect",
            PeopleCorpus.SubjectSource("generic-guy", "png"),
            "--output", outputDir
        });

        result.ExitCode.Should().Be(0, result.Output);
        File.Exists(Path.Combine(outputDir, "manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "source.raw.overlay.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "source.piv.overlay.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "source.canada-passport.overlay.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "source.us-passport.overlay.jpg")).Should().BeTrue();
    }

    [Test]
    public async Task Crop_WithPivProfile_WritesArtifacts()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var outputDir = Path.Combine(TempDirectory, "crop");

        var result = await app.RunAsync(new[]
        {
            "crop",
            PeopleCorpus.SubjectSource("generic-guy", "png"),
            "--profile", "piv",
            "--variant", "digital",
            "--output", outputDir
        });

        result.ExitCode.Should().Be(0, result.Output);
        File.Exists(Path.Combine(outputDir, "source", "source.piv.jp2")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "manifest.json")).Should().BeTrue();
    }

    [Test]
    public async Task Detect_WithExplicitProfiles_WritesSelectedProjectedOverlays()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var outputDir = Path.Combine(TempDirectory, "detect-profiles");

        var result = await app.RunAsync(new[]
        {
            "detect",
            PeopleCorpus.SubjectSource("generic-guy", "png"),
            "--profiles", "piv,canada-passport",
            "--output", outputDir
        });

        result.ExitCode.Should().Be(0, result.Output);
        File.Exists(Path.Combine(outputDir, "source.raw.overlay.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "source.piv.overlay.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "source.canada-passport.overlay.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "source.us-passport.overlay.jpg")).Should().BeFalse();
    }

    [Test]
    public async Task Detect_WithGrossOnly_WritesOnlyGrossOverlay()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var outputDir = Path.Combine(TempDirectory, "detect-gross");

        var result = await app.RunAsync(new[]
        {
            "detect",
            PeopleCorpus.SubjectSource("generic-guy", "png"),
            "--gross-only",
            "--output", outputDir
        });

        result.ExitCode.Should().Be(0, result.Output);
        File.Exists(Path.Combine(outputDir, "manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "source.gross.overlay.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "source.raw.overlay.jpg")).Should().BeFalse();
        File.Exists(Path.Combine(outputDir, "source.piv.overlay.jpg")).Should().BeFalse();
        File.Exists(Path.Combine(outputDir, "source.canada-passport.overlay.jpg")).Should().BeFalse();
    }

    [Test]
    public async Task Detect_WithFineOnly_WritesOnlyFineOverlay()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var outputDir = Path.Combine(TempDirectory, "detect-fine");

        var result = await app.RunAsync(new[]
        {
            "detect",
            PeopleCorpus.SubjectSource("generic-guy", "png"),
            "--fine-only",
            "--output", outputDir
        });

        result.ExitCode.Should().Be(0, result.Output);
        File.Exists(Path.Combine(outputDir, "manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "source.fine.overlay.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "source.raw.overlay.jpg")).Should().BeFalse();
        File.Exists(Path.Combine(outputDir, "source.gross.overlay.jpg")).Should().BeFalse();
        File.Exists(Path.Combine(outputDir, "source.piv.overlay.jpg")).Should().BeFalse();
        File.Exists(Path.Combine(outputDir, "source.canada-passport.overlay.jpg")).Should().BeFalse();

        using var fineOverlay = await Image.LoadAsync<Rgba32>(Path.Combine(outputDir, "source.fine.overlay.jpg"));
        fineOverlay.Width.Should().Be(1024);
        fineOverlay.Height.Should().Be(1536);

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir, "manifest.json")));
        var subject = manifest.RootElement.GetProperty("Subjects")[0];
        subject.GetProperty("RawOverlayPath").ValueKind.Should().Be(JsonValueKind.Null);
        subject.GetProperty("FineOverlayPath").GetString().Should().Be("source.fine.overlay.jpg");
        subject.GetProperty("Profiles").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Test]
    public async Task Detect_WithSaveChips_WritesExactChipAndReviewPngs()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var outputDir = Path.Combine(TempDirectory, "detect-chips");

        var result = await app.RunAsync(new[]
        {
            "detect",
            PeopleCorpus.SubjectSource("generic-guy", "png"),
            "--gross-only",
            "--save-chips",
            "--output", outputDir
        });

        result.ExitCode.Should().Be(0, result.Output);

        var chipPath = Path.Combine(outputDir, "chips", "source.png");
        var reviewPath = Path.Combine(outputDir, "chips", "source.review.png");
        File.Exists(chipPath).Should().BeTrue();
        File.Exists(reviewPath).Should().BeTrue();

        using var chip = await Image.LoadAsync<Rgba32>(chipPath);
        using var review = await Image.LoadAsync<Rgba32>(reviewPath);
        chip.Width.Should().Be(112);
        chip.Height.Should().Be(112);
        review.Width.Should().Be(448);
        review.Height.Should().Be(448);

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDir, "manifest.json")));
        var subject = manifest.RootElement.GetProperty("Subjects")[0];
        subject.GetProperty("ChipPath").GetString().Should().Be(Path.Combine("chips", "source.png"));
        subject.GetProperty("ChipReviewPath").GetString().Should().Be(Path.Combine("chips", "source.review.png"));
    }

    [Test]
    public async Task Detect_WithGrossOnlyAndFineOnly_ReturnsArgumentError()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var outputDir = Path.Combine(TempDirectory, "detect-invalid");

        var result = await app.RunAsync(new[]
        {
            "detect",
            PeopleCorpus.SubjectSource("generic-guy", "png"),
            "--gross-only",
            "--fine-only",
            "--output", outputDir
        });

        result.ExitCode.Should().NotBe(0);
        result.Output.Should().Contain("--gross-only and --fine-only cannot be used together.");
    }
}

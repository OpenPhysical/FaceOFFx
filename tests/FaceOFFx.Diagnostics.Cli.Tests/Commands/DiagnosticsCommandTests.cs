using AwesomeAssertions;
using FaceOFFx.Tests.Common;
using NUnit.Framework;

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
}

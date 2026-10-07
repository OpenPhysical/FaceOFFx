using System.Security.Cryptography;
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
        var output = Path.Combine(TempDirectory, "detect");
        var result = await app.RunAsync(["detect", PeopleCorpus.SubjectSource("generic-guy", "png"), "--output", output]);
        result.ExitCode.Should().Be(0, result.Output);
        foreach (var name in new[] { "00-original.png", "10-coarse.png", "20-chip-locate.png", "30-chip.png", "40-fine-chip.png", "50-fine-source.png" })
            File.Exists(Path.Combine(output, "source", name)).Should().BeTrue();
        File.Exists(Path.Combine(output, "manifest.json")).Should().BeTrue();
    }

    [Test]
    public async Task Detect_WritesOnlySubjectFoldersAndRootManifest()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var output = Path.Combine(TempDirectory, "detect-people");
        var result = await app.RunAsync(["detect", "--corpus", "people", "--output", output]);
        result.ExitCode.Should().Be(0, result.Output);
        Directory.EnumerateFiles(output).Select(Path.GetFileName).Should().BeEquivalentTo(["manifest.json"]);
        Directory.EnumerateDirectories(output).Select(Path.GetFileName).Should().Contain(["generic-guy", "person-01", "bush"]);
    }

    [Test]
    public async Task Detect_WritesFineChipVisualizationAndManifestStagePaths()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var output = Path.Combine(TempDirectory, "detect-stages");
        var result = await app.RunAsync(["detect", PeopleCorpus.SubjectSource("generic-guy", "png"), "--output", output]);
        result.ExitCode.Should().Be(0, result.Output);
        using var chip = await Image.LoadAsync<Rgba32>(Path.Combine(output, "source", "30-chip.png"));
        using var fine = await Image.LoadAsync<Rgba32>(Path.Combine(output, "source", "40-fine-chip.png"));
        chip.Width.Should().Be(112);
        chip.Height.Should().Be(112);
        fine.Width.Should().Be(448);
        fine.Height.Should().Be(448);
        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(output, "manifest.json")));
        var subject = manifest.RootElement.GetProperty("Subjects")[0];
        subject.GetProperty("OriginalPath").GetString().Should().Be("00-original.png");
        subject.GetProperty("CoarsePath").GetString().Should().Be("10-coarse.png");
        subject.GetProperty("ChipLocatePath").GetString().Should().Be("20-chip-locate.png");
        subject.GetProperty("ChipPath").GetString().Should().Be("30-chip.png");
        subject.GetProperty("FineChipPath").GetString().Should().Be("40-fine-chip.png");
        subject.GetProperty("FineSourcePath").GetString().Should().Be("50-fine-source.png");
        subject.GetProperty("Error").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [TestCase(false, "minimum", 11820)]
    [TestCase(true, "preferred", 22000)]
    public async Task DocsSamples_ExportsFixedBalancedJp2AndPendingIndependentPreview(bool directoryInput, string target, int cap)
    {
        var app = DiagnosticsCliTestHarness.Create();
        var output = Path.Combine(TempDirectory, "samples");
        var source = PeopleCorpus.WatermarkedCardholderSource();
        var input = source;
        var explicitInput = source;
        if (directoryInput)
        {
            explicitInput = Path.Combine(TempDirectory, "sample-input");
            Directory.CreateDirectory(explicitInput);
            input = Path.Combine(explicitInput, "german-male.png");
            File.Copy(source, input);
        }
        var sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(input));
        var result = await app.RunAsync(["docs", "samples", "--input", explicitInput, "--size-profile", target, "--output", output]);
        result.ExitCode.Should().Be(0, result.Output);
        using var thumbnail = await Image.LoadAsync<Rgba32>(Path.Combine(output, "original", "german_male_420w.jpg"));
        thumbnail.Width.Should().Be(420);
        var encodedPath = Path.Combine(output, "processed", "german_male_piv.jp2");
        var encoded = await File.ReadAllBytesAsync(encodedPath);
        encoded.Length.Should().BeInRange(1, cap);
        encoded.Take(12).Should().Equal(new byte[] { 0, 0, 0, 12, 0x6a, 0x50, 0x20, 0x20, 13, 10, 0x87, 10 });
        Directory.EnumerateFiles(Path.Combine(output, "processed")).Should().Equal(encodedPath);
        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(output, "manifest.json")));
        manifest.RootElement.GetProperty("Corpus").GetString().Should().Be("explicit-input");
        manifest.RootElement.GetProperty("SizeProfile").GetString().Should().Be(target);
        Path.GetFullPath(manifest.RootElement.GetProperty("Input").GetString()!).Should().Be(explicitInput);
        var assets = manifest.RootElement.GetProperty("Assets").EnumerateArray().ToArray();
        assets.Should().HaveCount(1);
        var asset = assets[0];
        asset.GetProperty("Width").GetInt32().Should().Be(480);
        asset.GetProperty("Height").GetInt32().Should().Be(640);
        asset.GetProperty("Bytes").GetInt32().Should().Be(encoded.Length);
        asset.GetProperty("EncodedSha256").GetString().Should().Be(Convert.ToHexString(SHA256.HashData(encoded)).ToLowerInvariant());
        asset.GetProperty("PreviewStatus").GetString().Should().Be("PendingIndependentDecode");
        File.Exists(Path.GetFullPath(asset.GetProperty("OutputPath").GetString()!)).Should().BeFalse();
        asset.GetProperty("SourceColorEvidence").GetProperty("Status").GetInt32().Should()
            .Be((int)FaceOFFx.Core.Domain.Transformations.PivSourceColorStatus.AssumedSrgb);
        var regional = asset.GetProperty("EncodingEvidence").GetProperty("RegionalCompressionVerification");
        regional.GetProperty("Attribution").GetProperty("MethodId").GetString()
            .Should().Be("synthesis-energy-decoder-effective-pass-v3");
        SHA256.HashData(await File.ReadAllBytesAsync(input)).Should().Equal(sourceHash);
        SHA256.HashData(await File.ReadAllBytesAsync(source)).Should().Equal(sourceHash);
    }

    [Test]
    public async Task DocsSamples_SourceWithInsufficientMargins_ReturnsGeometryFailure()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var output = Path.Combine(TempDirectory, "samples-margin");
        var result = await app.RunAsync(["docs", "samples", "--input", PeopleCorpus.SubjectSource("generic-guy", "png"), "--output", output]);
        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Crop margins");
        File.Exists(Path.Combine(output, "processed", "source_piv.jp2")).Should().BeFalse();
        File.Exists(Path.Combine(output, "manifest.json")).Should().BeFalse();
    }

    [Test]
    public async Task DocsSamples_InputAndCorpusTogether_ReturnsSourceSelectionFailure()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var output = Path.Combine(TempDirectory, "samples-ambiguous");
        var result = await app.RunAsync(["docs", "samples", "--input", PeopleCorpus.SubjectSource("generic-guy", "png"), "--corpus", "people", "--output", output]);
        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Choose either --input or --corpus");
        Directory.Exists(output).Should().BeFalse();
    }

    [Test]
    public async Task DocsSamples_ExistingAssets_PreservesDestination()
    {
        var app = DiagnosticsCliTestHarness.Create();
        var output = Path.Combine(TempDirectory, "samples-existing");
        Directory.CreateDirectory(output);
        var existing = Path.Combine(output, "sentinel.png");
        var bytes = new byte[] { 19, 61, 37, 42 };
        await File.WriteAllBytesAsync(existing, bytes);
        var result = await app.RunAsync(["docs", "samples", "--input", PeopleCorpus.SubjectSource("generic-guy", "png"), "--output", output]);
        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("new or empty output directory");
        (await File.ReadAllBytesAsync(existing)).Should().Equal(bytes);
        Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Should().Equal(existing);
    }
}

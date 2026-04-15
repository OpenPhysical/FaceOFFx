using System.ComponentModel;
using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Diagnostics.Cli.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Diagnostics.Cli.Commands;

internal sealed class DetectCommand(
    DiagnosticsCorpusService corpusService,
    DiagnosticsBatchService diagnostics,
    IAnsiConsole console) : AsyncCommand<DetectCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[INPUT]")]
        [Description("Input file or directory")]
        public string? InputPath { get; init; }

        [CommandOption("--corpus <ID>")]
        [Description("Named corpus id, such as 'people'")]
        public string? CorpusId { get; init; }

        [CommandOption("-o|--output <DIR>")]
        [Description("Output directory for manifest and overlays")]
        public string OutputDirectory { get; init; } = Path.Combine("artifacts", "diagnostics", "detect");

        [CommandOption("--save-chips")]
        [Description("Save the primary detected face crop for each image")]
        public bool SaveChips { get; init; }

        [CommandOption("--verify")]
        [Description("Fail if the corpus expectations do not match actual detection/landmarks results")]
        public bool Verify { get; init; }

        [CommandOption("--profiles <LIST>")]
        [Description("Comma-separated document ids to project, or 'all' for every shipped document")]
        public string Profiles { get; init; } = "all";

        [CommandOption("--gross-only")]
        [Description("Write only coarse detection overlays: detector box, RetinaFace 5-point landmarks, and the chip polygon")]
        public bool GrossOnly { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var subjects = corpusService.ResolveInputs(settings.InputPath, settings.CorpusId);
        var profiles = settings.GrossOnly ? Array.Empty<string>() : ResolveProfiles(settings.Profiles);
        Directory.CreateDirectory(settings.OutputDirectory);
        var chipDir = Path.Combine(settings.OutputDirectory, "chips");
        if (settings.SaveChips)
        {
            Directory.CreateDirectory(chipDir);
        }

        var failures = new List<string>();
        var records = new List<object>();

        await console.Progress().StartAsync(async ctx =>
        {
            var task = ctx.AddTask("[green]Running detection[/]", maxValue: subjects.Count);

            foreach (var subject in subjects)
            {
                var result = await diagnostics.DetectAsync(subject.InputPath, cancellationToken);
                if (result.IsFailure)
                {
                    failures.Add($"{subject.Id}: {result.Error}");
                    records.Add(new { subject.Id, subject.InputPath, Error = result.Error });
                    task.Increment(1);
                    continue;
                }

                if (settings.Verify)
                {
                    if (subject.ExpectedFaceCount.HasValue && result.Value.Faces.Count != subject.ExpectedFaceCount.Value)
                    {
                        failures.Add($"{subject.Id}: expected {subject.ExpectedFaceCount.Value} faces, got {result.Value.Faces.Count}");
                    }

                    if (!settings.GrossOnly && subject.ExpectLandmarks.HasValue)
                    {
                        var landmarksMatched = (result.Value.RawLandmarks.Count == 68) == subject.ExpectLandmarks.Value;
                        if (!landmarksMatched)
                        {
                            failures.Add($"{subject.Id}: expected landmarks={subject.ExpectLandmarks.Value}, got {result.Value.RawLandmarks.Count == 68}");
                        }
                    }
                }

                var rawOverlayPath = settings.GrossOnly
                    ? $"{subject.Id}.gross.overlay.jpg"
                    : $"{subject.Id}.raw.overlay.jpg";
                using (var image = await Image.LoadAsync<SixLabors.ImageSharp.PixelFormats.Rgba32>(subject.InputPath, cancellationToken))
                using (var rendered = settings.GrossOnly
                           ? OverlayRenderer.RenderGross(
                               image,
                               result.Value,
                               showBoundingBox: true)
                           : OverlayRenderer.RenderFull(
                               image,
                               result.Value,
                               null,
                               showBoundingBox: true,
                               showGuides: false))
                {
                    await rendered.SaveAsJpegAsync(
                        Path.Combine(settings.OutputDirectory, rawOverlayPath),
                        cancellationToken);
                }

                var overlayResults = new List<object>();
                foreach (var profileId in profiles)
                {
                    var overlay = await diagnostics.AnalyzeOverlayAsync(
                        subject.InputPath,
                        result.Value,
                        profileId,
                        cancellationToken);
                    var overlayPath = $"{subject.Id}.{profileId}.overlay.jpg";

                    if (overlay.IsFailure)
                    {
                        failures.Add($"{subject.Id}/{profileId}: {overlay.Error}");
                        overlayResults.Add(new
                        {
                            ProfileId = profileId,
                            OverlayPath = (string?)null,
                            Error = overlay.Error
                        });
                        continue;
                    }

                    using var sourceImage = await Image.LoadAsync<SixLabors.ImageSharp.PixelFormats.Rgba32>(subject.InputPath, cancellationToken);
                    using var projected = OverlayRenderer.RenderFull(
                        sourceImage,
                        result.Value,
                        overlay.Value,
                        showBoundingBox: true,
                        showGuides: true);
                    await projected.SaveAsJpegAsync(
                        Path.Combine(settings.OutputDirectory, overlayPath),
                        cancellationToken);

                    overlayResults.Add(new
                    {
                        ProfileId = profileId,
                        OverlayPath = overlayPath,
                        Error = (string?)null
                    });
                }

                string? chipPath = null;
                if (settings.SaveChips && result.Value.CanonicalGeometry.HasValue)
                {
                    using var image = await Image.LoadAsync<SixLabors.ImageSharp.PixelFormats.Rgba32>(subject.InputPath, cancellationToken);
                    using var chip = CanonicalFaceGeometryPipeline.RenderChip(
                        image,
                        result.Value.CanonicalGeometry.GetValueOrThrow("Canonical geometry is required to save the normalized chip."));
                    chipPath = Path.Combine("chips", $"{subject.Id}.png");
                    await chip.SaveAsPngAsync(Path.Combine(settings.OutputDirectory, chipPath), cancellationToken);
                }

                records.Add(new
                {
                    subject.Id,
                    result.Value.InputPath,
                    result.Value.Width,
                    result.Value.Height,
                    FaceCount = result.Value.Faces.Count,
                    result.Value.Faces,
                    LandmarksExtracted = !settings.GrossOnly && result.Value.RawLandmarks.Count == 68,
                    RawOverlayPath = rawOverlayPath,
                    ChipPath = chipPath,
                    ChipPolygon = result.Value.ChipPolygon,
                    Profiles = settings.GrossOnly ? null : overlayResults
                });
                task.Increment(1);
            }
        });

        await DiagnosticsBatchService.WriteManifestAsync(
            Path.Combine(settings.OutputDirectory, "manifest.json"),
            new { Corpus = settings.CorpusId, GeneratedAt = DateTimeOffset.UtcNow, Subjects = records },
            cancellationToken);

        if (failures.Count > 0)
        {
            foreach (var failure in failures)
            {
                console.MarkupLine($"[red]{Markup.Escape(failure)}[/]");
            }
        }

        return failures.Count == 0 ? 0 : 1;
    }

    private static IReadOnlyList<string> ResolveProfiles(string value)
    {
        if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            return DocumentCatalog.GetAll()
                .Select(document => document.Id)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

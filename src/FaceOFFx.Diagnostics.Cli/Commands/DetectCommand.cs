using System.ComponentModel;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Diagnostics.Cli.Services;
using SixLabors.ImageSharp;
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
        [Description("Output directory for the staged visualization folders and manifest")]
        public string OutputDirectory { get; init; } = Path.Combine("artifacts", "diagnostics", "detect");

        [CommandOption("--verify")]
        [Description("Fail if the corpus expectations do not match actual detection or landmark results")]
        public bool Verify { get; init; }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var subjectsResult = corpusService.ResolveInputs(settings.InputPath, settings.CorpusId);
        if (subjectsResult.IsFailure)
        {
            console.MarkupLine($"[red]{Markup.Escape(subjectsResult.Error.Message)}[/]");
            return 1;
        }

        var subjects = subjectsResult.Value;
        Directory.CreateDirectory(settings.OutputDirectory);

        var failures = new List<string>();
        var records = new List<object>();

        await console.Progress().StartAsync(async ctx =>
        {
            var task = ctx.AddTask("[green]Running detection[/]", maxValue: subjects.Count);

            foreach (var subject in subjects)
            {
                var result = await diagnostics.DetectAsync(subject.InputPath, cancellationToken)
                    .ConfigureAwait(false);
                if (result.IsFailure)
                {
                    failures.Add($"{subject.Id}: {result.Error.Message}");
                    records.Add(new
                    {
                        Id = subject.Id,
                        InputPath = subject.InputPath,
                        Error = result.Error.Message
                    });
                    task.Increment(1);
                    continue;
                }

                if (settings.Verify)
                {
                    if (subject.ExpectedFaceCount.HasValue
                        && result.Value.Faces.Count != subject.ExpectedFaceCount.Value)
                    {
                        failures.Add(
                            $"{subject.Id}: expected {subject.ExpectedFaceCount.Value} faces, got {result.Value.Faces.Count}");
                    }

                    if (subject.ExpectLandmarks.HasValue)
                    {
                        var landmarksMatched =
                            (result.Value.RawLandmarks.Count == 68) == subject.ExpectLandmarks.Value;
                        if (!landmarksMatched)
                        {
                            failures.Add(
                                $"{subject.Id}: expected landmarks={subject.ExpectLandmarks.Value}, got {result.Value.RawLandmarks.Count == 68}");
                        }
                    }
                }

                var subjectDirectory = Path.Combine(settings.OutputDirectory, subject.Id);
                Directory.CreateDirectory(subjectDirectory);

                string? originalPath = null;
                string? coarsePath = null;
                string? chipLocatePath = null;
                string? chipPath = null;
                string? fineChipPath = null;
                string? fineSourcePath = null;
                string? error = null;

                using var sourceImage = await Image.LoadAsync<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                    subject.InputPath,
                    cancellationToken);

                originalPath = "00-original.png";
                using (var rendered = OverlayRenderer.RenderOriginal(sourceImage))
                {
                    await rendered.SaveAsPngAsync(Path.Combine(subjectDirectory, originalPath), cancellationToken);
                }

                coarsePath = "10-coarse.png";
                using (var rendered = OverlayRenderer.RenderCoarse(sourceImage, result.Value))
                {
                    await rendered.SaveAsPngAsync(Path.Combine(subjectDirectory, coarsePath), cancellationToken);
                }

                var geometryResult = result.Value.CanonicalGeometry.ToPipelineResult(
                    new GeometryError(result.Value.GeometryError ?? "Canonical geometry was not available."));
                if (geometryResult.IsFailure)
                {
                    error = geometryResult.Error.Message;
                    failures.Add($"{subject.Id}: {error}");
                }
                else
                {
                    chipLocatePath = "20-chip-locate.png";
                    using (var rendered = OverlayRenderer.RenderChipLocate(sourceImage, result.Value))
                    {
                        await rendered.SaveAsPngAsync(
                            Path.Combine(subjectDirectory, chipLocatePath),
                            cancellationToken);
                    }

                    chipPath = "30-chip.png";
                    using var chip = CanonicalFaceGeometryPipeline.RenderChip(sourceImage, geometryResult.Value);
                    await chip.SaveAsPngAsync(Path.Combine(subjectDirectory, chipPath), cancellationToken);

                    fineChipPath = "40-fine-chip.png";
                    using (var rendered = OverlayRenderer.RenderFineChip(chip, geometryResult.Value.ChipLandmarks))
                    {
                        await rendered.SaveAsPngAsync(
                            Path.Combine(subjectDirectory, fineChipPath),
                            cancellationToken);
                    }

                    fineSourcePath = "50-fine-source.png";
                    using (var rendered = OverlayRenderer.RenderFineSource(sourceImage, result.Value))
                    {
                        await rendered.SaveAsPngAsync(
                            Path.Combine(subjectDirectory, fineSourcePath),
                            cancellationToken);
                    }
                }

                records.Add(new
                {
                    Id = subject.Id,
                    result.Value.InputPath,
                    result.Value.Width,
                    result.Value.Height,
                    FaceCount = result.Value.Faces.Count,
                    result.Value.Faces,
                    LandmarksExtracted = result.Value.RawLandmarks.Count == 68,
                    OriginalPath = originalPath,
                    CoarsePath = coarsePath,
                    ChipLocatePath = chipLocatePath,
                    ChipPath = chipPath,
                    FineChipPath = fineChipPath,
                    FineSourcePath = fineSourcePath,
                    ChipPolygon = result.Value.ChipPolygon,
                    Error = error
                });

                task.Increment(1);
            }
        });

        await DiagnosticsBatchService.WriteManifestAsync(
            Path.Combine(settings.OutputDirectory, "manifest.json"),
            new { Corpus = settings.CorpusId, GeneratedAt = DateTimeOffset.UtcNow, Subjects = records },
            cancellationToken);

        foreach (var failure in failures)
        {
            console.MarkupLine($"[red]{Markup.Escape(failure)}[/]");
        }

        return failures.Count == 0 ? 0 : 1;
    }
}

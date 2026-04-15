using System.ComponentModel;
using FaceOFFx.Diagnostics.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Diagnostics.Cli.Commands;

internal sealed class CropCommand(
    DiagnosticsCorpusService corpusService,
    DiagnosticsBatchService diagnostics,
    IAnsiConsole console) : AsyncCommand<CropCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[INPUT]")]
        [Description("Input file or directory")]
        public string? InputPath { get; init; }

        [CommandOption("--corpus <ID>")]
        [Description("Named corpus id, such as 'people'")]
        public string? CorpusId { get; init; }

        [CommandOption("--profile <ID>")]
        [Description("Document/profile id, such as piv or canada-passport")]
        public required string ProfileId { get; init; }

        [CommandOption("--variant <ID>")]
        [Description("Workflow variant: print, digital, or standard for PIV")]
        public string? Variant { get; init; }

        [CommandOption("-o|--output <DIR>")]
        [Description("Output directory for rendered crops and manifest")]
        public string OutputDirectory { get; init; } = Path.Combine("artifacts", "diagnostics", "crop");
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var subjects = corpusService.ResolveInputs(settings.InputPath, settings.CorpusId);
        Directory.CreateDirectory(settings.OutputDirectory);
        var records = new List<object>();
        var failures = new List<string>();

        await console.Progress().StartAsync(async ctx =>
        {
            var task = ctx.AddTask("[green]Rendering crops[/]", maxValue: subjects.Count);
            foreach (var subject in subjects)
            {
                var subjectOutput = Path.Combine(settings.OutputDirectory, subject.Id);
                Directory.CreateDirectory(subjectOutput);

                var result = await diagnostics.CropAsync(subject.InputPath, settings.ProfileId, settings.Variant, subjectOutput, cancellationToken);
                if (result.IsFailure)
                {
                    failures.Add($"{subject.Id}: {result.Error}");
                    task.Increment(1);
                    continue;
                }

                records.Add(new
                {
                    SubjectId = subject.Id,
                    DocumentId = result.Value.Document.Id,
                    Variant = result.Value.Variant.Id,
                    result.Value.Deliverables,
                    result.Value.ProvenancePath
                });
                task.Increment(1);
            }
        });

        await DiagnosticsBatchService.WriteManifestAsync(
            Path.Combine(settings.OutputDirectory, "manifest.json"),
            new { settings.ProfileId, settings.Variant, GeneratedAt = DateTimeOffset.UtcNow, Subjects = records },
            cancellationToken);

        foreach (var failure in failures)
        {
            console.MarkupLine($"[red]{Markup.Escape(failure)}[/]");
        }

        return failures.Count == 0 ? 0 : 1;
    }
}

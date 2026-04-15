using System.ComponentModel;
using System.Text.Json;
using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Infrastructure.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

[Description("Run a document photo workflow and write outputs plus provenance.")]
internal abstract class DocumentIssuanceCommandBase(
    DocumentJobRunner jobRunner,
    IAnsiConsole console) : AsyncCommand<DocumentIssuanceCommandBase.Settings>
{
    protected abstract string DocumentId { get; }
    protected abstract string DocumentDisplayName { get; }

    protected readonly DocumentJobRunner JobRunner = jobRunner;
    protected readonly IAnsiConsole Console = console;

    internal class Settings : CommandSettings
    {
        [CommandArgument(0, "<INPUT>")]
        [Description("Input image file path")]
        public string InputPath { get; set; } = string.Empty;

        [CommandOption("-o|--output-dir <DIR>")]
        [Description("Directory for the rendered artifacts and provenance")]
        public string? OutputDirectory { get; set; }

        [CommandOption("-v|--variant <VARIANT>")]
        [Description("Named document variant, such as print or digital")]
        public string? Variant { get; set; }

        [CommandOption("--json")]
        [Description("Write a machine-readable job summary to stdout")]
        public bool Json { get; set; }

        [CommandOption("--explain")]
        [Description("Show the cited clauses used by this document workflow")]
        public bool Explain { get; set; }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        var request = new DocumentJobRequest(
            settings.InputPath,
            DocumentId,
            VariantId: settings.Variant,
            OutputDirectory: settings.OutputDirectory,
            Json: settings.Json,
            Explain: settings.Explain);

        StringWriter? swallowedStdout = null;
        TextWriter? originalStdout = null;

        try
        {
            if (settings.Json)
            {
                originalStdout = System.Console.Out;
                swallowedStdout = new StringWriter();
                System.Console.SetOut(swallowedStdout);
            }

            var result = await JobRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                if (settings.Json)
                {
                    WriteJson(originalStdout, new { Error = result.Error.Message });
                }
                else
                {
                    Console.MarkupLine($"[red]Error:[/] {result.Error.Message}");
                }

                return 1;
            }

            if (settings.Json)
            {
                WriteJson(originalStdout, new
                {
                    Document = result.Value.Document.Id,
                    Variant = result.Value.Variant.Id,
                    result.Value.InputPath,
                    result.Value.InputPassed,
                    result.Value.InputSummary,
                    result.Value.ProvenancePath,
                    Deliverables = result.Value.Deliverables.Select(deliverable => new
                    {
                        deliverable.Id,
                        deliverable.OutputPath,
                        deliverable.Passed,
                        deliverable.Summary,
                        deliverable.FileSizeBytes,
                        deliverable.SupportingInfoPath,
                        deliverable.OriginalFileRequirementSatisfied,
                        deliverable.ProductionDefaults
                    }),
                    ManualChecklist = result.Value.ManualChecklist
                });
                return result.Value.InputPassed && result.Value.Deliverables.All(d => d.Passed) ? 0 : 1;
            }

            Console.MarkupLine($"[green]Document:[/] {DocumentDisplayName}");
            Console.MarkupLine($"[green]Input:[/] {result.Value.InputPath}");
            Console.MarkupLine($"[green]Input analysis:[/] {result.Value.InputSummary}");

            foreach (var deliverable in result.Value.Deliverables)
            {
                var status = deliverable.Passed ? "[green]PASS[/]" : "[red]FAIL[/]";
                Console.MarkupLine($"{status} {deliverable.DisplayName}: {deliverable.OutputPath}");
                Console.MarkupLine($"[grey]{deliverable.Summary}[/]");

                foreach (var check in deliverable.Checks.Where(check => check.Disposition == DocumentCheckDisposition.Advisory && !check.Passed))
                {
                    Console.MarkupLine($"[yellow]ADVISORY[/] {check.Name}: {check.Summary}");
                }

                if (!string.IsNullOrWhiteSpace(deliverable.SupportingInfoPath))
                {
                    Console.MarkupLine($"[blue]Supporting Info:[/] {deliverable.SupportingInfoPath}");
                }
            }

            if (result.Value.ManualChecklist.Count > 0)
            {
                Console.WriteLine();
                Console.MarkupLine("[bold]Manual Checklist[/]");
                foreach (var item in result.Value.ManualChecklist)
                {
                    Console.MarkupLine($"- [yellow]{item.Name}:[/] {item.Summary}");
                }
            }

            Console.MarkupLine($"[blue]Provenance:[/] {result.Value.ProvenancePath}");

            if (settings.Explain)
            {
                Console.WriteLine();
                Console.MarkupLine("[bold]Cited Clauses[/]");
                foreach (var citation in result.Value.Document.Citations)
                {
                    Console.MarkupLine($"- [yellow]{citation.DocumentTitle}[/], {citation.Clause}");
                    Console.MarkupLine($"  {citation.Summary}");
                }
            }

            return result.Value.InputPassed && result.Value.Deliverables.All(d => d.Passed) ? 0 : 1;
        }
        finally
        {
            if (originalStdout != null)
            {
                System.Console.SetOut(originalStdout);
            }

            swallowedStdout?.Dispose();
        }
    }

    private static void WriteJson(TextWriter? writer, object payload)
    {
        var target = writer ?? System.Console.Out;
        target.WriteLine(JsonSerializer.Serialize(payload));
        target.Flush();
    }
}

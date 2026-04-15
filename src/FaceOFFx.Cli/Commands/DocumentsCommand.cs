using FaceOFFx.Core.Domain.Documents;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

internal sealed class DocumentsCommand(IAnsiConsole console) : Command
{
    private readonly IAnsiConsole _console = console;

    public override int Execute(CommandContext context, CancellationToken cancellationToken)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Supported Documents[/]");
        table.AddColumn("Document");
        table.AddColumn("Default Variant");
        table.AddColumn("Variants");
        table.AddColumn("Notes");
        table.AddColumn("Normative Sources");

        foreach (var document in DocumentCatalog.GetAll().OrderBy(doc => doc.Id, StringComparer.OrdinalIgnoreCase))
        {
            var sources = string.Join(
                Environment.NewLine,
                document.Citations.Select(citation => $"{citation.DocumentTitle} {citation.Clause}"));
            var variants = string.Join(
                ", ",
                document.Variants.Keys.OrderBy(variant => variant, StringComparer.OrdinalIgnoreCase));
            var hasAdvisory = document.Variants.Values
                .SelectMany(variant => variant.Deliverables)
                .SelectMany(deliverable => deliverable.OutputChecks)
                .Any(check => check.Disposition == DocumentCheckDisposition.Advisory);
            var notes = hasAdvisory || document.ManualChecklist.Count > 0
                ? string.Join(", ", new[]
                    {
                        hasAdvisory ? "advisory checks" : null,
                        document.ManualChecklist.Count > 0 ? "manual checklist" : null
                    }.Where(note => note is not null))
                : "-";

            table.AddRow(document.Id, document.PrimaryVariantId, string.IsNullOrEmpty(variants) ? "-" : variants, notes, sources);
        }

        _console.Write(table);
        return 0;
    }
}

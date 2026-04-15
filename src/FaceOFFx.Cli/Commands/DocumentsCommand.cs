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
        table.AddColumn("Other Variants");
        table.AddColumn("Normative Sources");

        foreach (var document in DocumentCatalog.GetAll().OrderBy(doc => doc.Id, StringComparer.OrdinalIgnoreCase))
        {
            var sources = string.Join(
                Environment.NewLine,
                document.Citations.Select(citation => $"{citation.DocumentTitle} {citation.Clause}"));
            var variants = string.Join(
                ", ",
                document.Variants.Keys
                    .Where(variant => !string.Equals(variant, document.PrimaryVariantId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(variant => variant, StringComparer.OrdinalIgnoreCase));

            table.AddRow(document.Id, document.PrimaryVariantId, string.IsNullOrEmpty(variants) ? "-" : variants, sources);
        }

        _console.Write(table);
        return 0;
    }
}

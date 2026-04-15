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
        table.AddColumn("Normative Sources");

        foreach (var document in DocumentCatalog.GetAll().OrderBy(doc => doc.Id, StringComparer.OrdinalIgnoreCase))
        {
            var sources = string.Join(
                Environment.NewLine,
                document.Citations.Select(citation => $"{citation.DocumentTitle} {citation.Clause}"));

            table.AddRow(document.Id, document.PrimaryVariantId, sources);
        }

        _console.Write(table);
        return 0;
    }
}

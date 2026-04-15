using System.ComponentModel;
using FaceOFFx.Infrastructure.Services;
using Spectre.Console;

namespace FaceOFFx.Cli.Commands;

[Description("Run the Canada citizenship grant photo workflow.")]
internal sealed class CanadaCitizenshipGrantCommand(
    DocumentJobRunner jobRunner,
    IAnsiConsole console) : DocumentIssuanceCommandBase(jobRunner, console)
{
    protected override string DocumentId => "canada-citizenship-grant";
    protected override string DocumentDisplayName => "Canada Citizenship Grant";
}

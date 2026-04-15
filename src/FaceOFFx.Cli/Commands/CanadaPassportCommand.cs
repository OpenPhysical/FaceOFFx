using System.ComponentModel;
using FaceOFFx.Infrastructure.Services;
using Spectre.Console;

namespace FaceOFFx.Cli.Commands;

[Description("Run the Canada passport photo workflow.")]
internal sealed class CanadaPassportCommand(
    DocumentJobRunner jobRunner,
    IAnsiConsole console) : DocumentIssuanceCommandBase(jobRunner, console)
{
    protected override string DocumentId => "canada-passport";
    protected override string DocumentDisplayName => "Canada Passport";
}

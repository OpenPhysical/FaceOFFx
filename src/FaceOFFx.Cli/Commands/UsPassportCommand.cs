using System.ComponentModel;
using FaceOFFx.Infrastructure.Services;
using Spectre.Console;

namespace FaceOFFx.Cli.Commands;

[Description("Run the U.S. passport photo workflow.")]
internal sealed class UsPassportCommand(
    DocumentJobRunner jobRunner,
    IAnsiConsole console) : DocumentIssuanceCommandBase(jobRunner, console)
{
    protected override string DocumentId => "us-passport";
    protected override string DocumentDisplayName => "U.S. Passport";
}

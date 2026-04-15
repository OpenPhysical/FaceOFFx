using System.ComponentModel;
using FaceOFFx.Infrastructure.Services;
using Spectre.Console;

namespace FaceOFFx.Cli.Commands;

[Description("Run the Canada permanent resident card photo workflow.")]
internal sealed class CanadaPermanentResidentCardCommand(
    DocumentJobRunner jobRunner,
    IAnsiConsole console) : DocumentIssuanceCommandBase(jobRunner, console)
{
    protected override string DocumentId => "canada-permanent-resident";
    protected override string DocumentDisplayName => "Canada Permanent Resident";
}

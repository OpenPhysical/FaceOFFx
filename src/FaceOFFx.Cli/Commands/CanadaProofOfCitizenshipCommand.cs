using System.ComponentModel;
using FaceOFFx.Infrastructure.Services;
using Spectre.Console;

namespace FaceOFFx.Cli.Commands;

[Description("Run the Canada proof of citizenship photo workflow.")]
internal sealed class CanadaProofOfCitizenshipCommand(
    DocumentJobRunner jobRunner,
    IAnsiConsole console) : DocumentIssuanceCommandBase<DocumentIssuanceCommandSettings>(jobRunner, console)
{
    protected override string DocumentId => "canada-proof-of-citizenship";
    protected override string DocumentDisplayName => "Canada Proof of Citizenship";
}

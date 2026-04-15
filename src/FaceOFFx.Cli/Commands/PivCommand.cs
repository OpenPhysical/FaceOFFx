using System.ComponentModel;
using FaceOFFx.Infrastructure.Services;
using Spectre.Console;

namespace FaceOFFx.Cli.Commands;

[Description("Run the spec-backed PIV issuance recipe and write outputs plus provenance.")]
internal sealed class PivCommand(
    DocumentJobRunner jobRunner,
    IAnsiConsole console) : DocumentIssuanceCommandBase(jobRunner, console)
{
    protected override string DocumentId => "piv";
    protected override string DocumentDisplayName => "PIV";
}

using System.ComponentModel;
using FaceOFFx.Infrastructure.Services;
using Spectre.Console;

namespace FaceOFFx.Cli.Commands;

[Description("Run the U.S. permanent resident application photo workflow.")]
internal sealed class UsPermanentResidentPhotoCommand(
    DocumentJobRunner jobRunner,
    IAnsiConsole console) : DocumentIssuanceCommandBase<DocumentIssuanceCommandSettings>(jobRunner, console)
{
    protected override string DocumentId => "us-permanent-resident";
    protected override string DocumentDisplayName => "U.S. Permanent Resident";
}

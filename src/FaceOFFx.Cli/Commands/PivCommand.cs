using System.ComponentModel;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Infrastructure.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

[Description("Run the spec-backed PIV issuance recipe and write outputs plus provenance.")]
internal sealed class PivCommand(
    DocumentJobRunner jobRunner,
    IAnsiConsole console) : DocumentIssuanceCommandBase<PivCommand.Settings>(jobRunner, console)
{
    internal sealed class Settings : DocumentIssuanceCommandSettings
    {
        [CommandOption("--filesize-target <TARGET>")]
        [Description("Named PIV card image file-size target: preferred or minimum")]
        public string? FileSizeTarget { get; set; }
    }

    protected override string DocumentId => "piv";
    protected override string DocumentDisplayName => "PIV";
    protected override bool SupportsFileSizeTargetOption => true;

    protected override DocumentJobRequest CreateRequest(Settings settings) =>
        new(
            settings.InputPath,
            DocumentId,
            VariantId: settings.Variant,
            OutputDirectory: settings.OutputDirectory,
            Json: settings.Json,
            Explain: settings.Explain,
            FileSizeTargetId: settings.FileSizeTarget is null
                ? Maybe<string>.None
                : Maybe<string>.From(settings.FileSizeTarget));
}

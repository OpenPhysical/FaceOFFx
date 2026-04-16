using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Documents;
using FaceOFFx.Infrastructure.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

[Description("Run a document photo workflow and write outputs plus provenance.")]
internal abstract class DocumentIssuanceCommandBase<TSettings>(
    DocumentJobRunner jobRunner,
    IAnsiConsole console) : AsyncCommand<TSettings>
    where TSettings : DocumentIssuanceCommandSettings
{
    protected abstract string DocumentId { get; }
    protected abstract string DocumentDisplayName { get; }
    protected virtual bool SupportsFileSizeTargetOption => false;

    protected readonly DocumentJobRunner JobRunner = jobRunner;
    protected readonly IAnsiConsole Console = console;

    public override ValidationResult Validate(CommandContext context, TSettings settings)
    {
        if (!SupportsFileSizeTargetOption
            && context.Arguments.Any(argument =>
                argument.Equals("--filesize-target", StringComparison.OrdinalIgnoreCase)
                || argument.StartsWith("--filesize-target=", StringComparison.OrdinalIgnoreCase)))
        {
            return ValidationResult.Error("--filesize-target is only supported by the piv command.");
        }

        return ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        TSettings settings,
        CancellationToken cancellationToken)
    {
        var request = CreateRequest(settings);

        if (settings.Json)
        {
            System.Console.SetOut(TextWriter.Null);
        }

        using var stdoutRedirect = settings.Json ? StandardOutputRedirect.ToNull() : null;
        var result = await JobRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        stdoutRedirect?.Dispose();

        if (result.IsFailure)
        {
            if (settings.Json)
            {
                WriteJson(new { Error = result.Error.Message });
            }
            else
            {
                Console.MarkupLine($"[red]Error:[/] {result.Error.Message}");
            }

            return 1;
        }

        if (settings.Json)
        {
            WriteJson(new
            {
                Document = result.Value.Document.Id,
                Variant = result.Value.Variant.Id,
                result.Value.InputPath,
                result.Value.InputPassed,
                result.Value.InputSummary,
                result.Value.ProvenancePath,
                Deliverables = result.Value.Deliverables.Select(deliverable => new
                {
                    deliverable.Id,
                    deliverable.OutputPath,
                    deliverable.Passed,
                    deliverable.Summary,
                    deliverable.FileSizeBytes,
                    deliverable.SupportingInfoPath,
                    deliverable.OriginalFileRequirementSatisfied,
                    deliverable.ProductionDefaults
                }),
                ManualChecklist = result.Value.ManualChecklist
            });
            return result.Value.InputPassed && result.Value.Deliverables.All(d => d.Passed) ? 0 : 1;
        }

        Console.MarkupLine($"[green]Document:[/] {DocumentDisplayName}");
        Console.MarkupLine($"[green]Input:[/] {result.Value.InputPath}");
        Console.MarkupLine($"[green]Input analysis:[/] {result.Value.InputSummary}");

        foreach (var deliverable in result.Value.Deliverables)
        {
            var status = deliverable.Passed ? "[green]PASS[/]" : "[red]FAIL[/]";
            Console.MarkupLine($"{status} {deliverable.DisplayName}: {deliverable.OutputPath}");
            Console.MarkupLine($"[grey]{deliverable.Summary}[/]");

            foreach (var check in deliverable.Checks.Where(check => check.Disposition == DocumentCheckDisposition.Advisory && !check.Passed))
            {
                Console.MarkupLine($"[yellow]ADVISORY[/] {check.Name}: {check.Summary}");
            }

            if (!string.IsNullOrWhiteSpace(deliverable.SupportingInfoPath))
            {
                Console.MarkupLine($"[blue]Supporting Info:[/] {deliverable.SupportingInfoPath}");
            }
        }

        if (result.Value.ManualChecklist.Count > 0)
        {
            Console.WriteLine();
            Console.MarkupLine("[bold]Manual Checklist[/]");
            foreach (var item in result.Value.ManualChecklist)
            {
                Console.MarkupLine($"- [yellow]{item.Name}:[/] {item.Summary}");
            }
        }

        Console.MarkupLine($"[blue]Provenance:[/] {result.Value.ProvenancePath}");

        if (settings.Explain)
        {
            Console.WriteLine();
            Console.MarkupLine("[bold]Cited Clauses[/]");
            foreach (var citation in result.Value.Document.Citations)
            {
                Console.MarkupLine($"- [yellow]{citation.DocumentTitle}[/], {citation.Clause}");
                Console.MarkupLine($"  {citation.Summary}");
            }
        }

        return result.Value.InputPassed && result.Value.Deliverables.All(d => d.Passed) ? 0 : 1;
    }

    private static void WriteJson(object payload)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        using var stdout = System.Console.OpenStandardOutput();
        stdout.Write(jsonBytes);
        stdout.WriteByte((byte)'\n');
        stdout.Flush();
    }

    protected virtual DocumentJobRequest CreateRequest(TSettings settings) =>
        new(
            settings.InputPath,
            DocumentId,
            VariantId: settings.Variant,
            OutputDirectory: settings.OutputDirectory,
            Json: settings.Json,
            Explain: settings.Explain,
            FileSizeTargetId: Maybe<string>.None);
}

internal sealed class StandardOutputRedirect : IDisposable
{
    private const int StdoutFileDescriptor = 1;
    private const int WriteOnly = 0x0001;

    private readonly int _savedStdout;
    private bool _disposed;

    private StandardOutputRedirect(int savedStdout)
    {
        _savedStdout = savedStdout;
    }

    public static StandardOutputRedirect? ToNull()
    {
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            return null;
        }

        var savedStdout = Dup(StdoutFileDescriptor);
        if (savedStdout == -1)
        {
            throw CreateRedirectError("duplicate stdout");
        }

        var nullDevice = Open("/dev/null", WriteOnly);
        if (nullDevice == -1)
        {
            var error = CreateRedirectError("open /dev/null");
            Close(savedStdout);
            throw error;
        }

        if (Dup2(nullDevice, StdoutFileDescriptor) == -1)
        {
            var error = CreateRedirectError("redirect stdout");
            Close(nullDevice);
            Close(savedStdout);
            throw error;
        }

        Close(nullDevice);
        return new StandardOutputRedirect(savedStdout);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Dup2(_savedStdout, StdoutFileDescriptor);
        Close(_savedStdout);
        _disposed = true;
    }

    private static InvalidOperationException CreateRedirectError(string operation) =>
        new($"Unable to {operation} for clean JSON output: errno {Marshal.GetLastPInvokeError()}.");

    [DllImport("libc", EntryPoint = "dup", SetLastError = true)]
    private static extern int Dup(int oldfd);

    [DllImport("libc", EntryPoint = "dup2", SetLastError = true)]
    private static extern int Dup2(int oldfd, int newfd);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(string pathname, int flags);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    private static extern int Close(int fd);
}

internal class DocumentIssuanceCommandSettings : CommandSettings
{
    [CommandArgument(0, "<INPUT>")]
    [Description("Input image file path")]
    public string InputPath { get; set; } = string.Empty;

    [CommandOption("-o|--output-dir <DIR>")]
    [Description("Directory for the rendered artifacts and provenance")]
    public string? OutputDirectory { get; set; }

    [CommandOption("-v|--variant <VARIANT>")]
    [Description("Named document variant, such as print or digital")]
    public string? Variant { get; set; }

    [CommandOption("--json")]
    [Description("Write a machine-readable job summary to stdout")]
    public bool Json { get; set; }

    [CommandOption("--explain")]
    [Description("Show the cited clauses used by this document workflow")]
    public bool Explain { get; set; }
}

using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

[Description("Prepare a PIV JPEG 2000 image and its verification evidence.")]
internal sealed class PivCommand(PivImageEncoder encoder, IAnsiConsole console) : AsyncCommand<PivCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<INPUT>")]
        [Description("Source photo path")]
        public string InputPath { get; set; } = string.Empty;
        [CommandOption("-o|--output <FILE>")]
        [Description("Output JP2 path, default: INPUT.piv.jp2")]
        public string? OutputPath { get; set; }
        [CommandOption("--filesize-target <TARGET>")]
        [Description("Complete JP2 ceiling: minimum (11820), preferred (22000), or a positive byte count")]
        [DefaultValue("minimum")]
        public string FileSizeTarget { get; set; } = "minimum";
        [CommandOption("--json")]
        [Description("Write the evidence summary as JSON")]
        public bool Json { get; set; }
        [CommandOption("--overwrite")]
        [Description("Replace existing output and evidence files")]
        public bool Overwrite { get; set; }
        [CommandOption("--debug")]
        [Description("Write diagnostic logging to stderr")]
        public bool Debug { get; set; }
    }

    public override ValidationResult Validate(CommandContext context, Settings settings)
    {
        if (!File.Exists(settings.InputPath)) return ValidationResult.Error("Supply an existing source photo.");
        if (Target(settings) is null)
            return ValidationResult.Error("Choose filesize target minimum, preferred, or a positive invariant integer byte count with room for up to 892 wrapper bytes.");
        var output = OutputPath(settings);
        if (new FileInfo(output).LinkTarget is not null || new FileInfo(output + ".json").LinkTarget is not null)
            return ValidationResult.Error("Choose regular output paths separate from symbolic links.");
        if (Directory.Exists(output) || Directory.Exists(output + ".json"))
            return ValidationResult.Error("Choose output file paths separate from existing directories.");
        if (!string.Equals(Path.GetExtension(output), ".jp2", StringComparison.OrdinalIgnoreCase))
            return ValidationResult.Error("Use the .jp2 output extension.");
        if (string.Equals(Path.GetFullPath(settings.InputPath), output, StringComparison.OrdinalIgnoreCase))
            return ValidationResult.Error("Choose an output path separate from the source.");
        if (!settings.Overwrite && (File.Exists(output) || File.Exists(output + ".json")))
            return ValidationResult.Error("Choose a new output path or authorize --overwrite.");
        return ValidationResult.Success();
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var target = Target(settings)!;
        var sourceBytes = await File.ReadAllBytesAsync(settings.InputPath, cancellationToken).ConfigureAwait(false);
        var oldStdout = System.Console.Out;
        Result<PivEncodingResult, PipelineError> result;
        try
        {
            if (settings.Json) System.Console.SetOut(TextWriter.Null);
            using var redirect = settings.Json ? StandardOutputRedirect.ToNull() : null;
            result = await encoder.EncodeAsync(sourceBytes, target, cancellationToken).ConfigureAwait(false);
        }
        finally { System.Console.SetOut(oldStdout); }
        if (result.IsFailure)
        {
            ReportError(result.Error.Message, settings.Json);
            return 1;
        }
        var encoded = result.Value;
        var output = OutputPath(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var requirements = encoded.VerificationRequirements;
        var evidence = new
        {
            Profile = "piv", InputPath = Path.GetFullPath(settings.InputPath), OutputPath = output,
            SourceSha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant(),
            ImageSha256 = Convert.ToHexString(SHA256.HashData(encoded.ImageData)).ToLowerInvariant(),
            encoded.OutputDimensions, FileSizeBytes = encoded.ImageData.Length,
            FileSizeTarget = target, encoded.GeometryEvidence, encoded.RoiCoverage, encoded.SourceColorEvidence,
            EncodingEvidence = encoded.Encoding,
            VerificationRequirements = requirements,
            AutomatedEncodingPassed = true
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(evidence, new JsonSerializerOptions { WriteIndented = true });
        await PivOutputWriter.WriteAsync(output, encoded.ImageData, bytes, settings.Overwrite, cancellationToken).ConfigureAwait(false);
        if (settings.Json) WriteJson(evidence);
        else
        {
            console.MarkupLine($"[green]Encoded:[/] {Markup.Escape(output)} ({encoded.ImageData.Length:N0} bytes)");
            console.MarkupLine($"[yellow]Regional measurement:[/] {Markup.Escape(encoded.Encoding.RegionalCompressionVerification?.Statement ?? "Regional review pending.")}");
            foreach (var warning in requirements)
                console.MarkupLine($"[yellow]Review:[/] {Markup.Escape(warning)}");
            console.MarkupLine($"[grey]Evidence:[/] {Markup.Escape(output + ".json")}");
        }
        return 0;
    }

    private void ReportError(string message, bool json)
    {
        if (json) WriteJson(new { Error = message });
        else console.MarkupLine($"[red]Error:[/] {Markup.Escape(message)}");
    }
    private static string OutputPath(Settings settings) => Path.GetFullPath(settings.OutputPath ??
        Path.Combine(Path.GetDirectoryName(settings.InputPath) ?? string.Empty, Path.GetFileNameWithoutExtension(settings.InputPath) + ".piv.jp2"));
    private static PivFileSizeTarget? Target(Settings settings)
    {
        if (string.Equals(settings.FileSizeTarget, "minimum", StringComparison.OrdinalIgnoreCase)) return PivFileSizeTarget.Minimum;
        if (string.Equals(settings.FileSizeTarget, "preferred", StringComparison.OrdinalIgnoreCase)) return PivFileSizeTarget.Preferred;
        if (!int.TryParse(settings.FileSizeTarget, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) ||
            bytes <= 0 || bytes > int.MaxValue - 892) return null;
        return PivFileSizeTarget.FromBytes(bytes);
    }
    private static void WriteJson(object value)
    {
        using var stdout = System.Console.OpenStandardOutput();
        stdout.Write(JsonSerializer.SerializeToUtf8Bytes(value));
        stdout.WriteByte((byte)'\n');
        stdout.Flush();
    }
}

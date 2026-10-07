using System.ComponentModel;
using System.Security.Cryptography;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Diagnostics.Cli.Services;
using FaceOFFx.Infrastructure.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Diagnostics.Cli.Commands;

/// <summary>Exports fixed-recipe JP2 samples and a manifest for independent development previews.</summary>
internal sealed class DocsSamplesCommand(
    DiagnosticsCorpusService corpusService,
    ProfileEncoder profileEncoder,
    IAnsiConsole console) : AsyncCommand<DocsSamplesCommand.Settings>
{
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--input <PATH>")]
        [Description("Source image or directory")]
        public string? InputPath { get; init; }

        [CommandOption("--corpus <ID>")]
        [Description("Named source corpus (default: people)")]
        public string? CorpusId { get; init; }

        [CommandOption("-o|--output <DIR>")]
        [Description("New sample output directory")]
        public string OutputDirectory { get; init; } = Path.Combine("docs", "samples");

        [CommandOption("--size-profile <ID>")]
        [Description("JP2 target: minimum or preferred")]
        public string SizeProfile { get; init; } = "minimum";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        var explicitInput = !string.IsNullOrWhiteSpace(settings.InputPath);
        if (explicitInput && !string.IsNullOrWhiteSpace(settings.CorpusId))
        {
            console.MarkupLine("[red]Choose either --input or --corpus for the sample source.[/]");
            return 1;
        }
        var target = ProfileFileSizeTargets.ResolveForProfile("piv", settings.SizeProfile);
        if (target.IsFailure)
        {
            console.MarkupLine($"[red]{Markup.Escape(target.Error.Message)}[/]");
            return 1;
        }
        var profile = ProfileSpecifications.Piv.WithFileSizeTarget(target.Value).Value;
        var corpusId = explicitInput ? null : settings.CorpusId ?? "people";
        var resolved = corpusService.ResolveInputs(settings.InputPath, corpusId);
        if (resolved.IsFailure)
        {
            console.MarkupLine($"[red]{Markup.Escape(resolved.Error.Message)}[/]");
            return 1;
        }
        var output = Path.GetFullPath(settings.OutputDirectory);
        if (File.Exists(output) || Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
        {
            console.MarkupLine("[red]Samples require a new or empty output directory.[/]");
            return 1;
        }
        Directory.CreateDirectory(Path.Combine(output, "original"));
        Directory.CreateDirectory(Path.Combine(output, "processed"));
        var assets = new List<object>();
        foreach (var subject in resolved.Value.Where(subject => explicitInput || IsReadmeSubject(subject.Id))
                     .OrderBy(subject => subject.Id, StringComparer.Ordinal))
        {
            var id = subject.Id.Replace('-', '_');
            var bytes = await File.ReadAllBytesAsync(subject.InputPath, cancellationToken).ConfigureAwait(false);
            using var source = Image.Load<Rgba32>(bytes);
            using var thumbnail = source.Clone(c => c.Resize(420, (int)Math.Round(source.Height * 420d / source.Width)));
            var thumbnailPath = Path.Combine(output, "original", id + "_420w.jpg");
            await thumbnail.SaveAsJpegAsync(thumbnailPath, new JpegEncoder { Quality = 90 }, cancellationToken).ConfigureAwait(false);
            var result = await profileEncoder.ProcessAsync(bytes, profile, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                console.MarkupLine($"[red]{Markup.Escape(result.Error.Message)}[/]");
                return 1;
            }
            var encoded = result.Value;
            var encodedPath = Path.Combine(output, "processed", id + "_piv.jp2");
            await File.WriteAllBytesAsync(encodedPath, encoded.ImageData, cancellationToken).ConfigureAwait(false);
            assets.Add(new
            {
                Subject = id, InputPath = Relative(subject.InputPath), SourceSha256 = Hash(bytes), Label = "PIV",
                EncodedPath = Relative(encodedPath), EncodedSha256 = Hash(encoded.ImageData),
                OutputPath = Relative(Path.ChangeExtension(encodedPath, ".png")),
                PreviewStatus = "PendingIndependentDecode", Width = encoded.OutputDimensions.Width,
                Height = encoded.OutputDimensions.Height, Bytes = encoded.ImageData.Length,
                encoded.SourceColorEvidence, encoded.GeometryEvidence, encoded.RoiCoverage,
                EncodingEvidence = encoded.Encoding
            });
            console.MarkupLine($"{Markup.Escape(id)}: {encoded.ImageData.Length:N0} JP2 bytes");
        }
        await DiagnosticsBatchService.WriteManifestAsync(Path.Combine(output, "manifest.json"), new
        {
            Corpus = corpusId ?? "explicit-input", SizeProfile = target.Value.Id,
            Input = explicitInput ? Relative(Path.GetFullPath(settings.InputPath!)) : null,
            PreviewCommand = "python3 scripts/ReadmeGallery/render_diagnostics.py <output-directory>", Assets = assets
        }, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static string Relative(string path) => Path.GetRelativePath(Directory.GetCurrentDirectory(), path).Replace('\\', '/');
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    private static bool IsReadmeSubject(string id) => id is "bush" or "carter" or "generic-guy" or "johnson" or "starmer";
}

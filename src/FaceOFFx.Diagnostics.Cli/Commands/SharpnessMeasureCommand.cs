using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FaceOFFx.Core.Domain.Quality.Assessors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Diagnostics.Cli.Commands;

[Description("Measure sharpness over blur progressions")]
internal sealed class SharpnessMeasureCommand : AsyncCommand<SharpnessMeasureCommand.Settings>
{
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("-i|--input <DIR>")]
        [Description("Input directory containing blur progressions")]
        public string InputDirectory { get; init; } = Path.Combine("artifacts", "diagnostics", "sharpness", "blur-progressions");

        [CommandOption("-t|--threshold <VALUE>")]
        [Description("Sharpness threshold for pass/fail")]
        [DefaultValue(0.7f)]
        public float Threshold { get; init; } = 0.7f;

        [CommandOption("-o|--output <FILE>")]
        [Description("Optional JSON output file")]
        public string? OutputFile { get; init; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var testFiles = Directory.GetFiles(settings.InputDirectory, "*_blur_*.jpg")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (testFiles.Length == 0)
        {
            AnsiConsole.MarkupLine($"[red]No blur images found in {Markup.Escape(settings.InputDirectory)}[/]");
            return 1;
        }

        var results = new List<object>();
        foreach (var file in testFiles)
        {
            using var image = await Image.LoadAsync<Rgba32>(file, cancellationToken);
            var assessment = SharpnessAssessor.Assess(image);
            results.Add(new
            {
                File = Path.GetFileName(file),
                Score = assessment.IsSuccess ? assessment.Value.Overall.Value : 0f,
                HighFrequencyRatio = assessment.IsSuccess ? assessment.Value.HighFrequencyRatio : 0f,
                Passed = assessment.IsSuccess && assessment.Value.Overall.Value >= settings.Threshold,
                Error = assessment.IsFailure ? assessment.Error : null
            });
        }

        if (!string.IsNullOrWhiteSpace(settings.OutputFile))
        {
            var outputPath = Path.IsPathRooted(settings.OutputFile)
                ? settings.OutputFile
                : Path.Combine("artifacts", "diagnostics", "sharpness", settings.OutputFile);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await File.WriteAllTextAsync(
                outputPath,
                JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
        }

        return 0;
    }
}

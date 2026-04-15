using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using FaceOFFx.Core.Domain.Quality.Assessors;
using FaceOFFx.Diagnostics.Cli.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Diagnostics.Cli.Commands;

[Description("Generate blur progressions for sharpness testing")]
internal sealed class SharpnessBlurCommand(
    DiagnosticsCorpusService corpusService) : AsyncCommand<SharpnessBlurCommand.Settings>
{
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[INPUT]")]
        [Description("Source image or directory")]
        public string? InputPath { get; init; }

        [CommandOption("--corpus <ID>")]
        [Description("Named corpus id, such as 'people'")]
        public string? CorpusId { get; init; }

        [CommandOption("-l|--levels <VALUES>")]
        [Description("Comma-separated blur levels")]
        public string? BlurLevels { get; init; }

        [CommandOption("-o|--output <DIR>")]
        [Description("Output directory for blur progressions")]
        public string OutputDirectory { get; init; } = Path.Combine("artifacts", "diagnostics", "sharpness", "blur-progressions");

        public float[] GetBlurLevels()
        {
            if (string.IsNullOrWhiteSpace(BlurLevels))
            {
                return new[] { 0f, 0.3f, 0.5f, 0.63f, 0.7f, 1.0f };
            }

            return BlurLevels.Split(',').Select(value => float.Parse(value.Trim())).OrderBy(value => value).ToArray();
        }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var subjects = corpusService.ResolveInputs(settings.InputPath, settings.CorpusId);
        Directory.CreateDirectory(settings.OutputDirectory);

        foreach (var subject in subjects)
        {
            using var sourceImage = await Image.LoadAsync<Rgba32>(subject.InputPath, cancellationToken);
            foreach (var blurLevel in settings.GetBlurLevels())
            {
                using var processedImage = sourceImage.Clone();
                if (blurLevel > 0)
                {
                    processedImage.Mutate(x => x.GaussianBlur(blurLevel * 2));
                }

                await processedImage.SaveAsJpegAsync(
                    Path.Combine(settings.OutputDirectory, $"{subject.Id}_blur_{blurLevel:F2}.jpg"),
                    cancellationToken);
            }
        }

        return 0;
    }
}

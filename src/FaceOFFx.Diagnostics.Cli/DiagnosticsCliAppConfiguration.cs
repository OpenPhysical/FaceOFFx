using FaceOFFx.Diagnostics.Cli.Commands;
using Spectre.Console.Cli;

namespace FaceOFFx.Diagnostics.Cli;

/// <summary>
/// Configures the diagnostics CLI command surface.
/// </summary>
public static class DiagnosticsCliAppConfiguration
{
    /// <summary>
    /// Registers the diagnostics CLI commands and examples with Spectre.Console.
    /// </summary>
    public static void Configure(IConfigurator config)
    {
        config.SetApplicationName("faceoffx-diagnostics");
        config.SetApplicationVersion(
            typeof(DiagnosticsCliAppConfiguration).Assembly.GetName().Version?.ToString() ?? "Unknown");
        config.ValidateExamples();

        config
            .AddCommand<DetectCommand>("detect")
            .WithDescription("Run staged human-detection visualization and write ordered PNG outputs per subject")
            .WithExample("detect", "--corpus", "people", "--output", "artifacts/diagnostics/detect")
            .WithExample("detect", "tests/test-images/people/generic-guy/source.png", "--output", "artifacts/diagnostics/detect");

        config.AddBranch("sharpness", sharpness =>
        {
            sharpness.SetDescription("Sharpness calibration and generated-image tooling");

            sharpness
                .AddCommand<SharpnessBlurCommand>("blur")
                .WithDescription("Generate blur progressions for the people corpus")
                .WithExample("sharpness", "blur", "--corpus", "people")
                .WithExample("sharpness", "blur", "tests/test-images/people/johnson/source.jpg", "--levels", "0,0.3,0.5,0.63,0.7,1.0");

            sharpness
                .AddCommand<SharpnessMeasureCommand>("measure")
                .WithDescription("Measure sharpness over generated blur progressions")
                .WithExample("sharpness", "measure", "--input", "artifacts/diagnostics/sharpness/blur-progressions")
                .WithExample("sharpness", "measure", "--input", "artifacts/diagnostics/sharpness/blur-progressions", "--output", "sharpness-results.json");

            sharpness
                .AddCommand<SharpnessCleanCommand>("clean")
                .WithDescription("Clean generated sharpness artifacts")
                .WithExample("sharpness", "clean", "--all");
        });
    }
}

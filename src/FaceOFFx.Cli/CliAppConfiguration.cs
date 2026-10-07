using FaceOFFx.Cli.Commands;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli;

/// <summary>
/// Configures the release FaceOFFx CLI command surface.
/// </summary>
public static class CliAppConfiguration
{
    /// <summary>
    /// Registers the release CLI commands and examples with Spectre.Console.
    /// </summary>
    public static void Configure(IConfigurator config)
    {
        config.SetApplicationName("faceoffx");
        config.SetApplicationVersion(
            typeof(CliAppConfiguration).Assembly.GetName().Version?.ToString() ?? "Unknown"
        );
        config.ValidateExamples();

        config.AddCommand<PivCommand>("piv")
            .WithDescription("PIV image preparation, compatibility alias")
            .WithExample("piv", "photo.jpg", "--filesize-target", "minimum", "--output", "photo.jp2");
    }
}

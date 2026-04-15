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

        config
            .AddCommand<PivCommand>("piv")
            .WithDescription("Run the spec-backed PIV document workflow")
            .WithExample("piv", "photo.jpg")
            .WithExample("piv", "photo.jpg", "--variant", "digital")
            .WithExample("piv", "photo.jpg", "--json")
            .WithExample("piv", "photo.jpg", "--explain");

        config
            .AddCommand<UsPassportCommand>("us-passport")
            .WithDescription("Run the U.S. passport document workflow")
            .WithExample("us-passport", "photo.jpg")
            .WithExample("us-passport", "photo.jpg", "--variant", "digital");

        config
            .AddCommand<UsPermanentResidentPhotoCommand>("us-permanent-resident")
            .WithDescription("Run the U.S. permanent resident application photo workflow")
            .WithExample("us-permanent-resident", "photo.jpg")
            .WithExample("us-permanent-resident", "photo.jpg", "--variant", "digital");

        config
            .AddCommand<CanadaPassportCommand>("canada-passport")
            .WithDescription("Run the Canada passport document workflow")
            .WithExample("canada-passport", "photo.jpg");

        config
            .AddCommand<CanadaPermanentResidentCardCommand>("canada-permanent-resident")
            .WithDescription("Run the Canada permanent resident card photo workflow")
            .WithExample("canada-permanent-resident", "photo.jpg")
            .WithExample("canada-permanent-resident", "photo.jpg", "--variant", "digital");

        config
            .AddCommand<CanadaCitizenshipGrantCommand>("canada-citizenship-grant")
            .WithDescription("Run the Canada citizenship grant photo workflow")
            .WithExample("canada-citizenship-grant", "photo.jpg")
            .WithExample("canada-citizenship-grant", "photo.jpg", "--variant", "digital");

        config
            .AddCommand<CanadaProofOfCitizenshipCommand>("canada-proof-of-citizenship")
            .WithDescription("Run the Canada proof of citizenship photo workflow")
            .WithExample("canada-proof-of-citizenship", "photo.jpg")
            .WithExample("canada-proof-of-citizenship", "photo.jpg", "--variant", "digital");

        config
            .AddCommand<DocumentsCommand>("documents")
            .WithDescription("List the supported spec-backed document workflows")
            .WithExample("documents");
    }
}

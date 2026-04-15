using FaceOFFx.Cli.Commands;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli;

internal static class CliAppConfiguration
{
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

#if DEBUG
        config
            .AddCommand<RoiCommand>("roi")
            .WithDescription("Visualize facial ROI Inner Region for engineering validation")
            .WithExample("roi", "photo.jpg")
            .WithExample("roi", "photo.jpg", "--output", "roi_visual.jpg", "--show-landmarks");

        config
            .AddCommand<InteractiveCommand>("interactive")
            .WithDescription("Interactive engineering validation mode")
            .WithExample("interactive")
            .WithExample("interactive", "--directory", "./images");

        config.AddBranch("dataset", dataset =>
        {
            dataset.SetDescription("Dataset validation and batch processing commands");

            dataset
                .AddCommand<DatasetValidateCommand>("validate")
                .WithDescription("Validate and process an entire dataset")
                .WithExample("dataset", "validate", "./dataset/SFHQ-T2I", "--standard", "piv", "--relaxed-crop")
                .WithExample("dataset", "validate", "./dataset/wider-face", "--filter-quality", "--min-face-size", "50")
                .WithExample("dataset", "validate", "./dataset/icao-synthetic", "--standard", "icao", "--interactive");

            dataset
                .AddCommand<DatasetReviewCommand>("review")
                .WithDescription("Review failed images from dataset validation")
                .WithExample("dataset", "review", "./processed_dataset/results.json")
                .WithExample("dataset", "review", "./processed_dataset/results.json", "--min-sharpness", "0.4", "--max-sharpness", "0.7")
                .WithExample("dataset", "review", "./processed_dataset/results.json", "--reason", "LowSharpness", "--output", "override.txt");
        });

        config.AddBranch("test", test =>
        {
            test.SetDescription("Automated testing and calibration tools");

            test
                .AddCommand<TestBlurCommand>("blur")
                .WithDescription("Generate blur progressions for testing")
                .WithExample("test", "blur", "tests/sample_images/*.jpg")
                .WithExample("test", "blur", "tests/sample_images/*.jpg", "--levels", "0,0.3,0.5,0.63,0.7,1.0", "--measure-baseline");

            test
                .AddCommand<TestSharpnessCommand>("sharpness")
                .WithDescription("Test sharpness calibration on blur progressions")
                .WithExample("test", "sharpness", "--threshold", "0.7")
                .WithExample("test", "sharpness", "--input", ".faceoffx-test/blur-progressions", "--output", "sharpness-results.json");

            test
                .AddCommand<TestReportCommand>("report")
                .WithDescription("Generate HTML reports from test results")
                .WithExample("test", "report", "--type", "sharpness-calibration")
                .WithExample("test", "report", "--input", "sharpness-results.json", "--open");

            test
                .AddCommand<TestCleanCommand>("clean")
                .WithDescription("Clean up test artifacts")
                .WithExample("test", "clean", "--all")
                .WithExample("test", "clean", "--blur", "--results", "--force");
        });
#endif
    }
}

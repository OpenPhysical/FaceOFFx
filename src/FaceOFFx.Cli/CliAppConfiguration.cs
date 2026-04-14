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
            .AddCommand<ProcessCommand>("process")
            .WithDescription("Process images for PIV compliance (supports JP2, JPEG, PNG, TIFF output)")
            .WithExample("process", "photo.jpg")
            .WithExample("process", "photo.jpg", "--output", "result.jp2")
            .WithExample("process", "photo.jpg", "--verbose")
            .WithExample("process", "photo.jpg", "--no-resize", "--format", "jpeg", "--no-roi")
            .WithExample("process", "photo.jpg", "--no-resize", "--format", "jp2", "--no-roi")
            .WithExample("process", "photo.jpg", "--format", "png", "--no-roi")
            .WithExample("process", "photo.jpg", "--format", "tiff", "--no-roi")
            .WithExample("process", "photo.jpg", "--quality-gate", "0.8")
            .WithExample("process", "photo.jpg", "--quality-gate", "0.7", "--quality-report");

        config
            .AddCommand<RoiCommand>("roi")
            .WithDescription("Visualize facial ROI Inner Region for JPEG 2000 encoding")
            .WithExample("roi", "photo.jpg")
            .WithExample("roi", "photo.jpg", "--output", "roi_visual.jpg", "--show-landmarks")
            .WithExample("roi", "photo.jpg", "--stroke-width", "5", "--verbose");

        config
            .AddCommand<QualityCommand>("quality")
            .WithDescription("Assess facial image quality according to ISO/IEC 19794-5")
            .WithExample("quality", "--input", "photo.jpg")
            .WithExample("quality", "--input", "photo.jpg", "--standard", "piv", "--format", "json")
            .WithExample("quality", "--input", "photo.jpg", "--threshold", "0.8", "--strict")
            .WithExample("quality", "--input", "photo.jpg", "--format", "detailed", "--visual");

        config
            .AddCommand<InteractiveCommand>("interactive")
            .WithDescription("Interactive validation mode with step-by-step guidance")
            .WithExample("interactive")
            .WithExample("interactive", "--directory", "./images");

        config
            .AddCommand<ValidateCommand>("validate")
            .WithDescription("Quick compliance validation - pass/fail without processing")
            .WithExample("validate", "photo.jpg")
            .WithExample("validate", "photo.jpg", "--standard", "icao")
            .WithExample("validate", "photo.jpg", "--standard", "piv", "--detailed")
            .WithExample("validate", "photo.jpg", "--min-confidence", "0.9");

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
    }
}

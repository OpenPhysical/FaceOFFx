using FaceOFFx.Cli;
using FaceOFFx.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;

// Configure services
var services = new ServiceCollection();

// Configure logging - check for --debug flag in args
var hasDebugFlag = args.Contains("--debug");

// Configure logging
services.AddLogging(builder =>
{
    // Clear existing providers first
    builder.ClearProviders();

    // Set minimum level based on debug flag
    if (hasDebugFlag)
    {
        builder.SetMinimumLevel(LogLevel.Debug);
    }
    else
    {
        builder.SetMinimumLevel(LogLevel.Warning);
    }

    // Use simple console logging for now
    builder.AddSimpleConsole(options =>
    {
        options.IncludeScopes = false;
        options.SingleLine = true;
        options.TimestampFormat = hasDebugFlag ? "HH:mm:ss " : null;
    });
});

// Register all FaceOFFx services
services.AddFaceOffxCli();

// Display banner
AnsiConsole.Write(new FigletText("FaceOFFx").LeftJustified().Color(Color.Blue));

AnsiConsole.MarkupLine("[grey]\"I want to take his face... off.\"[/] - Face/Off (1997)");
AnsiConsole.WriteLine();

// Create the CLI app with dependency injection
using var registrar = new DependencyInjectionRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("faceoffx");
    config.SetApplicationVersion(
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "Unknown"
    );
    config.ValidateExamples();

    // PIV Commands - single clean command
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

    // ROI Commands - visualize facial region for JPEG 2000 encoding
    config
        .AddCommand<RoiCommand>("roi")
        .WithDescription("Visualize facial ROI Inner Region for JPEG 2000 encoding")
        .WithExample("roi", "photo.jpg")
        .WithExample("roi", "photo.jpg", "--output", "roi_visual.jpg", "--show-landmarks")
        .WithExample("roi", "photo.jpg", "--stroke-width", "5", "--verbose");

    // Quality Commands - ISO/IEC 19794-5 quality assessment
    config
        .AddCommand<QualityCommand>("quality")
        .WithDescription("Assess facial image quality according to ISO/IEC 19794-5")
        .WithExample("quality", "--input", "photo.jpg")
        .WithExample("quality", "--input", "photo.jpg", "--standard", "piv", "--format", "json")
        .WithExample("quality", "--input", "photo.jpg", "--threshold", "0.8", "--strict")
        .WithExample("quality", "--input", "photo.jpg", "--format", "detailed", "--visual");

    // Interactive Commands - Step-by-step validation mode
    config
        .AddCommand<InteractiveCommand>("interactive")
        .WithDescription("Interactive validation mode with step-by-step guidance")
        .WithExample("interactive")
        .WithExample("interactive", "--directory", "./images");

    // Universal CLI Commands - Analysis without processing
    config
        .AddCommand<ValidateCommand>("validate")
        .WithDescription("Quick compliance validation - pass/fail without processing")
        .WithExample("validate", "photo.jpg")
        .WithExample("validate", "photo.jpg", "--standard", "icao")
        .WithExample("validate", "photo.jpg", "--standard", "piv", "--detailed")
        .WithExample("validate", "photo.jpg", "--min-confidence", "0.9");
});

// Run the CLI app
try
{
    return await app.RunAsync(args);
}
catch (Exception ex)
{
    AnsiConsole.WriteException(ex);
    return 1;
}

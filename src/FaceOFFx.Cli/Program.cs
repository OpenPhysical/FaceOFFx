using FaceOFFx.Cli;
using FaceOFFx.Cli.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;

// Configure services
var services = new ServiceCollection();
var console = AnsiConsole.Console;
var requiresCleanStdout = StructuredOutputDetector.RequiresCleanStdout(args);

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
        builder.SetMinimumLevel(requiresCleanStdout ? LogLevel.Error : LogLevel.Warning);
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
services.AddSingleton<IAnsiConsole>(console);
services.AddFaceOffxCli();

if (!requiresCleanStdout)
{
    console.MarkupLine(" [grey]╭───╮[/]   [bold blue]Face[/][bold]OFF[/][bold yellow]x[/]");
    console.MarkupLine(" [grey]│[/][bold cyan]◉ ◉[/][grey]│[/]   [grey]──────────────────────────[/]");
    console.MarkupLine(" [grey]│[/][white]╰─╯[/][grey]│[/]   [grey]PIV · Passport · PR Photos[/]");
    console.MarkupLine(" [grey]╰───╯[/]   [dim]\"I want to take his face... off.\"[/]");
    console.WriteLine();
}

// Create the CLI app with dependency injection
using var registrar = new DependencyInjectionRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(CliAppConfiguration.Configure);

// Run the CLI app
try
{
    return await app.RunAsync(args);
}
catch (Exception ex)
{
    console.WriteException(ex);
    return 1;
}

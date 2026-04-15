using FaceOFFx.Diagnostics.Cli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;

var services = new ServiceCollection();
var console = AnsiConsole.Console;
var hasDebugFlag = args.Contains("--debug");

services.AddLogging(builder =>
{
    builder.ClearProviders();
    builder.SetMinimumLevel(hasDebugFlag ? LogLevel.Debug : LogLevel.Warning);
    builder.AddSimpleConsole(options =>
    {
        options.IncludeScopes = false;
        options.SingleLine = true;
        options.TimestampFormat = hasDebugFlag ? "HH:mm:ss " : null;
    });
});

services.AddSingleton<IAnsiConsole>(console);
services.AddFaceOffxDiagnosticsCli();

console.MarkupLine(" [grey]╭───╮[/]   [bold blue]Face[/][bold]OFF[/][bold yellow]x[/] [bold white]Diagnostics[/]");
console.MarkupLine(" [grey]│[/][bold cyan]◉ ◉[/][grey]│[/]   [grey]──────────────────────────[/]");
console.MarkupLine(" [grey]│[/][white]╰─╯[/][grey]│[/]   [grey]Detect · Overlay · Crop[/]");
console.MarkupLine(" [grey]╰───╯[/]   [dim]Engineering corpus and transform inspection[/]");
console.WriteLine();

using var registrar = new DependencyInjectionRegistrar(services);
var app = new CommandApp(registrar);
app.Configure(DiagnosticsCliAppConfiguration.Configure);

try
{
    return await app.RunAsync(args);
}
catch (Exception ex)
{
    console.WriteException(ex);
    return 1;
}

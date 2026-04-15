using FaceOFFx.Diagnostics.Cli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli.Extensions.DependencyInjection;
using Spectre.Console.Testing;

namespace FaceOFFx.Diagnostics.Cli.Tests;

internal static class DiagnosticsCliTestHarness
{
    public static CommandAppTester Create()
    {
        var console = new TestConsole();
        var services = new ServiceCollection();

        services.AddSingleton<IAnsiConsole>(console);
        services.AddLogging(builder => builder.ClearProviders());
        services.AddFaceOffxDiagnosticsCli();

        var registrar = new DependencyInjectionRegistrar(services);
        var settings = new CommandAppTesterSettings { TrimConsoleOutput = false };
        var tester = new CommandAppTester(registrar, settings, console);
        tester.Configure(DiagnosticsCliAppConfiguration.Configure);
        return tester;
    }
}

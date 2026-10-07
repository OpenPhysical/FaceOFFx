using FaceOFFx.Diagnostics.Cli.Commands;
using FaceOFFx.Diagnostics.Cli.Services;
using FaceOFFx.Infrastructure.Services;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace FaceOFFx.Diagnostics.Cli;

/// <summary>
/// Registers FaceOFFx diagnostics CLI services and commands.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds runtime services and diagnostics command registrations.
    /// </summary>
    [UsedImplicitly]
    public static IServiceCollection AddFaceOffxDiagnosticsCli(this IServiceCollection services)
    {
        services.TryAddSingleton<IAnsiConsole>(AnsiConsole.Console);

        services.AddScoped<IFacialProcessingServiceFactory, OnnxFacialProcessingServiceFactory>();

        services.AddScoped<FaceGeometryPipeline>();
        services.AddScoped<ProfileEncoder>();
        services.AddScoped<DiagnosticsCorpusService>();
        services.AddScoped<DiagnosticsBatchService>();

        services.AddTransient<DetectCommand>();
        services.AddTransient<DocsSamplesCommand>();
        services.AddTransient<SharpnessBlurCommand>();
        services.AddTransient<SharpnessMeasureCommand>();
        services.AddTransient<SharpnessCleanCommand>();

        return services;
    }
}

using FaceOFFx.Cli.Commands;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace FaceOFFx.Cli;

/// <summary>
/// Extension methods for registering FaceOFFx CLI services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all FaceOFFx services.
    /// </summary>
    [UsedImplicitly]
    public static IServiceCollection AddFaceOffxCli(this IServiceCollection services)
    {
        // Note: Cannot log during service registration as services aren't built yet
        services.TryAddSingleton<IAnsiConsole>(AnsiConsole.Console);

        services.AddScoped<PivImageEncoder>(provider =>
            new PivImageEncoder(provider.GetRequiredService<ILoggerFactory>()));

        // Register CLI commands
        services.AddTransient<PivCommand>();
        return services;
    }
}

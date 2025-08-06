using FaceOFFx.Cli.Commands;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Infrastructure.Services;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // Register core services as Scoped to avoid early instantiation
        services.AddScoped<IFaceDetector>(sp =>
        {
            var detectorLogger = sp.GetRequiredService<ILogger<RetinaFaceDetector>>();
            return new RetinaFaceDetector(detectorLogger);
        });

        services.AddScoped<ILandmarkExtractor>(sp =>
        {
            var extractorLogger = sp.GetRequiredService<ILogger<OnnxLandmarkExtractor>>();
            return new OnnxLandmarkExtractor(extractorLogger);
        });

        // Use Transient for Jpeg2000EncoderService to avoid static cleanup issues
        services.AddTransient<IJpeg2000Encoder>(sp =>
        {
            var encoderLogger = sp.GetRequiredService<ILogger<Jpeg2000EncoderService>>();
            return new Jpeg2000EncoderService(encoderLogger);
        });

        // Register CLI commands
        services.AddTransient<ProcessCommand>();
        services.AddTransient<RoiCommand>();
        services.AddTransient<QualityCommand>();
        services.AddTransient<ValidateCommand>();
        services.AddTransient<InteractiveCommand>();

        return services;
    }
}

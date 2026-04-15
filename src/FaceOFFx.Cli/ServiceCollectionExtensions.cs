using FaceOFFx.Cli.Commands;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Infrastructure.Services;
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

        services.AddScoped<IFacialProcessingServiceFactory, OnnxFacialProcessingServiceFactory>();

        // Use Transient for Jpeg2000EncoderService to avoid static cleanup issues
        services.AddTransient<IJpeg2000Encoder>(sp =>
        {
            var encoderLogger = sp.GetRequiredService<ILogger<Jpeg2000EncoderService>>();
            return new Jpeg2000EncoderService(encoderLogger);
        });

        // Register quality assessor
        services.AddScoped<IQualityAssessor>(sp =>
        {
            var assessorLogger = sp.GetRequiredService<ILogger<QualityAssessor>>();
            return new QualityAssessor(assessorLogger);
        });

        services.AddScoped<PassportPhotoRenderService>();
        services.AddScoped<FaceGeometryPipeline>();
        services.AddScoped<DocumentRenderService>();
        services.AddScoped<DocumentJobRunner>();

        // Register CLI commands
        services.AddTransient<PivCommand>();
        services.AddTransient<UsPassportCommand>();
        services.AddTransient<UsPermanentResidentPhotoCommand>();
        services.AddTransient<CanadaPassportCommand>();
        services.AddTransient<CanadaPermanentResidentCardCommand>();
        services.AddTransient<CanadaCitizenshipGrantCommand>();
        services.AddTransient<CanadaProofOfCitizenshipCommand>();
        services.AddTransient<DocumentsCommand>();
        return services;
    }
}

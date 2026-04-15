using FaceOFFx.Core.Abstractions;
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

        services.AddTransient<IJpeg2000Encoder>(sp =>
        {
            var encoderLogger = sp.GetRequiredService<ILogger<Jpeg2000EncoderService>>();
            return new Jpeg2000EncoderService(encoderLogger);
        });

        services.AddScoped<IQualityAssessor>(sp =>
        {
            var assessorLogger = sp.GetRequiredService<ILogger<QualityAssessor>>();
            return new QualityAssessor(assessorLogger);
        });

        services.AddScoped<PassportPhotoRenderService>();
        services.AddScoped<FaceGeometryPipeline>();
        services.AddScoped<DocumentRenderService>();
        services.AddScoped<DocumentJobRunner>();
        services.AddScoped<DiagnosticsCorpusService>();
        services.AddScoped<DiagnosticsBatchService>();

        services.AddTransient<DetectCommand>();
        services.AddTransient<CropCommand>();
        services.AddTransient<SharpnessBlurCommand>();
        services.AddTransient<SharpnessMeasureCommand>();
        services.AddTransient<SharpnessCleanCommand>();

        return services;
    }
}

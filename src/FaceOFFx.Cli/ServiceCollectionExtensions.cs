using FaceOFFx.Cli.Commands;
using FaceOFFx.Cli.Services;
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

        // Register quality assessor
        services.AddScoped<IQualityAssessor>(sp =>
        {
            var assessorLogger = sp.GetRequiredService<ILogger<QualityAssessor>>();
            return new QualityAssessor(assessorLogger);
        });

        services.AddScoped<StandardPortraitProcessorService>();
        services.AddScoped<DocumentJobRunner>();
        services.AddScoped<StandardPortraitProcessor>();

        // Register CLI commands
        services.AddTransient<PivCommand>();
        services.AddTransient<UsPassportCommand>();
        services.AddTransient<UsPermanentResidentPhotoCommand>();
        services.AddTransient<CanadaPassportCommand>();
        services.AddTransient<CanadaPermanentResidentCardCommand>();
        services.AddTransient<CanadaCitizenshipGrantCommand>();
        services.AddTransient<CanadaProofOfCitizenshipCommand>();
        services.AddTransient<DocumentsCommand>();
#if DEBUG
        services.AddTransient<RoiCommand>();
        services.AddTransient<InteractiveCommand>();
        services.AddTransient<DatasetCommand>();
        services.AddTransient<DatasetValidateCommand>();
        services.AddTransient<DatasetReviewCommand>();
        services.AddTransient<TestCommand>();
        services.AddTransient<TestBlurCommand>();
        services.AddTransient<TestSharpnessCommand>();
        services.AddTransient<TestReportCommand>();
        services.AddTransient<TestCleanCommand>();
#endif

        return services;
    }
}

using FaceOFFx.Core.Domain.Common;
using Microsoft.Extensions.Logging;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// Provides typed access to the shared ONNX-backed facial processing services for the current scope.
/// </summary>
public interface IFacialProcessingServiceFactory
{
    /// <summary>
    /// Gets the shared facial processing services for the current scope.
    /// </summary>
    Result<FacialProcessingServices, PipelineError> GetServices();
}

/// <summary>
/// Lazily creates and caches the ONNX-backed facial processing services for the current scope.
/// </summary>
public sealed class OnnxFacialProcessingServiceFactory(ILoggerFactory loggerFactory)
    : IFacialProcessingServiceFactory, IDisposable
{
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private Result<FacialProcessingServices, PipelineError>? _cachedServices;

    /// <inheritdoc />
    public Result<FacialProcessingServices, PipelineError> GetServices()
    {
        _cachedServices ??= FacialProcessingServices.Create(_loggerFactory);
        return _cachedServices.Value;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_cachedServices is { IsSuccess: true })
        {
            _cachedServices.Value.Value.Dispose();
        }
    }
}

internal sealed class SharedFacialProcessingServiceFactory(FacialProcessingServices services)
    : IFacialProcessingServiceFactory
{
    private readonly FacialProcessingServices _services = services;

    public Result<FacialProcessingServices, PipelineError> GetServices() =>
        Result.Success<FacialProcessingServices, PipelineError>(_services);
}

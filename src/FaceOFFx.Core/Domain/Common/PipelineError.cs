using CSharpFunctionalExtensions;

namespace FaceOFFx.Core.Domain.Common;

/// <summary>
/// Base typed error for the active geometry, render, and issuance pipeline.
/// </summary>
public abstract record PipelineError(string Code, string Message)
{
    /// <summary>
    /// Returns the human-readable message for command and JSON boundaries.
    /// </summary>
    public override string ToString() => Message;
}

/// <summary>Configuration or catalog failure in the active pipeline.</summary>
public sealed record ConfigurationError(string Message, string? Context = null)
    : PipelineError("configuration", Context is null ? Message : $"{Message} ({Context})");

/// <summary>Input loading or source-image failure in the active pipeline.</summary>
public sealed record InputError(string Message, string? Context = null)
    : PipelineError("input", Context is null ? Message : $"{Message} ({Context})");

/// <summary>Face detection or landmark extraction failure in the active pipeline.</summary>
public sealed record DetectionError(string Message, string? Context = null)
    : PipelineError("detection", Context is null ? Message : $"{Message} ({Context})");

/// <summary>Geometric transform or projection failure in the active pipeline.</summary>
public sealed record GeometryError(string Message, string? Context = null)
    : PipelineError("geometry", Context is null ? Message : $"{Message} ({Context})");

/// <summary>Image render or encoding failure in the active pipeline.</summary>
public sealed record RenderError(string Message, string? Context = null)
    : PipelineError("render", Context is null ? Message : $"{Message} ({Context})");

/// <summary>Validation failure in the active pipeline.</summary>
public sealed record ValidationError(string Message, string? Context = null)
    : PipelineError("validation", Context is null ? Message : $"{Message} ({Context})");

/// <summary>Filesystem persistence failure in the active pipeline.</summary>
public sealed record PersistenceError(string Message, string? Context = null)
    : PipelineError("persistence", Context is null ? Message : $"{Message} ({Context})");

/// <summary>
/// Converts between typed pipeline results and string-based boundaries.
/// </summary>
public static class PipelineResultExtensions
{
    /// <summary>Flattens a typed result to a string-error result.</summary>
    public static Result<T> ToResult<T>(this Result<T, PipelineError> result) =>
        result.IsSuccess
            ? Result.Success(result.Value)
            : Result.Failure<T>(result.Error.Message);

    /// <summary>Flattens a typed unit result to a string-error unit result.</summary>
    public static UnitResult<string> ToUnitResult(this UnitResult<PipelineError> result) =>
        result.IsSuccess
            ? UnitResult.Success<string>()
            : UnitResult.Failure(result.Error.Message);

    /// <summary>Maps a string-error result into a typed pipeline result.</summary>
    public static Result<T, PipelineError> ToPipelineResult<T>(
        this Result<T> result,
        Func<string, PipelineError> mapError) =>
        result.IsSuccess
            ? Result.Success<T, PipelineError>(result.Value)
            : Result.Failure<T, PipelineError>(mapError(result.Error));

    /// <summary>Maps a maybe into a typed pipeline result.</summary>
    public static Result<T, PipelineError> ToPipelineResult<T>(
        this Maybe<T> maybe,
        PipelineError error)
    {
        if (maybe.HasNoValue)
        {
            return Result.Failure<T, PipelineError>(error);
        }

        return Result.Success<T, PipelineError>(maybe.Value);
    }

    /// <summary>Maps a successful typed result and converts thrown exceptions into typed errors.</summary>
    public static Result<K, PipelineError> MapTry<T, K>(
        this Result<T, PipelineError> result,
        Func<T, K> map,
        Func<Exception, PipelineError> mapError)
    {
        if (result.IsFailure)
        {
            return Result.Failure<K, PipelineError>(result.Error);
        }

        try
        {
            return Result.Success<K, PipelineError>(map(result.Value));
        }
        catch (Exception ex)
        {
            return Result.Failure<K, PipelineError>(mapError(ex));
        }
    }

    /// <summary>Binds an async typed result function.</summary>
    public static async Task<Result<K, PipelineError>> BindAsync<T, K>(
        this Task<Result<T, PipelineError>> resultTask,
        Func<T, Task<Result<K, PipelineError>>> bind)
    {
        var result = await resultTask.ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result.Failure<K, PipelineError>(result.Error);
        }

        return await bind(result.Value).ConfigureAwait(false);
    }

    /// <summary>Maps an async typed result function.</summary>
    public static async Task<Result<K, PipelineError>> MapAsync<T, K>(
        this Task<Result<T, PipelineError>> resultTask,
        Func<T, K> map)
    {
        var result = await resultTask.ConfigureAwait(false);
        return result.IsFailure
            ? Result.Failure<K, PipelineError>(result.Error)
            : Result.Success<K, PipelineError>(map(result.Value));
    }
}

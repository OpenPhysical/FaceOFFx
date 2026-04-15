using System.Text.Json;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;

namespace FaceOFFx.Diagnostics.Cli.Services;

internal sealed class DiagnosticsCorpusService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Result<IReadOnlyList<CorpusSubject>, PipelineError> ResolveInputs(string? inputPath, string? corpusId)
    {
        if (!string.IsNullOrWhiteSpace(corpusId))
        {
            return LoadCorpus(corpusId!);
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return Result.Failure<IReadOnlyList<CorpusSubject>, PipelineError>(
                new InputError("Either an input path or --corpus must be provided."));
        }

        var fullPath = Path.GetFullPath(inputPath);
        if (File.Exists(fullPath))
        {
            return Result.Success<IReadOnlyList<CorpusSubject>, PipelineError>(new[]
            {
                new CorpusSubject(
                    Path.GetFileNameWithoutExtension(fullPath),
                    fullPath,
                    null,
                    null)
            });
        }

        if (Directory.Exists(fullPath))
        {
            return Result.Success<IReadOnlyList<CorpusSubject>, PipelineError>(Directory
                .EnumerateFiles(fullPath)
                .Where(IsImagePath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new CorpusSubject(Path.GetFileNameWithoutExtension(path), path, null, null))
                .ToArray());
        }

        return Result.Failure<IReadOnlyList<CorpusSubject>, PipelineError>(
            new InputError($"Input path '{inputPath}' was not found.", inputPath));
    }

    public string CorpusOutputBase(string corpusId) => Path.Combine("artifacts", "diagnostics", corpusId);

    private static Result<IReadOnlyList<CorpusSubject>, PipelineError> LoadCorpus(string corpusId)
    {
        if (!string.Equals(corpusId, "people", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<IReadOnlyList<CorpusSubject>, PipelineError>(
                new ConfigurationError($"Unsupported corpus '{corpusId}'.", corpusId));
        }

        var rootResult = FindSolutionRoot();
        if (rootResult.IsFailure)
        {
            return Result.Failure<IReadOnlyList<CorpusSubject>, PipelineError>(rootResult.Error);
        }

        var root = rootResult.Value;
        var manifestPath = Path.Combine(root, "tests", "test-images", "people", "corpus.json");
        CorpusManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<CorpusManifest>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<CorpusSubject>, PipelineError>(
                new InputError($"Failed to parse corpus manifest '{manifestPath}': {ex.Message}", manifestPath));
        }

        if (manifest is null)
        {
            return Result.Failure<IReadOnlyList<CorpusSubject>, PipelineError>(
                new InputError($"Failed to parse corpus manifest '{manifestPath}'.", manifestPath));
        }

        return Result.Success<IReadOnlyList<CorpusSubject>, PipelineError>(manifest.Subjects
            .Select(subject => new CorpusSubject(
                subject.Id,
                Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, subject.Source)),
                subject.ExpectedFaceCount,
                subject.ExpectLandmarks))
            .ToArray());
    }

    private static Result<string, PipelineError> FindSolutionRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var searchDir = new DirectoryInfo(currentDir);

        while (searchDir != null && !File.Exists(Path.Combine(searchDir.FullName, "FaceOFFx.sln")))
        {
            searchDir = searchDir.Parent;
        }

        return searchDir is not null
            ? Result.Success<string, PipelineError>(searchDir.FullName)
            : Result.Failure<string, PipelineError>(
                new ConfigurationError("Could not find solution root."));
    }

    private static bool IsImagePath(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jp2", StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record CorpusManifest(IReadOnlyList<CorpusManifestSubject> Subjects);

internal sealed record CorpusManifestSubject(
    string Id,
    string Source,
    int? ExpectedFaceCount,
    bool? ExpectLandmarks);

internal sealed record CorpusSubject(
    string Id,
    string InputPath,
    int? ExpectedFaceCount,
    bool? ExpectLandmarks);

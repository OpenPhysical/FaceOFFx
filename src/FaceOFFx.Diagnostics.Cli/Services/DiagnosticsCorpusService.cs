using System.Text.Json;

namespace FaceOFFx.Diagnostics.Cli.Services;

internal sealed class DiagnosticsCorpusService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<CorpusSubject> ResolveInputs(string? inputPath, string? corpusId)
    {
        if (!string.IsNullOrWhiteSpace(corpusId))
        {
            return LoadCorpus(corpusId!);
        }

        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new InvalidOperationException("Either an input path or --corpus must be provided.");
        }

        var fullPath = Path.GetFullPath(inputPath);
        if (File.Exists(fullPath))
        {
            return new[]
            {
                new CorpusSubject(
                    Path.GetFileNameWithoutExtension(fullPath),
                    fullPath,
                    null,
                    null)
            };
        }

        if (Directory.Exists(fullPath))
        {
            return Directory
                .EnumerateFiles(fullPath)
                .Where(IsImagePath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new CorpusSubject(Path.GetFileNameWithoutExtension(path), path, null, null))
                .ToArray();
        }

        throw new FileNotFoundException($"Input path '{inputPath}' was not found.");
    }

    public string CorpusOutputBase(string corpusId) => Path.Combine("artifacts", "diagnostics", corpusId);

    private static IReadOnlyList<CorpusSubject> LoadCorpus(string corpusId)
    {
        if (!string.Equals(corpusId, "people", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported corpus '{corpusId}'.");
        }

        var root = FindSolutionRoot();
        var manifestPath = Path.Combine(root, "tests", "test-images", "people", "corpus.json");
        var manifest = JsonSerializer.Deserialize<CorpusManifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse corpus manifest '{manifestPath}'.");

        return manifest.Subjects
            .Select(subject => new CorpusSubject(
                subject.Id,
                Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, subject.Source)),
                subject.ExpectedFaceCount,
                subject.ExpectLandmarks))
            .ToArray();
    }

    private static string FindSolutionRoot()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var searchDir = new DirectoryInfo(currentDir);

        while (searchDir != null && !File.Exists(Path.Combine(searchDir.FullName, "FaceOFFx.sln")))
        {
            searchDir = searchDir.Parent;
        }

        return searchDir?.FullName
            ?? throw new InvalidOperationException("Could not find solution root.");
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

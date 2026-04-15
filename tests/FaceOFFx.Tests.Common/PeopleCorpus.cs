namespace FaceOFFx.Tests.Common;

public static class PeopleCorpus
{
    public static string FindSolutionRoot()
    {
        var currentDir = AppDomain.CurrentDomain.BaseDirectory;
        var searchDir = new DirectoryInfo(currentDir);

        while (searchDir != null && !File.Exists(Path.Combine(searchDir.FullName, "FaceOFFx.sln")))
        {
            searchDir = searchDir.Parent;
        }

        return searchDir?.FullName
            ?? throw new InvalidOperationException("Could not find solution root.");
    }

    public static string CorpusRoot() =>
        Path.Combine(FindSolutionRoot(), "tests", "test-images", "people");

    public static string SubjectDirectory(string subjectId) =>
        Path.Combine(CorpusRoot(), subjectId);

    public static string SubjectSource(string subjectId, string extension) =>
        Path.Combine(SubjectDirectory(subjectId), $"source.{extension.TrimStart('.')}");

    public static string SubjectVariant(string subjectId, string fileName) =>
        Path.Combine(SubjectDirectory(subjectId), fileName);
}

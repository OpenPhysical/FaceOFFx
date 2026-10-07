namespace FaceOFFx.Cli;

/// <summary>
/// Detects whether a CLI invocation requires clean machine-readable stdout.
/// </summary>
public static class StructuredOutputDetector
{
    /// <summary>
    /// Returns <c>true</c> when the supplied arguments request structured stdout.
    /// </summary>
    public static bool RequiresCleanStdout(string[] args)
    {
        return args.Any(arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase));
    }

}

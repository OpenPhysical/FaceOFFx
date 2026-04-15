namespace FaceOFFx.Cli;

internal static class StructuredOutputDetector
{
    public static bool RequiresCleanStdout(string[] args)
    {
        if (args.Length == 0)
        {
            return false;
        }

        var commandIndex = Array.FindIndex(args, arg => !string.IsNullOrWhiteSpace(arg) && !arg.StartsWith('-'));
        if (commandIndex < 0)
        {
            return false;
        }

        var command = args[commandIndex];
        if (IsDocumentCommand(command))
        {
            return args.Skip(commandIndex + 1)
                .Any(arg => string.Equals(arg, "--json", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static bool IsDocumentCommand(string command) =>
        string.Equals(command, "piv", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "us-passport", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "us-permanent-resident", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "canada-passport", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "canada-permanent-resident", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "canada-citizenship-grant", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "canada-proof-of-citizenship", StringComparison.OrdinalIgnoreCase);
}

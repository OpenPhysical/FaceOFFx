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
        if (!string.Equals(command, "quality", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var i = commandIndex + 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--format", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return string.Equals(args[i + 1], "json", StringComparison.OrdinalIgnoreCase);
            }

            if (arg.StartsWith("--format=", StringComparison.OrdinalIgnoreCase))
            {
                var format = arg["--format=".Length..];
                return string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }
}

using System.Diagnostics;
using FaceOFFx.Tests.Common;
using NUnit.Framework;

namespace FaceOFFx.Cli.Tests;

internal static class CliProcessHarness
{
    public static async Task<(int ExitCode, byte[] Stdout, string Stderr)> Run(params string[] arguments)
    {
        var testDirectory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        var assembly = Path.Combine(PeopleCorpus.FindSolutionRoot(), "artifacts", "bin", "FaceOFFx.Cli",
            testDirectory.Parent!.Name, testDirectory.Name, "FaceOFFx.Cli.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        start.ArgumentList.Add(assembly);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Start the CLI process.");
        await using var stdout = new MemoryStream();
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var errorTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());
        return (process.ExitCode, stdout.ToArray(), await errorTask);
    }
}

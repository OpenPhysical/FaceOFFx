// MIT License
// 
// Copyright (c) 2025 FaceOFFx Contributors
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

/// <summary>
/// Command to clean up test artifacts
/// </summary>
[Description("Clean up test artifacts")]
internal sealed class TestCleanCommand : Command<TestCleanCommand.Settings>
{
    private const string DefaultTestDirectory = ".faceoffx-test";
    
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--all")]
        [Description("Clean all test artifacts")]
        [DefaultValue(false)]
        public bool All { get; set; }

        [CommandOption("--blur")]
        [Description("Clean blur progression images")]
        [DefaultValue(false)]
        public bool Blur { get; set; }

        [CommandOption("--results")]
        [Description("Clean test results")]
        [DefaultValue(false)]
        public bool Results { get; set; }

        [CommandOption("--reports")]
        [Description("Clean generated reports")]
        [DefaultValue(false)]
        public bool Reports { get; set; }
        
        [CommandOption("-f|--force")]
        [Description("Force deletion without confirmation")]
        [DefaultValue(false)]
        public bool Force { get; set; }
    }

    public override int Execute(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        if (!Directory.Exists(DefaultTestDirectory))
        {
            AnsiConsole.MarkupLine("[yellow]No test directory found. Nothing to clean.[/]");
            return 0;
        }

        // If no specific options, default to all
        if (!settings.All && !settings.Blur && !settings.Results && !settings.Reports)
        {
            settings.All = true;
        }

        var itemsToClean = new List<(string Path, string Description)>();

        if (settings.All || settings.Blur)
        {
            var blurDir = Path.Combine(DefaultTestDirectory, "blur-progressions");
            if (Directory.Exists(blurDir))
            {
                itemsToClean.Add((blurDir, "Blur progression images"));
            }
        }

        if (settings.All || settings.Results)
        {
            var resultsDir = Path.Combine(DefaultTestDirectory, "test-results");
            if (Directory.Exists(resultsDir))
            {
                itemsToClean.Add((resultsDir, "Test results"));
            }
        }

        if (settings.All || settings.Reports)
        {
            var reportsDir = Path.Combine(DefaultTestDirectory, "reports");
            if (Directory.Exists(reportsDir))
            {
                itemsToClean.Add((reportsDir, "Generated reports"));
            }
        }

        if (settings.All && itemsToClean.Any())
        {
            // If cleaning all and subdirs exist, clean the entire test directory
            itemsToClean.Clear();
            itemsToClean.Add((DefaultTestDirectory, "All test artifacts"));
        }

        if (!itemsToClean.Any())
        {
            AnsiConsole.MarkupLine("[yellow]Nothing to clean.[/]");
            return 0;
        }

        // Show what will be cleaned
        AnsiConsole.MarkupLine("[yellow]The following will be deleted:[/]");
        foreach (var (path, description) in itemsToClean)
        {
            var info = GetDirectoryInfo(path);
            AnsiConsole.MarkupLine($"  • {description}: [blue]{path}[/] ({info})");
        }

        // Confirm unless forced
        if (!settings.Force)
        {
            if (!AnsiConsole.Confirm("Continue with deletion?"))
            {
                AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
                return 0;
            }
        }

        // Perform cleanup
        foreach (var (path, description) in itemsToClean)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                AnsiConsole.MarkupLine($"[green]✓[/] Deleted {description}");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]✗[/] Failed to delete {description}: {ex.Message}");
            }
        }

        AnsiConsole.MarkupLine("\n[green]Cleanup complete.[/]");
        return 0;
    }

    private static string GetDirectoryInfo(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            var files = dir.GetFiles("*", SearchOption.AllDirectories);
            var totalSize = files.Sum(f => f.Length);
            
            return $"{files.Length} files, {FormatBytes(totalSize)}";
        }
        catch
        {
            return "unknown size";
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}

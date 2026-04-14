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
using FaceOFFx.Core.Domain.Quality.Assessors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

/// <summary>
/// Command to generate blur progressions for testing
/// </summary>
[Description("Generate blur progressions for sharpness testing")]
internal sealed class TestBlurCommand : AsyncCommand<TestBlurCommand.Settings>
{
    private const string DefaultTestDirectory = ".faceoffx-test";
    private const string BlurProgressionsSubdir = "blur-progressions";
    
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "[SOURCE]")]
        [Description("Source image or directory pattern (e.g., tests/sample_images/*.jpg)")]
        public string Source { get; set; } = "tests/sample_images/*_photo.jpg";

        [CommandOption("-l|--levels <VALUES>")]
        [Description("Comma-separated blur levels (e.g., 0,0.3,0.5,0.63,0.7,1.0)")]
        public string? BlurLevels { get; set; }

        [CommandOption("-o|--output <DIR>")]
        [Description("Output directory for blur progressions")]
        public string OutputDirectory { get; set; } = Path.Combine(DefaultTestDirectory, BlurProgressionsSubdir);

        [CommandOption("--quality <VALUE>")]
        [Description("JPEG quality (1-100)")]
        [DefaultValue(95)]
        public int JpegQuality { get; set; } = 95;
        
        [CommandOption("--measure-baseline")]
        [Description("Measure and display baseline sharpness of source images")]
        [DefaultValue(true)]
        public bool MeasureBaseline { get; set; } = true;

        public float[] GetBlurLevels()
        {
            if (string.IsNullOrWhiteSpace(BlurLevels))
            {
                return new[] { 0f, 0.3f, 0.5f, 0.6f, 0.63f, 0.65f, 0.67f, 0.7f, 0.8f, 1.0f };
            }

            return BlurLevels.Split(',')
                .Select(s => float.Parse(s.Trim()))
                .OrderBy(f => f)
                .ToArray();
        }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        // Find source images
        var sourceFiles = FindSourceImages(settings.Source);
        if (!sourceFiles.Any())
        {
            AnsiConsole.MarkupLine("[red]No source images found matching pattern:[/] " + settings.Source);
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Found {sourceFiles.Count} source images[/]");
        
        // Ensure output directory exists
        Directory.CreateDirectory(settings.OutputDirectory);
        
        var blurLevels = settings.GetBlurLevels();
        AnsiConsole.MarkupLine($"[blue]Blur levels:[/] {string.Join(", ", blurLevels)}");

        // Process each source image
        foreach (var sourceFile in sourceFiles)
        {
            await ProcessImageAsync(sourceFile, blurLevels, settings);
        }

        AnsiConsole.MarkupLine($"\n[green]✓ Blur progressions generated in:[/] {settings.OutputDirectory}");
        return 0;
    }

    private static List<string> FindSourceImages(string pattern)
    {
        var files = new List<string>();
        
        // Handle wildcards
        if (pattern.Contains('*') || pattern.Contains('?'))
        {
            var directory = Path.GetDirectoryName(pattern) ?? ".";
            var searchPattern = Path.GetFileName(pattern);
            
            if (Directory.Exists(directory))
            {
                files.AddRange(Directory.GetFiles(directory, searchPattern));
            }
        }
        else if (File.Exists(pattern))
        {
            files.Add(pattern);
        }
        else if (Directory.Exists(pattern))
        {
            // If it's a directory, find all images
            files.AddRange(Directory.GetFiles(pattern, "*.jpg"));
            files.AddRange(Directory.GetFiles(pattern, "*.jpeg"));
            files.AddRange(Directory.GetFiles(pattern, "*.png"));
        }

        return files.OrderBy(f => f).ToList();
    }

    private async Task ProcessImageAsync(string sourceFile, float[] blurLevels, Settings settings)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourceFile);
        
        AnsiConsole.MarkupLine($"\n[yellow]Processing:[/] {baseName}");

        using var sourceImage = await Image.LoadAsync<Rgba32>(sourceFile);
        
        // Measure baseline sharpness if requested
        if (settings.MeasureBaseline)
        {
            var baselineResult = SharpnessAssessor.Assess(sourceImage);
            if (baselineResult.IsSuccess)
            {
                var score = baselineResult.Value.Overall.Value;
                var ratio = baselineResult.Value.HighFrequencyRatio;
                AnsiConsole.MarkupLine($"  [blue]Baseline sharpness:[/] {score:P1} (ratio: {ratio:F4})");
            }
        }

        var table = new Table();
        table.AddColumn("Blur Level");
        table.AddColumn("Output File");
        table.AddColumn("Sharpness Score");
        
        foreach (var blurLevel in blurLevels)
        {
            var outputFile = Path.Combine(settings.OutputDirectory, $"{baseName}_blur_{blurLevel:F2}.jpg");
            
            using var processedImage = sourceImage.Clone();
            
            // Apply blur if level > 0
            if (blurLevel > 0)
            {
                var radius = blurLevel * 2; // Convert sigma to radius
                processedImage.Mutate(x => x.GaussianBlur(radius));
            }
            
            // Save the image
            await processedImage.SaveAsJpegAsync(outputFile, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder
            {
                Quality = settings.JpegQuality
            });
            
            // Measure sharpness
            var result = SharpnessAssessor.Assess(processedImage);
            var sharpnessScore = result.IsSuccess ? $"{result.Value.Overall.Value:P1}" : "Error";
            
            table.AddRow(
                $"{blurLevel:F2}",
                Path.GetFileName(outputFile),
                result.IsSuccess && result.Value.Overall.Value < 0.7f 
                    ? $"[red]{sharpnessScore}[/]" 
                    : $"[green]{sharpnessScore}[/]"
            );
        }
        
        AnsiConsole.Write(table);
    }
}

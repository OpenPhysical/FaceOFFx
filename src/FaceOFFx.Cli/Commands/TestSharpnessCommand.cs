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
using System.Text.Json;
using FaceOFFx.Core.Domain.Quality.Assessors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

/// <summary>
/// Command to test sharpness calibration on blur progressions
/// </summary>
[Description("Test sharpness calibration on blur progressions")]
internal sealed class TestSharpnessCommand : AsyncCommand<TestSharpnessCommand.Settings>
{
    private const string DefaultTestDirectory = ".faceoffx-test";
    
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("-i|--input <DIR>")]
        [Description("Input directory containing blur progressions")]
        public string InputDirectory { get; set; } = Path.Combine(DefaultTestDirectory, "blur-progressions");

        [CommandOption("-t|--threshold <VALUE>")]
        [Description("Sharpness threshold for pass/fail (0.0-1.0)")]
        [DefaultValue(0.7f)]
        public float Threshold { get; set; } = 0.7f;

        [CommandOption("-o|--output <FILE>")]
        [Description("Output JSON file for test results")]
        public string? OutputFile { get; set; }
        
        [CommandOption("--pattern <PATTERN>")]
        [Description("File pattern to test (e.g., *_blur_*.jpg)")]
        [DefaultValue("*_blur_*.jpg")]
        public string Pattern { get; set; } = "*_blur_*.jpg";
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        if (!Directory.Exists(settings.InputDirectory))
        {
            AnsiConsole.MarkupLine($"[red]Input directory not found:[/] {settings.InputDirectory}");
            AnsiConsole.MarkupLine("[yellow]Run 'faceoffx test blur' first to generate test images[/]");
            return 1;
        }

        var testFiles = Directory.GetFiles(settings.InputDirectory, settings.Pattern)
            .OrderBy(f => f)
            .ToList();

        if (!testFiles.Any())
        {
            AnsiConsole.MarkupLine($"[red]No test images found matching:[/] {settings.Pattern}");
            return 1;
        }

        AnsiConsole.MarkupLine($"[green]Found {testFiles.Count} test images[/]");
        AnsiConsole.MarkupLine($"[blue]Threshold:[/] {settings.Threshold:P0}");

        var results = new List<SharpnessTestResult>();
        
        // Group by base image
        var groupedFiles = testFiles
            .GroupBy(f => ExtractBaseName(Path.GetFileName(f)))
            .OrderBy(g => g.Key);

        foreach (var group in groupedFiles)
        {
            AnsiConsole.Write(new Rule($"[yellow]{group.Key}[/]"));
            
            var table = new Table();
            table.AddColumn("Blur Level");
            table.AddColumn("Sharpness Score");
            table.AddColumn("High Freq Ratio");
            table.AddColumn("Pass/Fail");
            table.AddColumn("Baseline Delta");

            float? baselineScore = null;
            float? baselineRatio = null;
            
            foreach (var file in group.OrderBy(f => ExtractBlurLevel(f)))
            {
                var result = await TestImageAsync(file, settings.Threshold);
                results.Add(result);

                // Track baseline (blur 0.00)
                if (result.BlurLevel == 0)
                {
                    baselineScore = result.SharpnessScore;
                    baselineRatio = result.HighFrequencyRatio;
                }

                var deltaString = baselineScore.HasValue && result.SharpnessScore < baselineScore 
                    ? $"-{(baselineScore.Value - result.SharpnessScore):P1}" 
                    : "";
                
                table.AddRow(
                    result.BlurLevel.ToString("F2"),
                    result.Pass 
                        ? $"[green]{result.SharpnessScore:P1}[/]" 
                        : $"[red]{result.SharpnessScore:P1}[/]",
                    result.HighFrequencyRatio.ToString("F4"),
                    result.Pass ? "[green]PASS[/]" : "[red]FAIL[/]",
                    deltaString
                );
            }
            
            AnsiConsole.Write(table);
            
            // Find calibration point
            var calibrationPoint = FindCalibrationPoint(group.Select(f => results.First(r => r.FilePath == f)).ToList(), settings.Threshold);
            if (calibrationPoint.HasValue)
            {
                AnsiConsole.MarkupLine($"[blue]Calibration point:[/] Blur {calibrationPoint.Value:F2} (first to fail at {settings.Threshold:P0} threshold)");
            }
        }

        // Save results if output file specified
        if (!string.IsNullOrWhiteSpace(settings.OutputFile))
        {
            var outputPath = settings.OutputFile;
            if (!Path.IsPathRooted(outputPath))
            {
                outputPath = Path.Combine(DefaultTestDirectory, "test-results", outputPath);
            }
            
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            
            var json = JsonSerializer.Serialize(results, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            await File.WriteAllTextAsync(outputPath, json);
            AnsiConsole.MarkupLine($"\n[green]✓ Results saved to:[/] {outputPath}");
        }

        return 0;
    }

    private static string ExtractBaseName(string fileName)
    {
        // Extract base name from pattern like "johnson_blur_0.63.jpg"
        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"^(.+?)_blur_[\d.]+\.\w+$");
        return match.Success ? match.Groups[1].Value : Path.GetFileNameWithoutExtension(fileName);
    }

    private static float ExtractBlurLevel(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"_blur_([\d.]+)\.");
        return match.Success && float.TryParse(match.Groups[1].Value, out var level) ? level : 0;
    }

    private static async Task<SharpnessTestResult> TestImageAsync(string filePath, float threshold)
    {
        using var image = await Image.LoadAsync<Rgba32>(filePath);
        var assessmentResult = SharpnessAssessor.Assess(image);
        
        var result = new SharpnessTestResult
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            BaseName = ExtractBaseName(Path.GetFileName(filePath)),
            BlurLevel = ExtractBlurLevel(filePath),
            Threshold = threshold,
            Timestamp = DateTime.UtcNow
        };

        if (assessmentResult.IsSuccess)
        {
            result.SharpnessScore = assessmentResult.Value.Overall.Value;
            result.HighFrequencyRatio = assessmentResult.Value.HighFrequencyRatio;
            result.Pass = result.SharpnessScore >= threshold;
            result.RegionalScores = assessmentResult.Value.RegionalScores;
        }
        else
        {
            result.Error = assessmentResult.Error;
        }

        return result;
    }

    private static float? FindCalibrationPoint(List<SharpnessTestResult> results, float threshold)
    {
        var orderedResults = results.OrderBy(r => r.BlurLevel).ToList();
        
        for (int i = 0; i < orderedResults.Count - 1; i++)
        {
            if (orderedResults[i].Pass && !orderedResults[i + 1].Pass)
            {
                // Linear interpolation to find exact crossing point
                var x1 = orderedResults[i].BlurLevel;
                var y1 = orderedResults[i].SharpnessScore;
                var x2 = orderedResults[i + 1].BlurLevel;
                var y2 = orderedResults[i + 1].SharpnessScore;
                
                if (Math.Abs(y2 - y1) > 0.001f)
                {
                    return x1 + (threshold - y1) * (x2 - x1) / (y2 - y1);
                }
            }
        }
        
        return null;
    }
}

/// <summary>
/// Result of a sharpness test on a single image
/// </summary>
internal sealed class SharpnessTestResult
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string BaseName { get; set; } = string.Empty;
    public float BlurLevel { get; set; }
    public float SharpnessScore { get; set; }
    public float HighFrequencyRatio { get; set; }
    public float Threshold { get; set; }
    public bool Pass { get; set; }
    public IReadOnlyDictionary<string, float>? RegionalScores { get; set; }
    public string? Error { get; set; }
    public DateTime Timestamp { get; set; }
}

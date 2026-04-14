// MIT License
// 
// Copyright (c) 2025 FaceOFFx
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
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Standards;
using FaceOFFx.Infrastructure.Extensions;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

/// <summary>
/// Interactive mode for step-by-step validation and processing
/// </summary>
internal sealed class InteractiveCommand : AsyncCommand<InteractiveCommand.Settings>
{
    private readonly IFaceDetector _faceDetector;
    private readonly ILandmarkExtractor _landmarkExtractor;
    private readonly IJpeg2000Encoder _jpeg2000Encoder;
    private readonly IQualityAssessor _qualityAssessor;
    private readonly ILogger<InteractiveCommand> _logger;

    public InteractiveCommand(
        IFaceDetector faceDetector,
        ILandmarkExtractor landmarkExtractor,
        IJpeg2000Encoder jpeg2000Encoder,
        IQualityAssessor qualityAssessor,
        ILogger<InteractiveCommand> logger)
    {
        _faceDetector = faceDetector;
        _landmarkExtractor = landmarkExtractor;
        _jpeg2000Encoder = jpeg2000Encoder;
        _qualityAssessor = qualityAssessor;
        _logger = logger;
    }

    /// <summary>
    /// Settings for the interactive command
    /// </summary>
    internal sealed class Settings : CommandSettings
    {
        /// <summary>
        /// Initial directory to browse for images
        /// </summary>
        [Description("Initial directory to browse")]
        [CommandOption("-d|--directory <PATH>")]
        public string? InitialDirectory { get; set; }
    }

    /// <summary>
    /// Executes the interactive validation workflow
    /// </summary>
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        AnsiConsole.Write(new FigletText("FaceOFFx")
            .LeftJustified()
            .Color(Color.Blue));
        
        AnsiConsole.MarkupLine("[dim]Interactive Validation Mode[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            var choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What would you like to do?")
                    .PageSize(10)
                    .AddChoices(new[]
                    {
                        "Process single image",
                        "Assess image quality",
                        "Batch process directory",
                        "View processing history",
                        "Configure settings",
                        "Exit"
                    }));

            switch (choice)
            {
                case "Process single image":
                    await ProcessSingleImage(settings.InitialDirectory);
                    break;
                    
                case "Assess image quality":
                    await AssessImageQuality(settings.InitialDirectory);
                    break;
                    
                case "Batch process directory":
                    await BatchProcessDirectory(settings.InitialDirectory);
                    break;
                    
                case "View processing history":
                    ViewProcessingHistory();
                    break;
                    
                case "Configure settings":
                    await ConfigureSettings();
                    break;
                    
                case "Exit":
                    AnsiConsole.MarkupLine("[green]Goodbye![/]");
                    return 0;
            }
            
            AnsiConsole.WriteLine();
            AnsiConsole.Prompt(new TextPrompt<string>("[grey]Press Enter to continue...[/]")
                .AllowEmpty()
                .HideChoices()
                .HideDefaultValue());
            AnsiConsole.Clear();
        }
    }

    private async Task ProcessSingleImage(string? initialDirectory)
    {
        // Get image file
        var imagePath = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter image path:")
                .DefaultValue(initialDirectory ?? ".")
                .PromptStyle("green"));

        if (!File.Exists(imagePath))
        {
            AnsiConsole.MarkupLine("[red]File not found![/]");
            return;
        }

        // Show preview info
        var fileInfo = new FileInfo(imagePath);
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Property")
            .AddColumn("Value");
        
        table.AddRow("Filename", fileInfo.Name);
        table.AddRow("Size", $"{fileInfo.Length / 1024} KB");
        table.AddRow("Modified", fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
        
        AnsiConsole.Write(table);

        // Get processing options
        var standard = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select standard:")
                .AddChoices(new[] { "PIV", "TWIC", "ICAO", "CAC" }));

        var targetBpp = AnsiConsole.Prompt(
            new TextPrompt<float>("Target bits per pixel:")
                .DefaultValue(0.9f)
                .Validate(bpp => bpp > 0 && bpp <= 2.0f));

        var assessQuality = AnsiConsole.Confirm("Perform quality assessment?");

        // Process with progress
        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Processing image[/]");
                
                try
                {
                    var imageData = await File.ReadAllBytesAsync(imagePath);
                    
                    task.Increment(25);
                    
                    // Process image - simplified for demo
                    AnsiConsole.MarkupLine("[yellow]Processing with standard: {0}, target BPP: {1}[/]", standard, targetBpp);
                    AnsiConsole.MarkupLine("[dim]Full processing would occur here using face detection, landmark extraction, and JPEG 2000 encoding[/]");
                    
                    task.Increment(50);
                    
                    task.Increment(50);
                    
                    // Simulate success
                    var output = Path.ChangeExtension(imagePath, ".jp2");
                    AnsiConsole.MarkupLine($"[green]Success![/] Would save to {output}");
                    
                    if (assessQuality)
                    {
                        AnsiConsole.MarkupLine("[yellow]Quality assessment would run here[/]");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Exception:[/] {ex.Message}");
                }
            });
    }

    private async Task AssessImageQuality(string? initialDirectory)
    {
        var imagePath = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter image path:")
                .DefaultValue(initialDirectory ?? ".")
                .PromptStyle("green"));

        if (!File.Exists(imagePath))
        {
            AnsiConsole.MarkupLine("[red]File not found![/]");
            return;
        }

        var generateOverlay = AnsiConsole.Confirm("Generate visual overlay?");

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Star)
            .StartAsync("Assessing image quality...", async ctx =>
            {
                try
                {
                    var imageData = await File.ReadAllBytesAsync(imagePath);
                    // Use extension method for quality assessment
                    var result = await imageData.AssessQualityAsync();
                    
                    if (result.IsSuccess)
                    {
                        ShowQualityResults(result.Value);
                        
                        if (generateOverlay)
                        {
                            // Would call visual overlay generation here
                            AnsiConsole.MarkupLine("[yellow]Visual overlay feature requires running 'quality --visual' command[/]");
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Error:[/] {result.Error}");
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Exception:[/] {ex.Message}");
                }
            });
    }

    private async Task BatchProcessDirectory(string? initialDirectory)
    {
        var directory = AnsiConsole.Prompt(
            new TextPrompt<string>("Enter directory path:")
                .DefaultValue(initialDirectory ?? ".")
                .PromptStyle("green"));

        if (!Directory.Exists(directory))
        {
            AnsiConsole.MarkupLine("[red]Directory not found![/]");
            return;
        }

        var pattern = AnsiConsole.Prompt(
            new TextPrompt<string>("File pattern:")
                .DefaultValue("*.jpg"));

        var files = Directory.GetFiles(directory, pattern);
        
        if (files.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No matching files found[/]");
            return;
        }

        AnsiConsole.MarkupLine($"Found [green]{files.Length}[/] files");
        
        if (!AnsiConsole.Confirm("Process all files?"))
            return;

        var results = new List<(string file, bool success, string message)>();

        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Processing files[/]", maxValue: files.Length);
                
                foreach (var file in files)
                {
                    task.Description = $"Processing {Path.GetFileName(file)}";
                    
                    try
                    {
                        var imageData = await File.ReadAllBytesAsync(file);
                        // Simplified for demo
                        results.Add((file, true, "Success (simulated)"));
                    }
                    catch (Exception ex)
                    {
                        results.Add((file, false, ex.Message));
                    }
                    
                    task.Increment(1);
                }
            });

        // Show summary
        var successCount = results.Count(r => r.success);
        var failureCount = results.Count - successCount;
        
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new BarChart()
            .Width(60)
            .Label("[green]Batch Processing Results[/]")
            .AddItem("Success", successCount, Color.Green)
            .AddItem("Failed", failureCount, Color.Red));

        if (failureCount > 0 && AnsiConsole.Confirm("Show failures?"))
        {
            var failureTable = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("File")
                .AddColumn("Error");
            
            foreach (var (file, success, message) in results.Where(r => !r.success))
            {
                failureTable.AddRow(Path.GetFileName(file), message);
            }
            
            AnsiConsole.Write(failureTable);
        }
    }

    private void ViewProcessingHistory()
    {
        // This would read from a log file or database
        AnsiConsole.MarkupLine("[yellow]Processing history not implemented in this demo[/]");
        AnsiConsole.MarkupLine("[dim]This would show recent processing operations and their results[/]");
    }

    private async Task ConfigureSettings()
    {
        var settings = new Dictionary<string, string>
        {
            { "Default Standard", "PIV" },
            { "Target BPP", "0.9" },
            { "Auto Quality Check", "Yes" },
            { "Output Format", "JPEG 2000" }
        };

        var table = new Table()
            .Border(TableBorder.Rounded)
            .Title("Current Settings")
            .AddColumn("Setting")
            .AddColumn("Value");
        
        foreach (var kvp in settings)
        {
            table.AddRow(kvp.Key, kvp.Value);
        }
        
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[dim]Settings configuration would be implemented here[/]");
        
        await Task.CompletedTask;
    }

    private void ShowQualityResults(Core.Domain.Quality.Iso19794Assessment assessment)
    {
        AnsiConsole.Write(new Rule("[yellow]Quality Assessment Results[/]"));
        
        var status = assessment.IsCompliant ? "[green]COMPLIANT[/]" : "[red]NON-COMPLIANT[/]";
        AnsiConsole.MarkupLine($"Status: {status}");
        AnsiConsole.MarkupLine($"Overall Score: [yellow]{assessment.Overall.Value:P0}[/]");
        
        var chart = new BreakdownChart()
            .Width(60)
            .AddItem("Symmetry", (int)(assessment.Symmetry.Overall.Value * 100), Color.Green)
            .AddItem("Sharpness", (int)(assessment.Sharpness.Overall.Value * 100), Color.Yellow)
            .AddItem("Geometry", (int)(assessment.Geometry.Overall.Value * 100), Color.Blue);
        
        AnsiConsole.Write(chart);
        
        if (assessment.Violations.Any())
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Violations:[/]");
            foreach (var violation in assessment.Violations.Take(5))
            {
                AnsiConsole.MarkupLine($"  • {violation.Category}: {violation.Description}");
            }
        }
    }
}

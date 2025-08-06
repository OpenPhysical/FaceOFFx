using System.ComponentModel;
using System.Text.Json;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Infrastructure.Extensions;
using FaceOFFx.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Cli.Commands;

/// <summary>
/// Command for assessing facial image quality according to ISO/IEC 19794-5
/// </summary>
public class QualityCommand : AsyncCommand<QualityCommand.Settings>
{
    public class Settings : CommandSettings
    {
        [Description("Path to the input image file")]
        [CommandOption("-i|--input <PATH>")]
        public string InputPath { get; set; } = string.Empty;

        [Description("Standard to assess against (piv, twic, icao, cac)")]
        [CommandOption("-s|--standard <STANDARD>")]
        [DefaultValue("piv")]
        public string Standard { get; set; } = "piv";

        [Description("Output format (json, text, detailed)")]
        [CommandOption("-f|--format <FORMAT>")]
        [DefaultValue("text")]
        public string OutputFormat { get; set; } = "text";

        [Description("Path to save the quality report")]
        [CommandOption("-o|--output <PATH>")]
        public string? OutputPath { get; set; }

        [Description("Minimum quality threshold (0.0-1.0)")]
        [CommandOption("-t|--threshold <THRESHOLD>")]
        [DefaultValue(0.7f)]
        public float Threshold { get; set; } = 0.7f;

        [Description("Enforce strict compliance")]
        [CommandOption("--strict")]
        public bool Strict { get; set; }

        [Description("Show visual quality overlay (saves image with quality indicators)")]
        [CommandOption("--visual")]
        public bool Visual { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        // Validate input
        if (!File.Exists(settings.InputPath))
        {
            AnsiConsole.MarkupLine($"[red]Error: Input file not found: {settings.InputPath}[/]");
            return 1;
        }

        // Create quality assessment options
        var options = settings.Strict
            ? QualityAssessmentOptions.Strict
            : QualityAssessmentOptions.ForStandard(settings.Standard) with
            {
                MinQualityThreshold = settings.Threshold,
                EnforceCompliance = false
            };

        // Load and assess image
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Star)
            .SpinnerStyle(Style.Parse("green"))
            .StartAsync($"Assessing quality of {Path.GetFileName(settings.InputPath)}...", async ctx =>
            {
                try
                {
                    var imageData = await File.ReadAllBytesAsync(settings.InputPath);
                    var result = await imageData.AssessQualityAsync(options);

                    if (result.IsFailure)
                    {
                        AnsiConsole.MarkupLine($"[red]Quality assessment failed: {result.Error}[/]");
                        return;
                    }

                    var assessment = result.Value;

                    // Output results based on format
                    switch (settings.OutputFormat.ToLower())
                    {
                        case "json":
                            await OutputJson(assessment, settings.OutputPath);
                            break;
                        case "detailed":
                            OutputDetailed(assessment);
                            break;
                        default:
                            OutputText(assessment);
                            break;
                    }

                    // Generate visual overlay if requested
                    if (settings.Visual)
                    {
                        await GenerateVisualOverlay(settings.InputPath, assessment);
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                }
            });

        return 0;
    }

    private static void OutputText(Iso19794Assessment assessment)
    {
        AnsiConsole.WriteLine();
        
        // Overall result
        var status = assessment.IsCompliant ? "[green]COMPLIANT[/]" : "[red]NON-COMPLIANT[/]";
        AnsiConsole.MarkupLine($"Status: {status}");
        AnsiConsole.MarkupLine($"Overall Quality: [yellow]{assessment.Overall}[/]");
        
        // Component scores
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Component")
            .AddColumn("Score")
            .AddColumn("Status");

        table.AddRow(
            "Symmetry",
            assessment.Symmetry.Overall.ToString(),
            GetStatusEmoji(assessment.Symmetry.Overall.Value));
        
        table.AddRow(
            "├─ Illumination",
            assessment.Symmetry.Illumination.ToString(),
            GetStatusEmoji(assessment.Symmetry.Illumination.Value));
        
        table.AddRow(
            "└─ Pose",
            assessment.Symmetry.Pose.ToString(),
            GetStatusEmoji(assessment.Symmetry.Pose.Value));
        
        table.AddRow(
            "Sharpness",
            assessment.Sharpness.Overall.ToString(),
            GetStatusEmoji(assessment.Sharpness.Overall.Value));
        
        table.AddRow(
            "Geometry",
            assessment.Geometry.Overall.ToString(),
            GetStatusEmoji(assessment.Geometry.Overall.Value));
        
        table.AddRow(
            "├─ Head Size",
            assessment.Geometry.HeadSize.ToString(),
            GetStatusEmoji(assessment.Geometry.HeadSize.Value));
        
        table.AddRow(
            "├─ Centering",
            assessment.Geometry.Centering.ToString(),
            GetStatusEmoji(assessment.Geometry.Centering.Value));
        
        table.AddRow(
            "└─ IPD",
            assessment.Geometry.InterPupillaryDistance.ToString(),
            GetStatusEmoji(assessment.Geometry.InterPupillaryDistance.Value));

        AnsiConsole.Write(table);

        // Violations
        if (assessment.Violations.Any())
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Violations:[/]");
            
            foreach (var violation in assessment.Violations.OrderByDescending(v => v.Severity))
            {
                var severityColor = violation.Severity switch
                {
                    ViolationSeverity.Critical => "red",
                    ViolationSeverity.Moderate => "yellow",
                    _ => "gray"
                };
                
                AnsiConsole.MarkupLine($"  [{severityColor}]• {violation.Category}: {violation.Description}[/]");
            }
        }
    }

    private static void OutputDetailed(Iso19794Assessment assessment)
    {
        OutputText(assessment); // Start with basic output
        
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[blue]Detailed Analysis[/]"));
        
        // Sharpness regional scores
        if (assessment.Sharpness.RegionalScores.Any())
        {
            AnsiConsole.MarkupLine("[green]Regional Sharpness:[/]");
            foreach (var (region, score) in assessment.Sharpness.RegionalScores)
            {
                AnsiConsole.MarkupLine($"  {region}: {score:F3} {GetStatusEmoji(score)}");
            }
        }
        
        // Image dimensions
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[green]Image Dimensions:[/]");
        AnsiConsole.MarkupLine($"  Actual: {assessment.Geometry.ActualDimensions.Width}×{assessment.Geometry.ActualDimensions.Height}");
        AnsiConsole.MarkupLine($"  Expected: {assessment.Geometry.ExpectedDimensions.Width}×{assessment.Geometry.ExpectedDimensions.Height}");
        
        // Processing time
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[gray]Assessment completed at: {assessment.AssessmentTime:yyyy-MM-dd HH:mm:ss} UTC[/]");
    }

    private static async Task OutputJson(Iso19794Assessment assessment, string? outputPath)
    {
        var json = JsonSerializer.Serialize(new
        {
            assessment.IsCompliant,
            OverallScore = assessment.Overall.Value,
            assessment.Summary,
            Scores = new
            {
                Symmetry = new
                {
                    Overall = assessment.Symmetry.Overall.Value,
                    Illumination = assessment.Symmetry.Illumination.Value,
                    Pose = assessment.Symmetry.Pose.Value
                },
                Sharpness = new
                {
                    Overall = assessment.Sharpness.Overall.Value,
                    HighFrequencyRatio = assessment.Sharpness.HighFrequencyRatio,
                    Regional = assessment.Sharpness.RegionalScores
                },
                Geometry = new
                {
                    Overall = assessment.Geometry.Overall.Value,
                    HeadSize = assessment.Geometry.HeadSize.Value,
                    Centering = assessment.Geometry.Centering.Value,
                    InterPupillaryDistance = assessment.Geometry.InterPupillaryDistance.Value
                }
            },
            Violations = assessment.Violations.Select(v => new
            {
                v.Category,
                v.Description,
                Severity = v.Severity.ToString()
            }),
            assessment.AssessmentTime
        }, new JsonSerializerOptions { WriteIndented = true });

        if (string.IsNullOrEmpty(outputPath))
        {
            AnsiConsole.WriteLine(json);
        }
        else
        {
            await File.WriteAllTextAsync(outputPath, json);
            AnsiConsole.MarkupLine($"[green]Report saved to: {outputPath}[/]");
        }
    }

    private static string GetStatusEmoji(float score) => score switch
    {
        >= 0.8f => "✅",
        >= 0.6f => "⚠️",
        _ => "❌"
    };

    private static async Task GenerateVisualOverlay(string inputPath, Iso19794Assessment assessment)
    {
        var outputPath = Path.Combine(
            Path.GetDirectoryName(inputPath) ?? ".",
            Path.GetFileNameWithoutExtension(inputPath) + "_quality.png");
        
        try
        {
            // Load the original image
            using var image = await Image.LoadAsync<Rgba32>(inputPath);
            
            // Create quality overlay
            var overlayResult = QualityVisualizationService.CreateQualityOverlay(
                image, 
                assessment,
                showScores: true,
                showViolations: true);
            
            if (overlayResult.IsSuccess)
            {
                // Save the annotated image
                await overlayResult.Value.SaveAsPngAsync(outputPath);
                AnsiConsole.MarkupLine($"[green]Visual overlay saved to: {outputPath}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Failed to create visual overlay: {overlayResult.Error}[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error generating visual overlay: {ex.Message}[/]");
        }
    }
}
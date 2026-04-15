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
internal sealed class QualityCommand(IAnsiConsole console) : AsyncCommand<QualityCommand.Settings>
{
    private readonly IAnsiConsole _console = console ?? throw new ArgumentNullException(nameof(console));

    internal sealed class Settings : CommandSettings
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
        public float? Threshold { get; set; }

        [Description("Enforce strict compliance")]
        [CommandOption("--strict")]
        public bool Strict { get; set; }

        [Description("Show visual quality overlay (saves image with quality indicators)")]
        [CommandOption("--visual")]
        public bool Visual { get; set; }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        if (!string.Equals(settings.OutputFormat, "json", StringComparison.OrdinalIgnoreCase))
        {
            _console.MarkupLine("[yellow]Deprecated:[/] `quality` is an expert command. Use document commands for product workflows.");
        }

        // Validate input
        if (!File.Exists(settings.InputPath))
        {
            _console.MarkupLine($"[red]Error: Input file not found: {settings.InputPath}[/]");
            return 1;
        }

        // Create quality assessment options
        var options = settings.Strict
            ? QualityAssessmentOptions.StrictForStandard(settings.Standard, settings.Threshold)
            : QualityAssessmentOptions.ForStandard(settings.Standard) with
            {
                MinQualityThreshold = settings.Threshold ?? 0.7f,
                EnforceCompliance = false
            };

        try
        {
            var imageData = await File.ReadAllBytesAsync(settings.InputPath);
            var result = await imageData.AssessQualityAsync(options);

            if (result.IsFailure)
            {
                _console.MarkupLine($"[red]Quality assessment failed: {result.Error}[/]");
                return 1;
            }

            var assessment = result.Value;
            var acceptance = QualityAcceptanceEvaluator.Evaluate(assessment, options);

            switch (settings.OutputFormat.ToLowerInvariant())
            {
                case "json":
                    await OutputJson(_console, assessment, acceptance, settings.OutputPath);
                    break;
                case "detailed":
                    OutputDetailed(_console, assessment, acceptance);
                    break;
                default:
                    OutputText(_console, assessment, acceptance);
                    break;
            }

            if (settings.Visual)
            {
                await GenerateVisualOverlay(settings.InputPath, assessment);
            }

            return acceptance.Accepted ? 0 : 1;
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error: {ex.Message}[/]");
            return 1;
        }
    }

    private static void OutputText(
        IAnsiConsole console,
        Iso19794Assessment assessment,
        QualityAcceptanceResult acceptance)
    {
        console.WriteLine();
        
        // Overall result
        var status = acceptance.Accepted ? "[green]COMPLIANT[/]" : "[red]NON-COMPLIANT[/]";
        console.MarkupLine($"Status: {status}");
        console.MarkupLine($"Overall Quality: [yellow]{assessment.Overall}[/]");
        console.MarkupLine(
            $"Native Compliance: {(assessment.IsCompliant ? "[green]Pass[/]" : "[red]Fail[/]")}");

        if (!acceptance.Accepted && !string.IsNullOrWhiteSpace(acceptance.FailureReason))
        {
            console.MarkupLine($"Requested Gate: [red]{acceptance.FailureReason}[/]");
        }
        
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

        console.Write(table);

        // Violations
        if (assessment.Violations.Any())
        {
            console.WriteLine();
            console.MarkupLine("[yellow]Violations:[/]");
            
            foreach (var violation in assessment.Violations.OrderByDescending(v => v.Severity))
            {
                var severityColor = violation.Severity switch
                {
                    ViolationSeverity.Critical => "red",
                    ViolationSeverity.Moderate => "yellow",
                    _ => "gray"
                };
                
                console.MarkupLine($"  [{severityColor}]• {violation.Category}: {violation.Description}[/]");
            }
        }
    }

    private static void OutputDetailed(
        IAnsiConsole console,
        Iso19794Assessment assessment,
        QualityAcceptanceResult acceptance)
    {
        OutputText(console, assessment, acceptance); // Start with basic output
        
        console.WriteLine();
        console.Write(new Rule("[blue]Detailed Analysis[/]"));
        
        // Sharpness regional scores
        if (assessment.Sharpness.RegionalScores.Any())
        {
            console.MarkupLine("[green]Regional Sharpness:[/]");
            foreach (var (region, score) in assessment.Sharpness.RegionalScores)
            {
                console.MarkupLine($"  {region}: {score:F3} {GetStatusEmoji(score)}");
            }
        }
        
        // Image dimensions
        console.WriteLine();
        console.MarkupLine("[green]Image Dimensions:[/]");
        console.MarkupLine($"  Actual: {assessment.Geometry.ActualDimensions.Width}×{assessment.Geometry.ActualDimensions.Height}");
        console.MarkupLine($"  Expected: {assessment.Geometry.ExpectedDimensions.Width}×{assessment.Geometry.ExpectedDimensions.Height}");
        
        // Processing time
        console.WriteLine();
        console.MarkupLine($"[gray]Assessment completed at: {assessment.AssessmentTime:yyyy-MM-dd HH:mm:ss} UTC[/]");
    }

    private static async Task OutputJson(
        IAnsiConsole console,
        Iso19794Assessment assessment,
        QualityAcceptanceResult acceptance,
        string? outputPath)
    {
        var json = JsonSerializer.Serialize(new
        {
            Accepted = acceptance.Accepted,
            assessment.IsCompliant,
            RequestedGateFailureReason = acceptance.FailureReason,
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
            console.WriteLine(json);
        }
        else
        {
            await File.WriteAllTextAsync(outputPath, json);
            console.MarkupLine($"[green]Report saved to: {outputPath}[/]");
        }
    }

    private static string GetStatusEmoji(float score) => score switch
    {
        >= 0.8f => "✅",
        >= 0.6f => "⚠️",
        _ => "❌"
    };

    private async Task GenerateVisualOverlay(string inputPath, Iso19794Assessment assessment)
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
                _console.MarkupLine($"[green]Visual overlay saved to: {outputPath}[/]");
            }
            else
            {
                _console.MarkupLine($"[red]Failed to create visual overlay: {overlayResult.Error}[/]");
            }
        }
        catch (Exception ex)
        {
            _console.MarkupLine($"[red]Error generating visual overlay: {ex.Message}[/]");
        }
    }
}

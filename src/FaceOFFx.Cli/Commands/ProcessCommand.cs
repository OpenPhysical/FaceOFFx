using System.ComponentModel;
using System.Text.Json;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Extensions;
using FaceOFFx.Infrastructure.Services;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

/// <summary>
/// Clean PIV command - just: input image → PIV compliant output
/// </summary>
/// <remarks>
/// Processes an input image for PIV usage
/// </remarks>
/// <param name="faceDetector"></param>
/// <param name="landmarkExtractor"></param>
/// <param name="jpeg2000Encoder"></param>
/// <param name="logger"></param>
/// <param name="console"></param>
/// <exception cref="ArgumentNullException"></exception>
[Description("Process image for PIV compliance")]
internal sealed class ProcessCommand(
    IFaceDetector faceDetector,
    ILandmarkExtractor landmarkExtractor,
    IJpeg2000Encoder jpeg2000Encoder,
    ILogger<ProcessCommand> logger,
    IAnsiConsole console
) : AsyncCommand<ProcessCommand.Settings>
{
    /// <inheritdoc />
    [UsedImplicitly]
    internal sealed class Settings : CommandSettings
    {
        /// <summary>
        /// Input file path
        /// </summary>
        [CommandArgument(0, "<INPUT>")]
        [Description("Input image file path")]
        [UsedImplicitly]
        public string InputPath { get; set; } = string.Empty;

        /// <summary>
        /// Output File Path
        /// </summary>
        [CommandOption("-o|--output <PATH>")]
        [Description("Output file path")]
        [UsedImplicitly]
        public string? OutputPath { get; set; }

        /// <summary>
        /// JPEG 2000 base compression rate in bits per pixel
        /// </summary>
        [CommandOption("--rate <RATE>")]
        [Description("JPEG 2000 base compression rate in bits per pixel")]
        [DefaultValue(0.7f)]
        public float BaseRate { get; set; } = 0.7f;

        /// <summary>
        /// ROI resolution level priority: 0=aggressive, 1=balanced, 2=conservative
        /// </summary>
        [CommandOption("--roi-level <LEVEL>")]
        [Description(
            "ROI resolution level priority: 0=aggressive, 1=balanced, 2=conservative, 3=smoothest"
        )]
        [DefaultValue(3)]
        public int RoiStartLevel { get; set; } = 3;

        /// <summary>
        /// If true, disable ROI encoding (enabled by default with level 3)
        /// </summary>
        [CommandOption("--no-roi")]
        [Description("Disable ROI encoding for uniform quality")]
        public bool NoRoi { get; set; }

        /// <summary>
        /// If true, enable ROI alignment with blocks (disabled by default for smoothest transitions)
        /// </summary>
        [CommandOption("--align")]
        [Description("Enable ROI alignment with compression blocks (may create harsh boundaries)")]
        public bool Align { get; set; }

        /// <summary>
        /// Show detailed processing information
        /// </summary>
        [CommandOption("--verbose")]
        [Description("Show detailed processing information")]
        public bool Verbose { get; set; }

        /// <summary>
        /// Enable debug logging
        /// </summary>
        [CommandOption("--debug")]
        [Description("Enable debug logging for troubleshooting")]
        public bool Debug { get; set; }

        /// <summary>
        /// Processing preset to use
        /// </summary>
        [CommandOption("--preset <PRESET>")]
        [Description(
            "Processing preset: piv-high (30KB), piv-balanced (20KB), twic-max (14KB), piv-min (12KB)"
        )]
        public string? Preset { get; set; }

        /// <summary>
        /// Target file size in bytes (overrides preset and rate)
        /// </summary>
        [CommandOption("--target-size <SIZE>")]
        [Description("Target file size in bytes (overrides preset and rate)")]
        public int? TargetSize { get; set; }

        /// <summary>
        /// Minimum face detection confidence
        /// </summary>
        [CommandOption("--min-confidence <CONFIDENCE>")]
        [Description("Minimum face detection confidence (0.0 to 1.0)")]
        [DefaultValue(0.8f)]
        public float MinConfidence { get; set; } = 0.8f;

        /// <summary>
        /// Maximum rotation angle for face alignment
        /// </summary>
        [CommandOption("--max-rotation <DEGREES>")]
        [Description("Maximum rotation angle in degrees for face alignment")]
        [DefaultValue(15.0f)]
        public float MaxRotation { get; set; } = 15.0f;

        /// <summary>
        /// Skip geometric resize and preserve the original image dimensions in the output.
        /// </summary>
        [CommandOption("--no-resize")]
        [Description("Skip geometric resize and preserve the original image dimensions in the output")]
        public bool NoResize { get; set; }

        /// <summary>
        /// Output format
        /// </summary>
        [CommandOption("--format <FORMAT>")]
        [Description(
            "Output format: jp2 (default), jpeg, png, or tiff. Non-JP2 formats require --no-roi"
        )]
        [DefaultValue("jp2")]
        public string Format { get; set; } = "jp2";

        /// <summary>
        /// JPEG quality setting
        /// </summary>
        [CommandOption("--jpeg-quality <QUALITY>")]
        [Description("JPEG quality (1-100, default: 85)")]
        [DefaultValue(85)]
        public int JpegQuality { get; set; } = 85;

        /// <summary>
        /// Minimum quality score (0.0-1.0) required for processing
        /// </summary>
        [CommandOption("--quality-gate <SCORE>")]
        [Description("Minimum quality score (0.0-1.0) required for processing")]
        public float? QualityGate { get; set; }

        /// <summary>
        /// Generate quality assessment report
        /// </summary>
        [CommandOption("--quality-report")]
        [Description("Generate quality assessment report")]
        public bool GenerateQualityReport { get; set; }

        /// <summary>
        /// Quality assessment standard (piv, twic, icao, cac)
        /// </summary>
        [CommandOption("--quality-standard <STANDARD>")]
        [Description("Quality assessment standard (piv, twic, icao, cac)")]
        [DefaultValue("piv")]
        public string QualityStandard { get; set; } = "piv";
        
        /// <summary>
        /// Apply strict quality requirements (0.85 minimum)
        /// </summary>
        [CommandOption("--strict")]
        [Description("Apply strict quality requirements (0.85 minimum)")]
        public bool Strict { get; set; }
    }

    private readonly IFaceDetector _faceDetector =
        faceDetector ?? throw new ArgumentNullException(nameof(faceDetector));
    private readonly ILandmarkExtractor _landmarkExtractor =
        landmarkExtractor ?? throw new ArgumentNullException(nameof(landmarkExtractor));
    private readonly IJpeg2000Encoder _jpeg2000Encoder =
        jpeg2000Encoder ?? throw new ArgumentNullException(nameof(jpeg2000Encoder));
    private readonly ILogger<ProcessCommand> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IAnsiConsole _console = console ?? throw new ArgumentNullException(nameof(console));

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        try
        {
            _console.MarkupLine("[yellow]Deprecated:[/] `process` is an expert command. Use document commands such as `piv`, `us-passport`, or `canada-passport` for product workflows.");

            // Validate input
            if (!File.Exists(settings.InputPath))
            {
                _logger.LogError("Input file not found: {InputPath}", settings.InputPath);
                _console.MarkupLine(
                    $"[red]Error: Input file '{settings.InputPath}' not found.[/]"
                );
                return 1;
            }

            // Validate quality gate
            if (settings.QualityGate.HasValue && (settings.QualityGate.Value < 0.0f || settings.QualityGate.Value > 1.0f))
            {
                _logger.LogError("Invalid quality gate value: {QualityGate}. Must be between 0.0 and 1.0.", settings.QualityGate);
                _console.MarkupLine(
                    $"[red]Error: Invalid quality gate value '{settings.QualityGate}'. Must be between 0.0 and 1.0.[/]"
                );
                return 1;
            }
            
            // Apply strict mode override
            if (settings.Strict)
            {
                settings.QualityGate = 0.85f;
                if (settings.Verbose)
                {
                    _console.MarkupLine("[yellow]Strict mode enabled: quality gate set to 0.85[/]");
                }
            }
            
            // Validate format
            var validFormats = new[] { "jp2", "jpeg", "jpg", "png", "tiff", "tif" };
            if (!validFormats.Contains(settings.Format, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "Invalid output format: {Format}. Must be one of: {ValidFormats}.",
                    settings.Format,
                    string.Join(", ", validFormats)
                );
                _console.MarkupLine(
                    $"[red]Error: Invalid output format '{settings.Format}'. Must be one of: {string.Join(", ", validFormats)}.[/]"
                );
                return 1;
            }

            // Validate ROI requirements for non-JP2 formats
            var isJp2Format = settings.Format.Equals("jp2", StringComparison.OrdinalIgnoreCase);
            if (!isJp2Format && !settings.NoRoi)
            {
                _logger.LogError(
                    "ROI is not supported for {Format} format. Use --no-roi flag.",
                    settings.Format
                );
                _console.MarkupLine(
                    $"[red]Error: ROI encoding is not supported for {settings.Format.ToUpperInvariant()} format. You must use the --no-roi flag.[/]"
                );
                return 1;
            }

            // Determine the output path
            var outputPath =
                settings.OutputPath ?? GenerateOutputPath(settings.InputPath, settings.Format);
            _logger.LogDebug("Output path determined: {OutputPath}", outputPath);

            // Determine processing options based on parameters
            var processingOptions = DetermineProcessingOptions(settings, _logger);

            // Process the image
            bool success = false;

            if (settings.Debug)
            {
                success = await ExecuteProcessingAsync(
                    settings,
                    outputPath,
                    processingOptions,
                    updateStatus: null
                );
            }
            else
            {
                await _console
                    .Status()
                    .StartAsync(
                        settings.NoResize
                            ? "Processing image..."
                            : "Processing image for face recognition...",
                        async ctx =>
                        {
                            success = await ExecuteProcessingAsync(
                                settings,
                                outputPath,
                                processingOptions,
                                status => ctx.Status(status)
                            );
                        }
                    );
            }

            return success ? 0 : 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in ProcessCommand.ExecuteAsync");
            _console.WriteException(ex);
            return 1;
        }
    }

    private async Task<bool> ExecuteProcessingAsync(
        Settings settings,
        string outputPath,
        ProcessingOptions processingOptions,
        Action<string>? updateStatus
    )
    {
        try
        {
            updateStatus?.Invoke("Loading image...");
            var imageData = await File.ReadAllBytesAsync(settings.InputPath);

            if (settings.Verbose)
            {
                _console.MarkupLine($"[blue]Source image: {imageData.Length} bytes[/]");
            }

            _logger.LogDebug(
                "Starting face processing with options: {Options}",
                processingOptions
            );

            var qualityAssessment = await AssessQualityAsync(imageData, settings, updateStatus);

            if (settings.NoResize)
            {
                updateStatus?.Invoke("Processing image without geometric resize...");
                _logger.LogDebug("Using no-resize mode for output generation");
                _console.MarkupLine("[yellow]No resize requested - preserving original image geometry[/]");
            }
            else
            {
                updateStatus?.Invoke("Processing with facial image encoder...");
            }

            var result = settings.NoResize
                ? await FacialImageEncoder.ProcessWithoutFaceDetectionAsync(
                    imageData,
                    processingOptions,
                    settings.Format,
                    settings.JpegQuality,
                    _logger
                )
                : await FacialImageEncoder.ProcessAsync(
                    imageData,
                    processingOptions,
                    settings.Format,
                    settings.JpegQuality,
                    _logger
                );

            _logger.LogInformation("Image processing completed successfully");
            await HandleSuccess(result, outputPath, settings, qualityAssessment, _logger, _console);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Face processing failed: {Error}", ex.Message);
            HandleFailure(ex.Message, _logger, _console);
            return false;
        }
    }

    private async Task<Iso19794Assessment?> AssessQualityAsync(
        byte[] imageData,
        Settings settings,
        Action<string>? updateStatus
    )
    {
        if (!settings.QualityGate.HasValue && !settings.GenerateQualityReport)
        {
            return null;
        }

        updateStatus?.Invoke("Performing quality assessment...");
        _logger.LogDebug(
            "Performing quality assessment with gate: {QualityGate}, standard: {Standard}",
            settings.QualityGate,
            settings.QualityStandard
        );

        var qualityOptions = QualityAssessmentOptions.ForStandard(settings.QualityStandard) with
        {
            MinQualityThreshold = settings.QualityGate ?? 0.7f,
            EnforceCompliance = false,
        };

        var qualityResult = await imageData.AssessQualityAsync(qualityOptions, _logger);
        if (qualityResult.IsFailure)
        {
            throw new InvalidOperationException(qualityResult.Error);
        }

        var qualityAssessment = qualityResult.Value;
        if (!settings.QualityGate.HasValue)
        {
            return qualityAssessment;
        }

        if (qualityAssessment.Overall.Value >= settings.QualityGate.Value)
        {
            _console.MarkupLine(
                $"[green]Quality assessment passed (score: {qualityAssessment.Overall.Value:F2})[/]"
            );
            return qualityAssessment;
        }

        throw new InvalidOperationException(
            $"Quality score {qualityAssessment.Overall.Value:F2} below quality threshold {settings.QualityGate.Value:F2}"
        );
    }

    /// <summary>
    /// Handles processing success
    /// </summary>
    /// <param name="result">Processing result</param>
    /// <param name="outputPath">Output file path</param>
    /// <param name="settings">Command settings</param>
    /// <param name="qualityAssessment">Optional quality assessment for the processed image.</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="console">Console used for command output.</param>
    private static async Task HandleSuccess(
        ProcessingResultDto result,
        string outputPath,
        Settings settings,
        Iso19794Assessment? qualityAssessment,
        ILogger<ProcessCommand> logger,
        IAnsiConsole console
    )
    {
        // Save the encoded JPEG 2000 image data
        await File.WriteAllBytesAsync(outputPath, result.ImageData);

        console.WriteLine();
        console.MarkupLine($"[green]✓ Face processing completed successfully![/]");
        
        // Show quality assessment status if quality gate was used
        if (settings.QualityGate.HasValue && qualityAssessment != null)
        {
            if (qualityAssessment.Overall.Value >= settings.QualityGate.Value)
            {
                console.MarkupLine($"[green]✓ Quality assessment passed (score: {qualityAssessment.Overall.Value:F2})[/]");
            }
        }
        
        console.MarkupLine($"[green]Output saved to: {outputPath}[/]");

        // Show processing results
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Processing Results[/]");

        table.AddColumn("Property");
        table.AddColumn("Value");

        var metadata = result.Metadata;
        table.AddRow(
            "Output Dimensions",
            $"{metadata.OutputDimensions.Width}x{metadata.OutputDimensions.Height}"
        );
        table.AddRow("File Size", $"{metadata.FileSize:N0} bytes");
        table.AddRow("Face Confidence", $"{metadata.FaceConfidence:F2}");
        table.AddRow("Processing Time", $"{metadata.ProcessingTime.TotalMilliseconds:F0}ms");

        if (metadata.CompressionRate > 0)
            table.AddRow("Compression Rate", $"{metadata.CompressionRate:F2} bpp");

        if (metadata.TargetSize.HasValue)
            table.AddRow("Target Size", $"{metadata.TargetSize.Value:N0} bytes");

        console.Write(table);

        // Show quality assessment if available
        if (qualityAssessment != null)
        {
            console.WriteLine();
            
            // Show quality assessment header
            console.MarkupLine("[bold]Quality assessment[/]");
            
            // In verbose mode, show detailed scores
            if (settings.Verbose)
            {
                console.MarkupLine($"Overall score: {qualityAssessment.Overall.Value:F2}");
                console.MarkupLine($"Symmetry: {qualityAssessment.Symmetry.Overall.Value:F2}");
                console.MarkupLine($"Sharpness: {qualityAssessment.Sharpness.Overall.Value:F2}");
                console.MarkupLine($"Geometry: {qualityAssessment.Geometry.Overall.Value:F2}");
                console.WriteLine();
            }
            
            var qualityTable = new Table()
                .Border(TableBorder.Rounded)
                .Title($"[bold]Quality Assessment - {(qualityAssessment.IsCompliant ? "[green]COMPLIANT[/]" : "[red]NON-COMPLIANT[/]")}[/]");
            qualityTable.AddColumn("Metric");
            qualityTable.AddColumn("Score");
            
            qualityTable.AddRow("Overall Quality", qualityAssessment.Overall.ToString());
            qualityTable.AddRow("├─ Symmetry", qualityAssessment.Symmetry.Overall.ToString());
            qualityTable.AddRow("├─ Sharpness", qualityAssessment.Sharpness.Overall.ToString());
            qualityTable.AddRow("└─ Geometry", qualityAssessment.Geometry.Overall.ToString());
            
            console.Write(qualityTable);
            
            if (qualityAssessment.Violations.Any())
            {
                console.MarkupLine("[yellow]Quality Violations:[/]");
                foreach (var violation in qualityAssessment.Violations.OrderByDescending(v => v.Severity))
                {
                    var color = violation.Severity switch
                    {
                        ViolationSeverity.Critical => "red",
                        ViolationSeverity.Moderate => "yellow",
                        _ => "gray"
                    };
                    console.MarkupLine($"  [{color}]• {violation.Category}: {violation.Description}[/]");
                }
            }
            
            // Save quality report if requested
            if (settings.GenerateQualityReport)
            {
                var reportPath = Path.ChangeExtension(outputPath, ".quality.json");
                var reportJson = JsonSerializer.Serialize(new
                {
                    qualityAssessment.IsCompliant,
                    OverallScore = qualityAssessment.Overall.Value,
                    Scores = new
                    {
                        Symmetry = qualityAssessment.Symmetry.Overall.Value,
                        Sharpness = qualityAssessment.Sharpness.Overall.Value,
                        Geometry = qualityAssessment.Geometry.Overall.Value
                    },
                    qualityAssessment.Violations,
                    qualityAssessment.AssessmentTime,
                    ProcessingResult = new
                    {
                        metadata.OutputDimensions,
                        metadata.FileSize,
                        metadata.FaceConfidence,
                        metadata.ProcessingTime
                    }
                }, new JsonSerializerOptions { WriteIndented = true });
                
                await File.WriteAllTextAsync(reportPath, reportJson);
                console.MarkupLine($"[blue]Quality report saved to: {reportPath}[/]");
            }
        }

        // Show verbose details
        if (settings.Verbose && metadata.AdditionalData.Any())
        {
            console.WriteLine();
            console.MarkupLine("[cyan]Additional Details:[/]");
            foreach (var kvp in metadata.AdditionalData)
            {
                var valueStr = kvp.Value?.ToString() ?? "null";
                console.MarkupLine(
                    $"[cyan]  {kvp.Key}: {valueStr.Replace("[", "[[").Replace("]", "]]")}[/]"
                );
            }
        }
    }

    /// <summary>
    /// Handles encoding failures
    /// </summary>
    /// <param name="error"></param>
    /// <param name="logger"></param>
    /// <param name="console">Console used for command output.</param>
    private static void HandleFailure(string error, ILogger<ProcessCommand> logger, IAnsiConsole console)
    {
        console.WriteLine();
        console.MarkupLine($"[red]✗ Processing failed: {error}[/]");

        // Provide helpful suggestions based on error type
        if (error.Contains("No faces detected"))
        {
            console.MarkupLine("[yellow]💡 Suggestions:[/]");
            console.MarkupLine(
                "[yellow]  • Ensure the image contains a clear, visible face[/]"
            );
            console.MarkupLine("[yellow]  • Check that the image is well-lit and in focus[/]");
        }
        else if (error.Contains("Multiple faces"))
        {
            console.MarkupLine("[yellow]💡 Suggestions:[/]");
            console.MarkupLine("[yellow]  • Crop the image to contain only one face[/]");
        }
    }

    /// <summary>
    /// Converts the input extension to the appropriate output format.
    /// </summary>
    /// <param name="inputPath"></param>
    /// <param name="format"></param>
    /// <returns></returns>
    private static string GenerateOutputPath(string inputPath, string format = "jp2")
    {
        var directory = Path.GetDirectoryName(inputPath) ?? ".";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);

        var extension = format.ToLowerInvariant() switch
        {
            "jpeg" or "jpg" => "jpg",
            "png" => "png",
            "tiff" or "tif" => "tiff",
            _ => "jp2",
        };

        return Path.Combine(directory, $"{nameWithoutExt}.{extension}");
    }

    /// <summary>
    /// Determines processing options based on command line settings
    /// </summary>
    private static ProcessingOptions DetermineProcessingOptions(
        Settings settings,
        ILogger<ProcessCommand> logger
    )
    {
        // Check for preset first
        if (!string.IsNullOrEmpty(settings.Preset))
        {
            var preset = GetPresetFromString(settings.Preset, logger);
            if (preset != null)
            {
                logger.LogDebug("Using preset: {Preset}", settings.Preset);
                return ApplyOverrides(preset, settings, logger);
            }
        }

        // Check for target size
        if (settings.TargetSize.HasValue)
        {
            logger.LogDebug("Using target size: {TargetSize} bytes", settings.TargetSize.Value);
            var options = ProcessingOptions.PivBalanced with
            {
                Strategy = EncodingStrategy.TargetSize(settings.TargetSize.Value),
            };
            return ApplyOverrides(options, settings, logger);
        }

        // Use custom rate if specified
        if (settings.BaseRate > 0)
        {
            logger.LogDebug("Using custom rate: {Rate} bpp", settings.BaseRate);
            var options = ProcessingOptions.PivBalanced with
            {
                Strategy = EncodingStrategy.FixedRate(settings.BaseRate),
            };
            return ApplyOverrides(options, settings, logger);
        }

        // Default to PIV standard
        logger.LogDebug("Using default PIV standard options");
        return ApplyOverrides(ProcessingOptions.PivBalanced, settings, logger);
    }

    /// <summary>
    /// Gets a processing preset from string name
    /// </summary>
    private static ProcessingOptions? GetPresetFromString(
        string presetName,
        ILogger<ProcessCommand> logger
    )
    {
        return presetName.ToLowerInvariant() switch
        {
            "twic-max" => ProcessingOptions.TwicMax,
            "piv-min" => ProcessingOptions.PivMin,
            "piv-balanced" => ProcessingOptions.PivBalanced,
            "piv-high" => ProcessingOptions.PivHigh,
            "piv-veryhigh" => ProcessingOptions.PivVeryHigh,
            "archival" => ProcessingOptions.Archival,
            "minimal" => ProcessingOptions.Minimal,
            "fast" => ProcessingOptions.Fast,
            _ => null,
        };
    }

    /// <summary>
    /// Applies CLI setting overrides to processing options
    /// </summary>
    private static ProcessingOptions ApplyOverrides(
        ProcessingOptions baseOptions,
        Settings settings,
        ILogger<ProcessCommand> logger
    )
    {
        var options = baseOptions;

        // Apply ROI level override
        if (settings.RoiStartLevel >= 0 && settings.RoiStartLevel <= 3)
        {
            if (settings.RoiStartLevel != baseOptions.RoiStartLevel)
            {
                logger.LogDebug(
                    "Overriding ROI level: {OldLevel} → {NewLevel}",
                    baseOptions.RoiStartLevel,
                    settings.RoiStartLevel
                );
                options = options with { RoiStartLevel = settings.RoiStartLevel };
            }
        }

        // Apply ROI enable/disable override
        if (settings.NoRoi && baseOptions.EnableRoi)
        {
            logger.LogDebug("Disabling ROI encoding");
            options = options with { EnableRoi = false };
        }

        // Apply ROI alignment override
        if (settings.Align && !baseOptions.AlignRoi)
        {
            logger.LogDebug("Enabling ROI alignment");
            options = options with { AlignRoi = true };
        }

        // Apply min confidence override
        if (
            settings.MinConfidence >= 0.0f
            && settings.MinConfidence <= 1.0f
            && Math.Abs(settings.MinConfidence - baseOptions.MinFaceConfidence) > 0.001f
        )
        {
            logger.LogDebug(
                "Overriding minimum face confidence: {OldConfidence} → {NewConfidence}",
                baseOptions.MinFaceConfidence,
                settings.MinConfidence
            );
            options = options with { MinFaceConfidence = settings.MinConfidence };
        }

        // Apply max rotation override
        if (
            settings.MaxRotation > 0.0f
            && Math.Abs(settings.MaxRotation - baseOptions.MaxRotationDegrees) > 0.001f
        )
        {
            logger.LogDebug(
                "Overriding maximum rotation: {OldRotation}° → {NewRotation}°",
                baseOptions.MaxRotationDegrees,
                settings.MaxRotation
            );
            options = options with { MaxRotationDegrees = settings.MaxRotation };
        }

        return options;
    }
}

using System.ComponentModel;
using CSharpFunctionalExtensions;
using FaceOFFx.Cli.Services;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Standards;
using FaceOFFx.Core.Domain.Transformations;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

/// <summary>
/// CLI command for visualizing facial ROI (Region of Interest) Inner Region on PIV-processed images.
/// Processes images through the complete PIV pipeline and generates ROI visualization for JPEG 2000 encoding.
/// </summary>
/// <remarks>
/// This command processes facial images through the PIV pipeline to:
/// 1. Process image to PIV compliance (detect faces, extract landmarks, rotate, crop, resize to 420x560)
/// 2. Transform the 68-point landmarks to final PIV coordinate space using mathematical transformation
/// 3. Calculate the single Inner Region for JPEG 2000 encoding in PIV space:
///    - Inner Region: Complete facial area with optimized boundaries - highest priority (red)
/// 4. Generate visualization with colored bounding box on the final PIV image
///
/// The output shows the ROI Inner Region precisely positioned for JPEG 2000 encoding of PIV-compliant images.
/// </remarks>
internal sealed class RoiCommand(
    IFaceDetector faceDetector,
    ILandmarkExtractor landmarkExtractor,
    IJpeg2000Encoder jpeg2000Encoder,
    ILogger<RoiCommand> logger
) : AsyncCommand<RoiCommand.Settings>
{
    /// <summary>
    /// Settings for the ROI visualization command.
    /// </summary>
    internal sealed class Settings : CommandSettings
    {
        /// <summary>
        /// Path to the input image file to process.
        /// </summary>
        [Description("Path to the input image file")]
        [CommandArgument(0, "<INPUT>")]
        public required string InputPath { get; init; }

        /// <summary>
        /// Path where the visualized output image will be saved.
        /// If not specified, saves to input filename with '_roi' suffix.
        /// </summary>
        [Description("Output path for the ROI visualization")]
        [CommandOption("-o|--output")]
        public string? OutputPath { get; init; }

        /// <summary>
        /// Output image format (JPEG or PNG). Default is JPEG.
        /// </summary>
        [Description("Output image format (JPEG or PNG)")]
        [CommandOption("-f|--format")]
        [DefaultValue("JPEG")]
        public string Format { get; init; } = "JPEG";

        /// <summary>
        /// JPEG quality level (1-100). Only applies when format is JPEG. Default is 95.
        /// </summary>
        [Description("JPEG quality (1-100)")]
        [CommandOption("-q|--quality")]
        [DefaultValue(95)]
        public int Quality { get; init; } = 95;

        /// <summary>
        /// Width of the ROI bounding box strokes in pixels. Default is 3.
        /// </summary>
        [Description("ROI bounding box stroke width")]
        [CommandOption("--stroke-width")]
        [DefaultValue(3f)]
        public float StrokeWidth { get; init; } = 3f;

        /// <summary>
        /// Whether to show ROI priority labels on the visualization. Default is true.
        /// </summary>
        [Description("Show ROI priority labels")]
        [CommandOption("--show-labels")]
        [DefaultValue(true)]
        public bool ShowLabels { get; init; } = true;

        /// <summary>
        /// Whether to include landmark points in the visualization. Default is true.
        /// </summary>
        [Description("Include 68-point landmarks in visualization")]
        [CommandOption("--show-landmarks")]
        [DefaultValue(true)]
        public bool ShowLandmarks { get; init; } = true;

        /// <summary>
        /// Whether to include PIV compliance lines (AA, BB, CC) in the visualization. Default is false.
        /// </summary>
        [Description("Include PIV compliance lines (AA, BB, CC) in visualization")]
        [CommandOption("--show-piv-lines")]
        [DefaultValue(false)]
        public bool ShowPivLines { get; init; } = false;

        /// <summary>
        /// Enable verbose output with detailed processing information.
        /// </summary>
        [Description("Enable verbose output")]
        [CommandOption("-v|--verbose")]
        [DefaultValue(false)]
        public bool Verbose { get; init; } = false;

        /// <summary>
        /// Enable debug logging for troubleshooting.
        /// </summary>
        [Description("Enable debug logging for troubleshooting")]
        [CommandOption("--debug")]
        [DefaultValue(false)]
        public bool Debug { get; init; } = false;

        /// <summary>
        /// Skip drawing ROI boxes (for JPEG 2000 encoding without visualization).
        /// </summary>
        [Description("Skip drawing ROI boxes")]
        [CommandOption("--no-boxes")]
        [DefaultValue(false)]
        public bool NoBoxes { get; init; } = false;
    }

    private readonly IFaceDetector _faceDetector =
        faceDetector ?? throw new ArgumentNullException(nameof(faceDetector));
    private readonly ILandmarkExtractor _landmarkExtractor =
        landmarkExtractor ?? throw new ArgumentNullException(nameof(landmarkExtractor));
    private readonly IJpeg2000Encoder _jpeg2000Encoder =
        jpeg2000Encoder ?? throw new ArgumentNullException(nameof(jpeg2000Encoder));
    private readonly ILogger<RoiCommand> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Executes the ROI visualization command.
    /// </summary>
    /// <param name="context">The command context.</param>
    /// <param name="settings">The command settings.</param>
    /// <param name="cancellationToken">Cancellation token for the command execution.</param>
    /// <returns>Exit code: 0 for success, 1 for failure.</returns>
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        _logger.LogDebug(
            "RoiCommand.ExecuteAsync started - InputPath: {InputPath}, OutputPath: {OutputPath}, Format: {Format}, Quality: {Quality}, StrokeWidth: {StrokeWidth}, ShowLabels: {ShowLabels}, ShowLandmarks: {ShowLandmarks}, ShowPivLines: {ShowPivLines}, Verbose: {Verbose}, NoBoxes: {NoBoxes}",
            settings.InputPath,
            settings.OutputPath,
            settings.Format,
            settings.Quality,
            settings.StrokeWidth,
            settings.ShowLabels,
            settings.ShowLandmarks,
            settings.ShowPivLines,
            settings.Verbose,
            settings.NoBoxes
        );

        try
        {
            // Validate input file
            if (!File.Exists(settings.InputPath))
            {
                _logger.LogError("Input file not found: {InputPath}", settings.InputPath);
                AnsiConsole.MarkupLine(
                    $"[red]Error: Input file '{settings.InputPath}' not found[/]"
                );
                return 1;
            }

            // Determine output path
            var outputPath =
                settings.OutputPath ?? GenerateOutputPath(settings.InputPath, settings.Format);
            _logger.LogDebug("Output path determined: {OutputPath}", outputPath);

            if (settings.Verbose)
            {
                AnsiConsole.MarkupLine($"[blue]Processing:[/] {settings.InputPath}");
                AnsiConsole.MarkupLine($"[blue]Output:[/] {outputPath}");
                AnsiConsole.MarkupLine($"[blue]Format:[/] {settings.Format}");
            }

            // Load the image
            _logger.LogDebug("Loading image from: {InputPath}", settings.InputPath);
            using var sourceImage = await Image.LoadAsync<Rgba32>(settings.InputPath);
            _logger.LogInformation(
                "Image loaded successfully - Dimensions: {Width}x{Height}",
                sourceImage.Width,
                sourceImage.Height
            );

            if (settings.Verbose)
            {
                AnsiConsole.MarkupLine(
                    $"[green]✓[/] Loaded image: {sourceImage.Width}x{sourceImage.Height}"
                );
            }

            // Services are now injected via constructor

            // Process the image through PIV pipeline with progress tracking
            var result = await AnsiConsole
                .Progress()
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn()
                )
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask("[blue]Processing PIV image and ROI Inner Region[/]");
                    task.MaxValue = 100;

                    // Step 1: Process through PIV pipeline (includes face detection, landmarks, transformation, and ROI calculation)
                    task.Description = "[blue]Processing PIV pipeline...[/]";
                    task.Value = 20;

                    _logger.LogDebug("Starting PIV processing for ROI visualization");
                    var pivResult = await PivProcessor.ProcessAsync(
                        sourceImage,
                        _faceDetector,
                        _landmarkExtractor,
                        _jpeg2000Encoder,
                        PivProcessingOptions.Default,
                        false, // enableRoi
                        true, // roiAlign
                        _logger
                    );

                    if (pivResult.IsFailure)
                    {
                        _logger.LogError("PIV processing failed: {Error}", pivResult.Error);
                        return Result.Failure<Image<Rgba32>>(pivResult.Error);
                    }

                    var pivData = pivResult.Value;
                    task.Value = 70;
                    _logger.LogDebug("PIV processing completed successfully");

                    if (settings.Verbose)
                    {
                        _logger.LogDebug(
                            "PIV processing results - Dimensions: {Width}x{Height}, Face confidence: {Confidence:F2}, Transformation: {Summary}",
                            pivData.Dimensions.Width,
                            pivData.Dimensions.Height,
                            pivData.SourceFace.Confidence,
                            pivData.ProcessingSummary
                        );
                        AnsiConsole.MarkupLine(
                            $"[green]✓[/] PIV processing complete: {pivData.Dimensions.Width}x{pivData.Dimensions.Height}"
                        );
                        AnsiConsole.MarkupLine(
                            $"[green]✓[/] Face confidence: {pivData.SourceFace.Confidence:F2}"
                        );
                        AnsiConsole.MarkupLine(
                            $"[green]✓[/] Applied transformation: {pivData.ProcessingSummary}"
                        );
                    }

                    // Step 2: Extract ROI Inner Region and transformed landmarks from PIV result
                    task.Description = "[blue]Extracting ROI Inner Region...[/]";
                    task.Value = 80;

                    _logger.LogDebug("Extracting ROI Inner Region from PIV result metadata");
                    if (
                        !pivData.Metadata.TryGetValue("RoiRegions", out var roiObject)
                        || roiObject is not FacialRoiSet roiSet
                    )
                    {
                        _logger.LogError(
                            "PIV processing did not produce ROI Inner Region in metadata"
                        );
                        return Result.Failure<Image<Rgba32>>(
                            "PIV processing did not produce ROI Inner Region"
                        );
                    }

                    if (
                        !pivData.Metadata.TryGetValue("PivImage", out var imageObject)
                        || imageObject is not Image<Rgba32> pivImage
                    )
                    {
                        _logger.LogError("PIV processing did not produce image in metadata");
                        return Result.Failure<Image<Rgba32>>(
                            "PIV processing did not produce image"
                        );
                    }
                    _logger.LogDebug(
                        "Successfully extracted ROI Inner Region and PIV image from metadata"
                    );

                    if (settings.Verbose)
                    {
                        var innerRegion = roiSet.InnerRegion;
                        _logger.LogDebug(
                            "ROI region - Appendix C.6 Inner: {Width}x{Height} at ({X},{Y})",
                            innerRegion.BoundingBox.Width,
                            innerRegion.BoundingBox.Height,
                            innerRegion.BoundingBox.X,
                            innerRegion.BoundingBox.Y
                        );
                        AnsiConsole.MarkupLine(
                            $"[green]✓[/] Appendix C.6 Inner ROI region (420x560 coordinates):"
                        );
                        AnsiConsole.MarkupLine(
                            $"  - Inner Region: {innerRegion.BoundingBox.Width}x{innerRegion.BoundingBox.Height} at ({innerRegion.BoundingBox.X},{innerRegion.BoundingBox.Y})"
                        );
                    }

                    // Step 3: Generate visualization on the PIV image
                    task.Description = "[blue]Generating ROI visualization...[/]";
                    task.Value = 90;

                    Result<Image<Rgba32>> visualizationResult;

                    // Use strokeWidth of 0 if NoBoxes is set
                    var strokeWidth = settings.NoBoxes ? 0f : settings.StrokeWidth;
                    _logger.LogDebug(
                        "Generating visualization with strokeWidth: {StrokeWidth} (NoBoxes: {NoBoxes})",
                        strokeWidth,
                        settings.NoBoxes
                    );

                    // Extract PIV lines if requested
                    PivComplianceLines? pivLines = null;
                    PivComplianceValidation? complianceValidation = null;
                    if (settings.ShowPivLines)
                    {
                        if (
                            pivData.Metadata.TryGetValue("PivLines", out var pivLinesObject)
                            && pivLinesObject is PivComplianceLines lines
                        )
                        {
                            pivLines = lines;
                            _logger.LogDebug("Extracted PIV lines from metadata for visualization");
                        }

                        if (
                            pivData.Metadata.TryGetValue(
                                "ComplianceValidation",
                                out var validationObject
                            ) && validationObject is PivComplianceValidation validation
                        )
                        {
                            complianceValidation = validation;
                            _logger.LogDebug(
                                "Extracted compliance validation from metadata for PIV lines coloring"
                            );
                        }

                        if (pivLines == null)
                        {
                            _logger.LogWarning("PIV lines requested but not found in metadata");
                        }
                    }

                    // Determine what to draw based on options
                    if (
                        settings.ShowLandmarks
                        && pivData.Metadata.TryGetValue(
                            "TransformedLandmarks",
                            out var landmarkObject
                        )
                        && landmarkObject is FaceLandmarks68 transformedLandmarks
                    )
                    {
                        _logger.LogDebug(
                            "Drawing complete visualization with landmarks and optional PIV lines"
                        );

                        if (
                            settings.ShowPivLines
                            && pivLines != null
                            && complianceValidation != null
                        )
                        {
                            // Show ROI Inner Region, landmarks, and PIV lines
                            visualizationResult = DrawCompleteVisualizationWithPivLines(
                                pivImage,
                                transformedLandmarks,
                                roiSet,
                                pivLines,
                                complianceValidation,
                                strokeWidth,
                                1.5f,
                                settings.ShowLabels
                            );
                        }
                        else
                        {
                            // Show both ROI Inner Region and transformed landmarks on PIV image
                            visualizationResult = RoiVisualizationService.DrawCompleteVisualization(
                                pivImage,
                                transformedLandmarks,
                                roiSet,
                                strokeWidth,
                                1.5f,
                                settings.ShowLabels
                            );
                        }
                    }
                    else
                    {
                        _logger.LogDebug("Drawing ROI Inner Region only with optional PIV lines");

                        if (
                            settings.ShowPivLines
                            && pivLines != null
                            && complianceValidation != null
                        )
                        {
                            // Show ROI Inner Region and PIV lines
                            visualizationResult = DrawRoiRegionsWithPivLines(
                                pivImage,
                                roiSet,
                                pivLines,
                                complianceValidation,
                                strokeWidth,
                                settings.ShowLabels
                            );
                        }
                        else
                        {
                            // Show only ROI Inner Region on PIV image
                            visualizationResult = RoiVisualizationService.DrawRoiRegions(
                                pivImage,
                                roiSet,
                                strokeWidth,
                                settings.ShowLabels
                            );
                        }
                    }

                    task.Value = 100;
                    task.Description = "[green]✓ PIV ROI visualization complete[/]";

                    _logger.LogDebug(
                        "Visualization generation completed, success: {IsSuccess}",
                        visualizationResult.IsSuccess
                    );
                    return visualizationResult;
                });

            if (result.IsFailure)
            {
                _logger.LogError("ROI visualization failed: {Error}", result.Error);
                AnsiConsole.MarkupLine($"[red]Error: {result.Error}[/]");
                return 1;
            }

            // Save the output image
            using var outputImage = result.Value;

            _logger.LogDebug(
                "Saving output image as {Format} to {OutputPath}",
                settings.Format,
                outputPath
            );
            if (settings.Format.ToUpperInvariant() == "PNG")
            {
                await outputImage.SaveAsPngAsync(outputPath);
                _logger.LogInformation(
                    "Saved ROI visualization as PNG to {OutputPath}",
                    outputPath
                );
            }
            else
            {
                await outputImage.SaveAsJpegAsync(
                    outputPath,
                    new JpegEncoder { Quality = settings.Quality }
                );
                _logger.LogInformation(
                    "Saved ROI visualization as JPEG (quality: {Quality}) to {OutputPath}",
                    settings.Quality,
                    outputPath
                );
            }

            if (settings.Verbose)
            {
                var fileInfo = new FileInfo(outputPath);
                _logger.LogDebug("Output file size: {Size} bytes", fileInfo.Length);
                AnsiConsole.MarkupLine(
                    $"[green]✓[/] Saved ROI visualization: {fileInfo.Length:N0} bytes"
                );
            }

            AnsiConsole.MarkupLine(
                $"[green]Success:[/] PIV ROI visualization saved to {outputPath}"
            );
            AnsiConsole.MarkupLine(
                $"[cyan]PIV Image:[/] 420x560 pixels with transformed landmarks and ROI Inner Region"
            );
            var legendText = "[cyan]ROI Legend:[/] [green]Green[/] = Appendix C.6 Inner Region";
            if (settings.ShowPivLines)
            {
                legendText +=
                    "\n[cyan]PIV Lines:[/] [blue]AA (Vertical)[/], [green]BB (Eyes)[/], [purple]CC (Head Width)[/] - colored by compliance";
            }
            AnsiConsole.MarkupLine(legendText);

            _logger.LogDebug("RoiCommand.ExecuteAsync completed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in RoiCommand.ExecuteAsync");
            AnsiConsole.MarkupLine($"[red]Unexpected error: {ex.Message}[/]");
            return 1;
        }
        finally
        {
            _logger.LogDebug("RoiCommand.ExecuteAsync ended");
        }
    }

    /// <summary>
    /// Generates an output file path based on the input path and format.
    /// </summary>
    /// <param name="inputPath">The input file path.</param>
    /// <param name="format">The desired output format.</param>
    /// <returns>A suggested output file path with appropriate extension.</returns>
    private static string GenerateOutputPath(string inputPath, string format)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputPath);
        var extension = format.ToUpperInvariant() == "PNG" ? ".png" : ".jpg";

        return Path.Combine(directory, $"{fileNameWithoutExtension}_roi{extension}");
    }

    /// <summary>
    /// Draws ROI Inner Region with PIV compliance lines overlay.
    /// </summary>
    private static Result<Image<Rgba32>> DrawRoiRegionsWithPivLines(
        Image<Rgba32> pivImage,
        FacialRoiSet roiSet,
        PivComplianceLines pivLines,
        PivComplianceValidation complianceValidation,
        float strokeWidth,
        bool showLabels
    )
    {
        // First draw ROI Inner Region
        var roiResult = RoiVisualizationService.DrawRoiRegions(
            pivImage,
            roiSet,
            strokeWidth,
            showLabels
        );
        if (roiResult.IsFailure)
        {
            return roiResult;
        }

        // Then overlay PIV compliance lines
        var pivLinesResult = RoiVisualizationService.DrawPivComplianceLines(
            roiResult.Value,
            pivLines,
            complianceValidation,
            2f,
            false
        );

        // Dispose the intermediate image if PIV lines drawing succeeded
        if (pivLinesResult.IsSuccess)
        {
            roiResult.Value.Dispose();
        }

        return pivLinesResult;
    }

    /// <summary>
    /// Draws complete visualization with ROI Inner Region, landmarks, and PIV compliance lines.
    /// </summary>
    private static Result<Image<Rgba32>> DrawCompleteVisualizationWithPivLines(
        Image<Rgba32> pivImage,
        FaceLandmarks68 transformedLandmarks,
        FacialRoiSet roiSet,
        PivComplianceLines pivLines,
        PivComplianceValidation complianceValidation,
        float strokeWidth,
        float pointSize,
        bool showLabels
    )
    {
        // First draw complete visualization (ROI + landmarks)
        var completeResult = RoiVisualizationService.DrawCompleteVisualization(
            pivImage,
            transformedLandmarks,
            roiSet,
            strokeWidth,
            pointSize,
            showLabels
        );
        if (completeResult.IsFailure)
        {
            return completeResult;
        }

        // Then overlay PIV compliance lines
        var pivLinesResult = RoiVisualizationService.DrawPivComplianceLines(
            completeResult.Value,
            pivLines,
            complianceValidation,
            2f,
            false
        );

        // Dispose the intermediate image if PIV lines drawing succeeded
        if (pivLinesResult.IsSuccess)
        {
            completeResult.Value.Dispose();
        }

        return pivLinesResult;
    }
}

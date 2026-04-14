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
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CSharpFunctionalExtensions;
using FaceOFFx.Cli.Services;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Standards;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Core.Domain.Quality;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

/// <summary>
/// Validates and processes an entire dataset of images
/// </summary>
internal sealed class DatasetValidateCommand : AsyncCommand<DatasetValidateCommand.Settings>
{
    private readonly IFaceDetector _faceDetector;
    private readonly ILandmarkExtractor _landmarkExtractor;
    private readonly IJpeg2000Encoder _jpeg2000Encoder;
    private readonly IQualityAssessor _qualityAssessor;
    private readonly StandardPortraitProcessor _standardPortraitProcessor;
    private readonly ILogger<DatasetValidateCommand> _logger;

    public DatasetValidateCommand(
        IFaceDetector faceDetector,
        ILandmarkExtractor landmarkExtractor,
        IJpeg2000Encoder jpeg2000Encoder,
        IQualityAssessor qualityAssessor,
        StandardPortraitProcessor standardPortraitProcessor,
        ILogger<DatasetValidateCommand> logger)
    {
        _faceDetector = faceDetector;
        _landmarkExtractor = landmarkExtractor;
        _jpeg2000Encoder = jpeg2000Encoder;
        _qualityAssessor = qualityAssessor;
        _standardPortraitProcessor = standardPortraitProcessor;
        _logger = logger;
    }

    /// <summary>
    /// Settings for dataset validation
    /// </summary>
    internal sealed class Settings : CommandSettings
    {
        /// <summary>
        /// Path to the dataset directory
        /// </summary>
        [Description("Path to the dataset directory")]
        [CommandArgument(0, "<PATH>")]
        public string DatasetPath { get; set; } = string.Empty;

        /// <summary>
        /// Processing standard (PIV, TWIC, ICAO, CAC)
        /// </summary>
        [Description("Processing standard (PIV, TWIC, ICAO, CAC)")]
        [CommandOption("-s|--standard <STANDARD>")]
        [DefaultValue("PIV")]
        public string Standard { get; set; } = "PIV";

        /// <summary>
        /// Enable relaxed cropping for pre-cropped faces
        /// </summary>
        [Description("Enable relaxed cropping for pre-cropped faces (e.g., SFHQ-T2I)")]
        [CommandOption("--relaxed-crop")]
        public bool RelaxedCrop { get; set; }

        /// <summary>
        /// Filter out low-quality images before processing
        /// </summary>
        [Description("Filter out low-quality images before processing")]
        [CommandOption("--filter-quality")]
        public bool FilterQuality { get; set; }

        /// <summary>
        /// Minimum face confidence for detection
        /// </summary>
        [Description("Minimum face confidence for detection")]
        [CommandOption("--min-confidence <VALUE>")]
        [DefaultValue(0.8f)]
        public float MinConfidence { get; set; } = 0.8f;

        /// <summary>
        /// Minimum face size in pixels
        /// </summary>
        [Description("Minimum face size in pixels")]
        [CommandOption("--min-face-size <PIXELS>")]
        [DefaultValue(50)]
        public int MinFaceSize { get; set; } = 50;

        /// <summary>
        /// Output directory for processed images
        /// </summary>
        [Description("Output directory (default: processed_<dataset-name>)")]
        [CommandOption("-o|--output <PATH>")]
        public string? OutputPath { get; set; }

        /// <summary>
        /// Enable interactive review mode
        /// </summary>
        [Description("Enable interactive human-in-the-loop review")]
        [CommandOption("-i|--interactive")]
        public bool Interactive { get; set; }

        /// <summary>
        /// Batch size for interactive review
        /// </summary>
        [Description("Batch size for interactive review")]
        [CommandOption("--batch-size <COUNT>")]
        [DefaultValue(10)]
        public int BatchSize { get; set; } = 10;

        /// <summary>
        /// Maximum number of parallel processing threads
        /// </summary>
        [Description("Maximum parallel processing threads")]
        [CommandOption("--parallel <COUNT>")]
        [DefaultValue(-1)]
        public int ParallelThreads { get; set; } = -1;

        /// <summary>
        /// Generate visualization overlays
        /// </summary>
        [Description("Generate visualization overlays")]
        [CommandOption("--visualize")]
        public bool Visualize { get; set; }

        /// <summary>
        /// File pattern to match
        /// </summary>
        [Description("File pattern to match (e.g., *.jpg)")]
        [CommandOption("-p|--pattern <PATTERN>")]
        [DefaultValue("*.jpg")]
        public string Pattern { get; set; } = "*.jpg";

        /// <summary>
        /// Minimum sharpness threshold for quality assessment
        /// </summary>
        [Description("Minimum sharpness threshold (0.0-1.0)")]
        [CommandOption("--sharpness-threshold <VALUE>")]
        [DefaultValue(0.7f)]
        public float SharpnessThreshold { get; set; } = 0.7f;
    }

    /// <summary>
    /// Result of processing a single image
    /// </summary>
    private class ProcessingResult
    {
        public required string FilePath { get; init; }
        public required string FileName { get; init; }
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public string? OutputPath { get; init; }
        public string? VisualizationPath { get; init; }
        public double? QualityScore { get; init; }
        public int? FaceCount { get; init; }
        public double ProcessingTime { get; init; }
        public Dictionary<string, object>? Metadata { get; init; }
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Validate input directory
            if (!Directory.Exists(settings.DatasetPath))
            {
                AnsiConsole.MarkupLine($"[red]Error: Dataset directory not found: {settings.DatasetPath}[/]");
                return 1;
            }

            // Set up output directory
            var datasetName = Path.GetFileName(settings.DatasetPath.TrimEnd(Path.DirectorySeparatorChar));
            var outputPath = settings.OutputPath ?? $"processed_{datasetName}";
            
            // Clean up existing output directory if it exists
            if (Directory.Exists(outputPath))
            {
                AnsiConsole.MarkupLine($"[yellow]Cleaning up existing output directory: {outputPath}[/]");
                Directory.Delete(outputPath, recursive: true);
            }
            
            // Create fresh output directory structure
            Directory.CreateDirectory(Path.Combine(outputPath, "passed"));
            Directory.CreateDirectory(Path.Combine(outputPath, "failed"));
            if (settings.Visualize)
            {
                Directory.CreateDirectory(Path.Combine(outputPath, "visualizations"));
            }

            // Find all matching files
            var searchOption = SearchOption.AllDirectories;
            var files = Directory.GetFiles(settings.DatasetPath, settings.Pattern, searchOption)
                .Where(f => !f.Contains("processed_", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (files.Length == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]No files matching pattern '{settings.Pattern}' found in {settings.DatasetPath}[/]");
                return 0;
            }

            AnsiConsole.MarkupLine($"[green]Found {files.Length} files to process[/]");

            // Process files
            var results = new List<ProcessingResult>();
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = settings.ParallelThreads == -1 
                    ? Environment.ProcessorCount 
                    : settings.ParallelThreads
            };

            if (settings.Interactive)
            {
                // Interactive mode - process in batches
                results = await ProcessInteractiveAsync(files, settings, outputPath, parallelOptions);
            }
            else
            {
                // Batch mode - process all at once
                results = await ProcessBatchAsync(files, settings, outputPath, parallelOptions);
            }

            // Generate report
            await GenerateReportAsync(results, outputPath, datasetName);

            // Show summary
            ShowSummary(results);

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dataset validation failed");
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    private async Task<List<ProcessingResult>> ProcessBatchAsync(
        string[] files, 
        Settings settings, 
        string outputPath,
        ParallelOptions parallelOptions)
    {
        var results = new List<ProcessingResult>();
        var progressLock = new object();

        await AnsiConsole.Progress()
            .Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            })
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Processing dataset[/]", maxValue: files.Length);

                await Parallel.ForEachAsync(files, parallelOptions, async (file, ct) =>
                {
                    var result = await ProcessSingleFileAsync(file, settings, outputPath);
                    
                    lock (progressLock)
                    {
                        results.Add(result);
                        task.Increment(1);
                    }
                });
            });

        return results;
    }

    private async Task<List<ProcessingResult>> ProcessInteractiveAsync(
        string[] files,
        Settings settings,
        string outputPath,
        ParallelOptions parallelOptions)
    {
        var allResults = new List<ProcessingResult>();
        var batches = files.Chunk(settings.BatchSize).ToArray();

        for (int i = 0; i < batches.Length; i++)
        {
            var batch = batches[i];
            AnsiConsole.MarkupLine($"\n[cyan]Processing batch {i + 1} of {batches.Length}[/]");

            // Process batch
            var batchResults = await ProcessBatchAsync(batch, settings, outputPath, parallelOptions);
            
            // Generate batch HTML for review
            var batchHtmlPath = Path.Combine(outputPath, $"batch_{i + 1}.html");
            await GenerateBatchReviewHtml(batchResults, batchHtmlPath);

            // Open in browser for review
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(batchHtmlPath) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", batchHtmlPath);
            else if (OperatingSystem.IsLinux())
                Process.Start("xdg-open", batchHtmlPath);

            // Prompt for continuation
            if (i < batches.Length - 1)
            {
                if (!AnsiConsole.Confirm("\nContinue to next batch?"))
                {
                    AnsiConsole.MarkupLine("[yellow]Processing stopped by user[/]");
                    break;
                }
            }

            allResults.AddRange(batchResults);
        }

        return allResults;
    }

    private async Task<ProcessingResult> ProcessSingleFileAsync(string filePath, Settings settings, string outputPath)
    {
        var stopwatch = Stopwatch.StartNew();
        var fileName = Path.GetFileName(filePath);

        try
        {
            // Read image
            var imageData = await File.ReadAllBytesAsync(filePath);
            using var image = Image.Load<Rgba32>(imageData);

            // Quick quality pre-check if requested
            if (settings.FilterQuality)
            {
                var quickCheck = await PerformQuickQualityCheck(image);
                if (!quickCheck.passed)
                {
                    return new ProcessingResult
                    {
                        FilePath = filePath,
                        FileName = fileName,
                        Success = false,
                        ErrorMessage = quickCheck.reason,
                        ProcessingTime = stopwatch.Elapsed.TotalSeconds
                    };
                }
            }

            // Detect faces
            var detectionResult = await _faceDetector.DetectFacesAsync(image);
            if (detectionResult.IsFailure)
            {
                return new ProcessingResult
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Success = false,
                    ErrorMessage = $"Face detection failed: {detectionResult.Error}",
                    ProcessingTime = stopwatch.Elapsed.TotalSeconds
                };
            }

            var faces = detectionResult.Value
                .Where(f => f.Confidence >= settings.MinConfidence)
                .Where(f => f.BoundingBox.Width >= settings.MinFaceSize && f.BoundingBox.Height >= settings.MinFaceSize)
                .ToArray();

            if (faces.Length == 0)
            {
                return new ProcessingResult
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Success = false,
                    ErrorMessage = "No suitable faces found",
                    FaceCount = 0,
                    ProcessingTime = stopwatch.Elapsed.TotalSeconds
                };
            }

            // For PIV/TWIC, we need exactly one face
            if ((settings.Standard == "PIV" || settings.Standard == "TWIC") && faces.Length > 1)
            {
                return new ProcessingResult
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Success = false,
                    ErrorMessage = $"Multiple faces detected ({faces.Length}), PIV/TWIC requires single face",
                    FaceCount = faces.Length,
                    ProcessingTime = stopwatch.Elapsed.TotalSeconds
                };
            }

            var face = faces.First();
            var outputFileName = Path.GetFileNameWithoutExtension(fileName) + "_processed";
            var jp2Path = Path.Combine(outputPath, "passed", outputFileName + ".jp2");
            var pngPath = Path.Combine(outputPath, "passed", outputFileName + ".png");

            var processedArtifactResult = await ProcessForStandardAsync(
                image,
                settings);
            if (processedArtifactResult.IsFailure)
            {
                return new ProcessingResult
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Success = false,
                    ErrorMessage = processedArtifactResult.Error,
                    FaceCount = faces.Length,
                    ProcessingTime = stopwatch.Elapsed.TotalSeconds
                };
            }
            using var processedArtifact = processedArtifactResult.Value;

            var qualityAssessmentResult = await AssessProcessedImageAsync(
                processedArtifact.ProcessedImage,
                settings.Standard);
            if (qualityAssessmentResult.IsFailure)
            {
                return new ProcessingResult
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Success = false,
                    ErrorMessage = qualityAssessmentResult.Error,
                    FaceCount = faces.Length,
                    ProcessingTime = stopwatch.Elapsed.TotalSeconds
                };
            }

            var qualityAssessment = qualityAssessmentResult.Value;
            var thresholdFailure = EvaluateQualityThresholds(qualityAssessment, settings);
            if (thresholdFailure != null)
            {
                return new ProcessingResult
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Success = false,
                    ErrorMessage = thresholdFailure,
                    FaceCount = faces.Length,
                    ProcessingTime = stopwatch.Elapsed.TotalSeconds,
                    QualityScore = qualityAssessment.Overall.Value,
                    Metadata = new Dictionary<string, object>
                    {
                        ["QualityAssessment"] = qualityAssessment,
                        ["FailureReason"] = qualityAssessment.Sharpness.Overall.Value < settings.SharpnessThreshold
                            ? "LowSharpness"
                            : "LowOverallQuality",
                        ["SharpnessScore"] = qualityAssessment.Sharpness.Overall.Value,
                        ["OverallQuality"] = qualityAssessment.Overall.Value
                    }
                };
            }

            await File.WriteAllBytesAsync(jp2Path, processedArtifact.EncodedImageData);
            await processedArtifact.ProcessedImage.SaveAsPngAsync(pngPath);

            string? vizPath = null;
            if (settings.Visualize)
            {
                vizPath = GenerateVisualization(image, face, outputPath, fileName);
            }

            var metadata = new Dictionary<string, object>
            {
                ["RotationDegrees"] = processedArtifact.RotationDegrees,
                ["FileSize"] = processedArtifact.EncodedImageData.Length,
                ["QualityAssessment"] = qualityAssessment,
                ["SharpnessScore"] = qualityAssessment.Sharpness.Overall.Value,
                ["OverallQuality"] = qualityAssessment.Overall.Value,
                ["RequestedStandard"] = settings.Standard.ToUpperInvariant(),
                ["OutputWidth"] = processedArtifact.OutputDimensions.Width,
                ["OutputHeight"] = processedArtifact.OutputDimensions.Height
            };

            return new ProcessingResult
            {
                FilePath = filePath,
                FileName = fileName,
                Success = true,
                OutputPath = jp2Path,
                VisualizationPath = vizPath,
                FaceCount = faces.Length,
                ProcessingTime = stopwatch.Elapsed.TotalSeconds,
                QualityScore = qualityAssessment.Overall.Value,
                Metadata = metadata
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file: {FilePath}", filePath);
            return new ProcessingResult
            {
                FilePath = filePath,
                FileName = fileName,
                Success = false,
                ErrorMessage = ex.Message,
                ProcessingTime = stopwatch.Elapsed.TotalSeconds
            };
        }
    }

    private async Task<Result<ProcessedArtifact>> ProcessForStandardAsync(
        Image<Rgba32> image,
        Settings settings)
    {
        if (settings.Standard.Equals("ICAO", StringComparison.OrdinalIgnoreCase))
        {
            var standardResult = await _standardPortraitProcessor.ProcessAsync(
                image,
                settings.Standard,
                settings.MinConfidence,
                settings.MinFaceSize);

            return standardResult.Map(result => new ProcessedArtifact(
                result.ProcessedImage,
                result.EncodedImageData,
                result.OutputDimensions,
                result.RotationDegrees));
        }

        var processingOptions = GetProcessingOptions(settings);
        var pivResult = await PivProcessor.ProcessAsync(
            image,
            _faceDetector,
            _landmarkExtractor,
            _jpeg2000Encoder,
            processingOptions,
            enableRoi: true,
            logger: _logger);
        if (pivResult.IsFailure)
        {
            return Result.Failure<ProcessedArtifact>(pivResult.Error);
        }

        if (!pivResult.Value.Metadata.TryGetValue("PivImage", out var pivImageObject) ||
            pivImageObject is not Image<Rgba32> pivImage)
        {
            return Result.Failure<ProcessedArtifact>(
                "PIV processing did not produce a reusable processed image");
        }

        return Result.Success(new ProcessedArtifact(
            pivImage.Clone(),
            pivResult.Value.ImageData,
            pivResult.Value.Dimensions,
            pivResult.Value.AppliedTransform.RotationDegrees));
    }

    private async Task<Result<Iso19794Assessment>> AssessProcessedImageAsync(
        Image<Rgba32> processedImage,
        string standardName)
    {
        var processedDetectionResult = await _faceDetector.DetectFacesAsync(processedImage);
        if (processedDetectionResult.IsFailure || processedDetectionResult.Value.Count == 0)
        {
            return Result.Failure<Iso19794Assessment>(
                "Failed to detect face in processed image for quality assessment");
        }

        var processedFace = processedDetectionResult.Value
            .OrderByDescending(face => face.Confidence)
            .First();
        var processedLandmarksResult = await _landmarkExtractor
            .ExtractLandmarksAsync(processedImage, processedFace.BoundingBox);
        if (processedLandmarksResult.IsFailure)
        {
            return Result.Failure<Iso19794Assessment>(
                $"Failed to extract landmarks for quality assessment: {processedLandmarksResult.Error}");
        }

        var qualityOptions = QualityAssessmentOptions.ForStandard(standardName) with
        {
            EnforceCompliance = false,
            EnableParallelAssessment = true
        };

        return await _qualityAssessor.AssessAsync(
            processedImage,
            processedFace,
            processedLandmarksResult.Value,
            qualityOptions);
    }

    private static string? EvaluateQualityThresholds(
        Iso19794Assessment qualityAssessment,
        Settings settings)
    {
        if (qualityAssessment.Sharpness.Overall.Value < settings.SharpnessThreshold)
        {
            return
                $"Image too blurry: sharpness score {qualityAssessment.Sharpness.Overall.Value:F2} below threshold {settings.SharpnessThreshold:F2}";
        }

        const double minOverallQuality = 0.6;
        if (qualityAssessment.Overall.Value < minOverallQuality)
        {
            return
                $"Image quality too low: overall score {qualityAssessment.Overall.Value:F2} below threshold {minOverallQuality:F2}";
        }

        return null;
    }

    private PivProcessingOptions GetProcessingOptions(Settings settings)
    {
        var options = settings.Standard.ToUpperInvariant() switch
        {
            "PIV" => PivProcessingOptions.Default,
            "TWIC" => PivProcessingOptions.Default with { BaseRate = 0.5f }, // Lower rate for TWIC
            "ICAO" => PivProcessingOptions.HighQuality,
            _ => PivProcessingOptions.Default
        };
        
        if (settings.RelaxedCrop)
        {
            // Use adaptive options for pre-cropped faces or when relaxed cropping is requested
            // This prevents aggressive cropping that cuts off facial features
            options = PivProcessingOptions.Adaptive with
            {
                MinEyeDistance = 50, // Reduced from default
                AllowPreCropped = true,
                BaseRate = options.BaseRate, // Preserve base rate from standard
                MinFaceConfidence = settings.MinConfidence
            };
        }

        return options;
    }

    private sealed record ProcessedArtifact(
        Image<Rgba32> ProcessedImage,
        byte[] EncodedImageData,
        ImageDimensions OutputDimensions,
        float RotationDegrees) : IDisposable
    {
        public void Dispose()
        {
            ProcessedImage.Dispose();
        }
    }

    private Task<(bool passed, string reason)> PerformQuickQualityCheck(Image<Rgba32> image)
    {
        // Basic quality checks
        if (image.Width < 100 || image.Height < 100)
            return Task.FromResult((false, "Image too small"));

        // Add more quality checks as needed
        return Task.FromResult((true, string.Empty));
    }

    private string GenerateVisualization(
        Image<Rgba32> image, 
        Core.Domain.Detection.DetectedFace face,
        string outputPath,
        string fileName)
    {
        var vizFileName = Path.GetFileNameWithoutExtension(fileName) + "_viz.png";
        var vizPath = Path.Combine(outputPath, "visualizations", vizFileName);

        // Clone image for visualization
        using var visualImage = image.Clone();
        
        // Draw face bounding box
        var box = face.BoundingBox;
        visualImage.Mutate(ctx =>
        {
            var rect = new SixLabors.ImageSharp.Rectangle(
                (int)box.X, 
                (int)box.Y, 
                (int)box.Width, 
                (int)box.Height);
            
            ctx.Draw(SixLabors.ImageSharp.Color.Green, 3f, rect);
        });

        // Draw confidence text
        var font = SixLabors.Fonts.SystemFonts.CreateFont("Arial", 16);
        visualImage.Mutate(ctx =>
        {
            var text = $"Confidence: {face.Confidence:F2}";
            ctx.DrawText(text, font, SixLabors.ImageSharp.Color.Green, new SixLabors.ImageSharp.PointF(box.X, box.Y - 20));
        });

        visualImage.SaveAsPng(vizPath);
        return vizPath;
    }

    private async Task GenerateReportAsync(
        List<ProcessingResult> results,
        string outputPath,
        string datasetName)
    {
        // Generate JSON results
        var jsonPath = Path.Combine(outputPath, "results.json");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(results, jsonOptions));

        // Generate CSV results
        var csvPath = Path.Combine(outputPath, "results.csv");
        var csvBuilder = new StringBuilder();
        csvBuilder.AppendLine("FileName,Success,ErrorMessage,FaceCount,ProcessingTime,OutputPath");
        
        foreach (var result in results)
        {
            csvBuilder.AppendLine($"{result.FileName},{result.Success},\"{result.ErrorMessage ?? ""}\",{result.FaceCount ?? 0},{result.ProcessingTime:F3},{result.OutputPath ?? ""}");
        }
        
        await File.WriteAllTextAsync(csvPath, csvBuilder.ToString());

        // Generate HTML report
        var htmlPath = Path.Combine(outputPath, "report.html");
        await GenerateHtmlReport(results, htmlPath, datasetName, outputPath);
    }

    private async Task GenerateHtmlReport(
        List<ProcessingResult> results,
        string htmlPath,
        string datasetName,
        string outputPath)
    {
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count - successCount;
        var avgProcessingTime = results.Average(r => r.ProcessingTime);

        var html = $@"<!DOCTYPE html>
<html>
<head>
    <title>FaceOFFx Dataset Validation Report - {datasetName}</title>
    <link href=""https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css"" rel=""stylesheet"">
    <script src=""https://cdn.jsdelivr.net/npm/chart.js""></script>
    <style>
        .metric-card {{ 
            text-align: center; 
            padding: 20px; 
            margin: 10px;
            border-radius: 10px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
        }}
        .success-card {{ background-color: #d4edda; }}
        .failure-card {{ background-color: #f8d7da; }}
        .info-card {{ background-color: #d1ecf1; }}
    </style>
</head>
<body>
    <div class=""container mt-4"">
        <h1>Dataset Validation Report: {datasetName}</h1>
        <p class=""text-muted"">Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>
        
        <div class=""row mt-4"">
            <div class=""col-md-3"">
                <div class=""metric-card info-card"">
                    <h3>{results.Count}</h3>
                    <p>Total Images</p>
                </div>
            </div>
            <div class=""col-md-3"">
                <div class=""metric-card success-card"">
                    <h3>{successCount}</h3>
                    <p>Passed ({(double)successCount / results.Count:P1})</p>
                </div>
            </div>
            <div class=""col-md-3"">
                <div class=""metric-card failure-card"">
                    <h3>{failureCount}</h3>
                    <p>Failed ({(double)failureCount / results.Count:P1})</p>
                </div>
            </div>
            <div class=""col-md-3"">
                <div class=""metric-card info-card"">
                    <h3>{avgProcessingTime:F2}s</h3>
                    <p>Avg Processing Time</p>
                </div>
            </div>
        </div>

        <div class=""row mt-5"">
            <div class=""col-md-6"">
                <canvas id=""successChart""></canvas>
            </div>
            <div class=""col-md-6"">
                <canvas id=""failureChart""></canvas>
            </div>
        </div>

        <h2 class=""mt-5"">Failure Analysis</h2>
        <table class=""table table-striped mt-3"">
            <thead>
                <tr>
                    <th>Error Type</th>
                    <th>Count</th>
                    <th>Percentage</th>
                </tr>
            </thead>
            <tbody>
                {GenerateFailureAnalysisRows(results)}
            </tbody>
        </table>

        <h2 class=""mt-5"">Successful Processed Images</h2>
        <div class=""row"">
            {GenerateSuccessfulImageGallery(results, outputPath)}
        </div>

        <h2 class=""mt-5"">Detailed Results</h2>
        <div class=""table-responsive"">
            <table class=""table table-sm table-hover mt-3"">
                <thead>
                    <tr>
                        <th>File</th>
                        <th>Status</th>
                        <th>Faces</th>
                        <th>Quality</th>
                        <th>Time (s)</th>
                        <th>Error</th>
                        <th>Output</th>
                    </tr>
                </thead>
                <tbody>
                    {GenerateDetailedResultRows(results)}
                </tbody>
            </table>
        </div>
    </div>

    <script>
        // Success/Failure Pie Chart
        new Chart(document.getElementById('successChart'), {{
            type: 'pie',
            data: {{
                labels: ['Passed', 'Failed'],
                datasets: [{{
                    data: [{successCount}, {failureCount}],
                    backgroundColor: ['#28a745', '#dc3545']
                }}]
            }},
            options: {{
                title: {{
                    display: true,
                    text: 'Processing Results'
                }}
            }}
        }});

        // Failure Reasons Chart
        {GenerateFailureChartScript(results)}
    </script>
</body>
</html>";

        await File.WriteAllTextAsync(htmlPath, html);
    }

    private string GenerateSuccessfulImageGallery(List<ProcessingResult> results, string outputPath)
    {
        var gallery = new StringBuilder();
        var successfulResults = results.Where(r => r.Success && r.OutputPath != null).Take(20); // Show first 20

        foreach (var result in successfulResults)
        {
            var jp2Path = Path.GetRelativePath(outputPath, result.OutputPath!);
            var pngPath = Path.ChangeExtension(jp2Path, ".png");
            var vizPath = result.VisualizationPath != null 
                ? Path.GetRelativePath(outputPath, result.VisualizationPath) 
                : null;
            
            gallery.AppendLine($@"
                <div class='col-md-3 mb-4'>
                    <div class='card'>
                        <img src='{pngPath}' class='card-img-top' alt='{result.FileName}' style='max-height: 200px; object-fit: contain;'>
                        <div class='card-body'>
                            <h6 class='card-title text-truncate' title='{result.FileName}'>{result.FileName}</h6>
                            <p class='card-text'>
                                <small>Faces: {result.FaceCount ?? 0}<br/>
                                Quality: {result.QualityScore:F2}<br/>
                                Time: {result.ProcessingTime:F2}s</small>
                            </p>
                            <div class='btn-group btn-group-sm' role='group'>
                                <a href='{pngPath}' class='btn btn-primary' target='_blank'>PNG</a>
                                <a href='{jp2Path}' class='btn btn-secondary' target='_blank'>JP2</a>
                                {(vizPath != null ? $"<a href='{vizPath}' class='btn btn-info' target='_blank'>Viz</a>" : "")}
                            </div>
                        </div>
                    </div>
                </div>");
        }

        if (results.Count(r => r.Success) > 20)
        {
            gallery.AppendLine($@"
                <div class='col-12'>
                    <p class='text-center text-muted'>... and {results.Count(r => r.Success) - 20} more successful images</p>
                </div>");
        }

        return gallery.ToString();
    }

    private string GenerateFailureAnalysisRows(List<ProcessingResult> results)
    {
        var failures = results.Where(r => !r.Success && r.ErrorMessage != null)
            .GroupBy(r => GetErrorCategory(r.ErrorMessage!))
            .OrderByDescending(g => g.Count());

        var rows = new StringBuilder();
        var totalFailures = results.Count(r => !r.Success);

        foreach (var group in failures)
        {
            var percentage = totalFailures > 0 ? (double)group.Count() / totalFailures * 100 : 0;
            rows.AppendLine($@"
                <tr>
                    <td>{group.Key}</td>
                    <td>{group.Count()}</td>
                    <td>{percentage:F1}%</td>
                </tr>");
        }

        return rows.ToString();
    }

    private string GetErrorCategory(string errorMessage)
    {
        if (errorMessage.Contains("No suitable faces", StringComparison.OrdinalIgnoreCase))
            return "No Face Detected";
        if (errorMessage.Contains("Multiple faces", StringComparison.OrdinalIgnoreCase))
            return "Multiple Faces";
        if (errorMessage.Contains("too small", StringComparison.OrdinalIgnoreCase))
            return "Image Too Small";
        if (errorMessage.Contains("Face detection failed", StringComparison.OrdinalIgnoreCase))
            return "Detection Failed";
        if (errorMessage.Contains("too blurry", StringComparison.OrdinalIgnoreCase) || 
            errorMessage.Contains("sharpness", StringComparison.OrdinalIgnoreCase))
            return "Too Blurry";
        if (errorMessage.Contains("quality too low", StringComparison.OrdinalIgnoreCase))
            return "Low Quality";
        if (errorMessage.Contains("Quality assessment failed", StringComparison.OrdinalIgnoreCase))
            return "Quality Assessment Failed";
        return "Other";
    }

    private string GenerateDetailedResultRows(List<ProcessingResult> results)
    {
        var rows = new StringBuilder();
        
        foreach (var result in results.Take(100)) // Limit to first 100 for performance
        {
            var statusBadge = result.Success 
                ? "<span class='badge bg-success'>Passed</span>" 
                : "<span class='badge bg-danger'>Failed</span>";
            
            var outputLink = result.Success && result.OutputPath != null
                ? $"<a href='{Path.GetFileName(result.OutputPath)}' target='_blank'>View</a>"
                : "-";
            
            var qualityDisplay = result.QualityScore.HasValue 
                ? $"{result.QualityScore.Value:F2}" 
                : "-";
            
            rows.AppendLine($@"
                <tr>
                    <td>{result.FileName}</td>
                    <td>{statusBadge}</td>
                    <td>{result.FaceCount ?? 0}</td>
                    <td>{qualityDisplay}</td>
                    <td>{result.ProcessingTime:F3}</td>
                    <td>{result.ErrorMessage ?? "-"}</td>
                    <td>{outputLink}</td>
                </tr>");
        }

        if (results.Count > 100)
        {
            rows.AppendLine($@"
                <tr>
                    <td colspan='5' class='text-center text-muted'>
                        ... and {results.Count - 100} more results
                    </td>
                </tr>");
        }

        return rows.ToString();
    }

    private string GenerateFailureChartScript(List<ProcessingResult> results)
    {
        var failureGroups = results.Where(r => !r.Success && r.ErrorMessage != null)
            .GroupBy(r => GetErrorCategory(r.ErrorMessage!))
            .OrderByDescending(g => g.Count())
            .ToList();

        if (!failureGroups.Any())
            return "// No failures to chart";

        var labels = string.Join(", ", failureGroups.Select(g => $"'{g.Key}'"));
        var data = string.Join(", ", failureGroups.Select(g => g.Count()));

        return $@"
        new Chart(document.getElementById('failureChart'), {{
            type: 'bar',
            data: {{
                labels: [{labels}],
                datasets: [{{
                    label: 'Failure Count',
                    data: [{data}],
                    backgroundColor: '#dc3545'
                }}]
            }},
            options: {{
                title: {{
                    display: true,
                    text: 'Failure Reasons'
                }},
                scales: {{
                    y: {{
                        beginAtZero: true
                    }}
                }}
            }}
        }});";
    }

    private async Task GenerateBatchReviewHtml(List<ProcessingResult> results, string htmlPath)
    {
        // Similar to GenerateHtmlReport but focused on batch review
        var html = $@"<!DOCTYPE html>
<html>
<head>
    <title>FaceOFFx Batch Review</title>
    <link href=""https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css"" rel=""stylesheet"">
    <style>
        .image-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
            gap: 20px;
            padding: 20px;
        }}
        .image-card {{
            border: 1px solid #ddd;
            border-radius: 8px;
            padding: 10px;
            text-align: center;
        }}
        .success {{ border-color: #28a745; }}
        .failure {{ border-color: #dc3545; }}
    </style>
</head>
<body>
    <div class=""container-fluid mt-4"">
        <h1>Batch Review</h1>
        <p>Review the processing results below. Close this window when done.</p>
        
        <div class=""image-grid"">
            {GenerateBatchImageCards(results)}
        </div>
    </div>
</body>
</html>";

        await File.WriteAllTextAsync(htmlPath, html);
    }

    private string GenerateBatchImageCards(List<ProcessingResult> results)
    {
        var cards = new StringBuilder();
        
        foreach (var result in results)
        {
            var cardClass = result.Success ? "success" : "failure";
            var statusText = result.Success ? "Passed" : "Failed";
            
            cards.AppendLine($@"
                <div class=""image-card {cardClass}"">
                    <h6>{result.FileName}</h6>
                    <p class=""mb-1""><strong>{statusText}</strong></p>
                    {(result.ErrorMessage != null ? $"<p class='small text-danger'>{result.ErrorMessage}</p>" : "")}
                    <p class=""small text-muted"">Faces: {result.FaceCount ?? 0}</p>
                </div>");
        }

        return cards.ToString();
    }

    private void ShowSummary(List<ProcessingResult> results)
    {
        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count - successCount;

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[yellow]Processing Summary[/]"));
        
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Metric")
            .AddColumn("Value");

        table.AddRow("Total Images", results.Count.ToString());
        table.AddRow("[green]Passed[/]", $"{successCount} ({(double)successCount / results.Count:P1})");
        table.AddRow("[red]Failed[/]", $"{failureCount} ({(double)failureCount / results.Count:P1})");
        table.AddRow("Avg Processing Time", $"{results.Average(r => r.ProcessingTime):F2}s");
        table.AddRow("Total Processing Time", $"{results.Sum(r => r.ProcessingTime):F1}s");

        AnsiConsole.Write(table);

        if (failureCount > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[red]Failure Analysis[/]"));

            var failureTable = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Error Type")
                .AddColumn("Count");

            var failureGroups = results.Where(r => !r.Success && r.ErrorMessage != null)
                .GroupBy(r => GetErrorCategory(r.ErrorMessage!))
                .OrderByDescending(g => g.Count());

            foreach (var group in failureGroups)
            {
                failureTable.AddRow(group.Key, group.Count().ToString());
            }

            AnsiConsole.Write(failureTable);
        }
    }
}

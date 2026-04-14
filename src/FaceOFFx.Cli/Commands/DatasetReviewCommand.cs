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
using FaceOFFx.Cli.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

/// <summary>
/// Reviews failed images from dataset validation and allows manual override
/// </summary>
internal sealed class DatasetReviewCommand : AsyncCommand<DatasetReviewCommand.Settings>
{
    /// <summary>
    /// Settings for dataset review
    /// </summary>
    internal sealed class Settings : CommandSettings
    {
        /// <summary>
        /// Path to the validation results JSON file
        /// </summary>
        [Description("Path to validation results.json file")]
        [CommandArgument(0, "<RESULTS-FILE>")]
        public string ResultsFile { get; set; } = string.Empty;

        /// <summary>
        /// Show only failed images
        /// </summary>
        [Description("Show only failed images")]
        [CommandOption("-f|--failed-only")]
        public bool FailedOnly { get; set; } = true;

        /// <summary>
        /// Minimum sharpness threshold for filtering
        /// </summary>
        [Description("Filter by minimum sharpness score")]
        [CommandOption("--min-sharpness <VALUE>")]
        public float? MinSharpness { get; set; }

        /// <summary>
        /// Maximum sharpness threshold for filtering
        /// </summary>
        [Description("Filter by maximum sharpness score")]
        [CommandOption("--max-sharpness <VALUE>")]
        public float? MaxSharpness { get; set; }

        /// <summary>
        /// Filter by failure reason
        /// </summary>
        [Description("Filter by failure reason (LowSharpness, LowOverallQuality, etc.)")]
        [CommandOption("-r|--reason <REASON>")]
        public string? FailureReason { get; set; }

        /// <summary>
        /// Generate override list
        /// </summary>
        [Description("Generate override list for selected images")]
        [CommandOption("-o|--output <FILE>")]
        public string? OutputFile { get; set; }
    }

    private class ValidationResult
    {
        public required string FilePath { get; init; }
        public required string FileName { get; init; }
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public double? QualityScore { get; init; }
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
            // Load results
            if (!File.Exists(settings.ResultsFile))
            {
                AnsiConsole.MarkupLine($"[red]Error: Results file not found: {settings.ResultsFile}[/]");
                return 1;
            }

            var jsonContent = await File.ReadAllTextAsync(settings.ResultsFile);
            var results = JsonSerializer.Deserialize<List<ValidationResult>>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (results == null || results.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No results found in file[/]");
                return 0;
            }

            // Filter results
            var filteredResults = FilterResults(results, settings);

            if (filteredResults.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No results match the filter criteria[/]");
                return 0;
            }
            
            // Sort filtered results by filename
            filteredResults = filteredResults.OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase).ToList();

            // Generate review HTML
            var outputDir = Path.GetDirectoryName(settings.ResultsFile) ?? ".";
            var reviewHtmlPath = Path.Combine(outputDir, "review.html");
            await GenerateReviewHtml(filteredResults, reviewHtmlPath, outputDir);

            // Open in browser
            OpenInBrowser(reviewHtmlPath);

            AnsiConsole.MarkupLine($"[green]Review page generated: {reviewHtmlPath}[/]");
            AnsiConsole.MarkupLine($"[cyan]Found {filteredResults.Count} images matching criteria[/]");

            // If output file requested, prompt for override selection
            if (settings.OutputFile != null)
            {
                await GenerateOverrideList(filteredResults, settings.OutputFile);
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex);
            return 1;
        }
    }

    private List<ValidationResult> FilterResults(List<ValidationResult> results, Settings settings)
    {
        var filtered = results.AsEnumerable();

        if (settings.FailedOnly)
        {
            filtered = filtered.Where(r => !r.Success);
        }

        if (settings.FailureReason != null)
        {
            filtered = filtered.Where(r => 
                r.Metadata?.ContainsKey("FailureReason") == true &&
                r.Metadata["FailureReason"].ToString()?.Equals(settings.FailureReason, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (settings.MinSharpness.HasValue || settings.MaxSharpness.HasValue)
        {
            filtered = filtered.Where(r =>
            {
                if (r.Metadata?.ContainsKey("SharpnessScore") == true)
                {
                    var sharpness = DatasetResultValueParser.TryGetDouble(r.Metadata["SharpnessScore"]);
                    if (sharpness is null)
                    {
                        return false;
                    }
                    
                    if (settings.MinSharpness.HasValue && sharpness.Value < settings.MinSharpness.Value)
                        return false;
                    
                    if (settings.MaxSharpness.HasValue && sharpness.Value > settings.MaxSharpness.Value)
                        return false;
                    
                    return true;
                }
                return false;
            });
        }

        return filtered.ToList();
    }

    private async Task GenerateReviewHtml(List<ValidationResult> results, string htmlPath, string outputDir)
    {
        // Determine the dataset directory from the results file path
        // Assuming structure: dataset/results.json or processed_dataset/results.json
        var datasetDir = "";
        var resultsDir = Path.GetDirectoryName(outputDir) ?? "";
        
        // Try to find the original dataset directory
        if (resultsDir.StartsWith("processed_"))
        {
            datasetDir = resultsDir.Replace("processed_", "");
            if (!Directory.Exists(datasetDir))
            {
                // Try parent directory
                var parentDir = Path.GetDirectoryName(resultsDir);
                if (parentDir != null)
                {
                    datasetDir = Path.Combine(parentDir, resultsDir.Replace("processed_", ""));
                }
            }
        }
        
        var html = $@"<!DOCTYPE html>
<html>
<head>
    <title>FaceOFFx Dataset Review</title>
    <link href=""https://cdn.jsdelivr.net/npm/bootstrap@5.3.0/dist/css/bootstrap.min.css"" rel=""stylesheet"">
    <style>
        body {{ background-color: #f8f9fa; }}
        .review-container {{ max-width: 1400px; margin: 0 auto; padding: 20px; }}
        .filter-card {{ 
            background: white; 
            border-radius: 10px; 
            padding: 20px; 
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
            margin-bottom: 20px;
        }}
        .image-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(350px, 1fr));
            gap: 20px;
        }}
        .review-card {{
            background: white;
            border: 2px solid #dee2e6;
            border-radius: 10px;
            padding: 15px;
            transition: all 0.2s;
        }}
        .review-card:hover {{
            transform: translateY(-2px);
            box-shadow: 0 4px 8px rgba(0,0,0,0.15);
        }}
        .review-card.selected {{
            border-color: #0d6efd;
            background-color: #e7f1ff;
        }}
        .quality-meter {{
            height: 20px;
            background: #e9ecef;
            border-radius: 10px;
            overflow: hidden;
            margin: 5px 0;
        }}
        .quality-fill {{
            height: 100%;
            transition: width 0.3s;
        }}
        .quality-high {{ background: #28a745; }}
        .quality-medium {{ background: #ffc107; }}
        .quality-low {{ background: #dc3545; }}
        .metric-badge {{
            display: inline-block;
            padding: 4px 8px;
            border-radius: 4px;
            font-size: 0.875rem;
            margin: 2px;
        }}
        .override-btn {{
            width: 100%;
            margin-top: 10px;
        }}
    </style>
</head>
<body>
    <div class=""review-container"">
        <h1>FaceOFFx Dataset Review</h1>
        <p class=""text-muted"">Review failed images and their quality metrics. Click cards to select for override.</p>
        
        <div class=""filter-card"">
            <h5>Filter Options</h5>
            <div class=""row"">
                <div class=""col-md-3"">
                    <label>Sharpness Range</label>
                    <input type=""range"" class=""form-range"" id=""sharpnessFilter"" min=""0"" max=""100"" value=""0"">
                    <span id=""sharpnessValue"">0%</span>
                </div>
                <div class=""col-md-3"">
                    <label>Failure Reason</label>
                    <select class=""form-select"" id=""reasonFilter"">
                        <option value="""">All</option>
                        <option value=""LowSharpness"">Low Sharpness</option>
                        <option value=""LowOverallQuality"">Low Overall Quality</option>
                        <option value=""Other"">Other</option>
                    </select>
                </div>
                <div class=""col-md-3"">
                    <label>Selected</label>
                    <p class=""mb-0""><strong id=""selectedCount"">0</strong> images</p>
                </div>
                <div class=""col-md-3"">
                    <button class=""btn btn-primary"" onclick=""exportSelected()"">Export Selected</button>
                    <button class=""btn btn-secondary"" onclick=""clearSelection()"">Clear</button>
                </div>
            </div>
        </div>

        <div class=""image-grid"" id=""imageGrid"">
            {GenerateReviewCards(results, outputDir)}
        </div>
    </div>

    <script>
        let selectedImages = new Set();

        function toggleSelection(fileName) {{
            const card = document.querySelector(`[data-filename='${{fileName}}']`);
            if (selectedImages.has(fileName)) {{
                selectedImages.delete(fileName);
                card.classList.remove('selected');
            }} else {{
                selectedImages.add(fileName);
                card.classList.add('selected');
            }}
            updateSelectedCount();
        }}

        function updateSelectedCount() {{
            document.getElementById('selectedCount').textContent = selectedImages.size;
        }}

        function clearSelection() {{
            selectedImages.clear();
            document.querySelectorAll('.review-card').forEach(card => {{
                card.classList.remove('selected');
            }});
            updateSelectedCount();
        }}

        function exportSelected() {{
            if (selectedImages.size === 0) {{
                alert('No images selected');
                return;
            }}
            
            const data = Array.from(selectedImages).join('\\n');
            const blob = new Blob([data], {{ type: 'text/plain' }});
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = 'override_list.txt';
            a.click();
        }}

        // Filter functionality
        document.getElementById('sharpnessFilter').addEventListener('input', function(e) {{
            document.getElementById('sharpnessValue').textContent = e.target.value + '%';
            applyFilters();
        }});

        document.getElementById('reasonFilter').addEventListener('change', applyFilters);

        function applyFilters() {{
            const sharpnessThreshold = parseInt(document.getElementById('sharpnessFilter').value) / 100;
            const reasonFilter = document.getElementById('reasonFilter').value;
            
            document.querySelectorAll('.review-card').forEach(card => {{
                const sharpness = parseFloat(card.dataset.sharpness || '0');
                const reason = card.dataset.reason || '';
                
                let show = true;
                
                if (sharpness < sharpnessThreshold) {{
                    show = false;
                }}
                
                if (reasonFilter && reason !== reasonFilter) {{
                    show = false;
                }}
                
                card.style.display = show ? 'block' : 'none';
            }});
        }}
    </script>
</body>
</html>";

        await File.WriteAllTextAsync(htmlPath, html);
    }

    private string GenerateReviewCards(List<ValidationResult> results, string outputDir)
    {
        var cards = new StringBuilder();
        
        // Sort results by filename using natural string comparison
        var sortedResults = results.OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        
        foreach (var result in sortedResults)
        {
            var sharpness = 0.0;
            var overallQuality = result.QualityScore ?? 0.0;
            var failureReason = "Other";
            
            if (result.Metadata != null)
            {
                // Try to get sharpness from QualityAssessment structure
                if (result.Metadata.TryGetValue("QualityAssessment", out var qaObj))
                {
                    try
                    {
                        var qa = DatasetResultValueParser.TryGetJsonElement(qaObj);
                        if (qa.HasValue && qa.Value.TryGetProperty("Sharpness", out var sharpnessObj))
                        {
                            if (sharpnessObj.TryGetProperty("Overall", out var overall))
                            {
                                if (overall.TryGetProperty("Value", out var value))
                                {
                                    sharpness = value.GetDouble();
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Fall back to SharpnessScore if available
                        if (result.Metadata.TryGetValue("SharpnessScore", out var s))
                            sharpness = DatasetResultValueParser.TryGetDouble(s) ?? 0.0;
                    }
                }
                
                if (result.Metadata.TryGetValue("FailureReason", out var r))
                    failureReason = DatasetResultValueParser.TryGetString(r) ?? "Other";
            }
            
            var qualityClass = overallQuality >= 0.8 ? "quality-high" : 
                              overallQuality >= 0.6 ? "quality-medium" : "quality-low";
            
            // Try to show image preview
            var imagePreview = "";
            
            // Look for the original image
            var possiblePaths = new List<string>();
            
            // If we have the full file path from the result
            if (!string.IsNullOrEmpty(result.FilePath) && File.Exists(result.FilePath))
            {
                var relativePath = GetRelativePathForHtml(result.FilePath, outputDir);
                imagePreview = $@"<a href=""{relativePath}"" target=""_blank"">
                                    <img src=""{relativePath}"" alt=""{result.FileName}"" 
                                         style=""max-width: 100%; height: 200px; object-fit: contain; 
                                         border-radius: 5px; margin-bottom: 10px; background-color: #f0f0f0; cursor: pointer;"" />
                                 </a>";
            }
            else
            {
                // Try to find the image in common locations
                var searchPaths = new[]
                {
                    Path.Combine(outputDir, "..", result.FileName),
                    Path.Combine(outputDir, "..", "tests", "sample_images", result.FileName),
                    Path.Combine(outputDir, result.FileName)
                };
                
                foreach (var path in searchPaths)
                {
                    if (File.Exists(path))
                    {
                        var relativePath = GetRelativePathForHtml(path, outputDir);
                        imagePreview = $@"<a href=""{relativePath}"" target=""_blank"">
                                            <img src=""{relativePath}"" alt=""{result.FileName}"" 
                                                 style=""max-width: 100%; height: 200px; object-fit: contain; 
                                                 border-radius: 5px; margin-bottom: 10px; background-color: #f0f0f0; cursor: pointer;"" />
                                         </a>";
                        break;
                    }
                }
            }
            
            if (string.IsNullOrEmpty(imagePreview))
            {
                imagePreview = $@"<div style=""height: 200px; display: flex; align-items: center; 
                                 justify-content: center; background-color: #f0f0f0; border-radius: 5px; 
                                 margin-bottom: 10px;""><span class=""text-muted"">Image not found</span></div>";
            }
            
            cards.AppendLine($@"
                <div class=""review-card"" data-filename=""{result.FileName}"" 
                     data-sharpness=""{sharpness}"" data-reason=""{failureReason}""
                     onclick=""toggleSelection('{result.FileName}')"">
                    {imagePreview}
                    <h6 class=""text-truncate"" title=""{result.FileName}"">{result.FileName}</h6>
                    
                    <div class=""mb-2"">
                        <small class=""text-muted"">Sharpness</small>
                        <div class=""quality-meter"">
                            <div class=""quality-fill {qualityClass}"" style=""width: {sharpness * 100}%""></div>
                        </div>
                        <small>{sharpness:P0}</small>
                    </div>
                    
                    <div class=""mb-2"">
                        <small class=""text-muted"">Overall Quality</small>
                        <div class=""quality-meter"">
                            <div class=""quality-fill {qualityClass}"" style=""width: {overallQuality * 100}%""></div>
                        </div>
                        <small>{overallQuality:P0}</small>
                    </div>
                    
                    <div class=""mb-2"">
                        <span class=""metric-badge bg-danger text-white"">{failureReason}</span>
                    </div>
                    
                    <p class=""small text-danger mb-0"">{result.ErrorMessage}</p>
                </div>");
        }
        
        return cards.ToString();
    }

    private void OpenInBrowser(string htmlPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", htmlPath);
            else if (OperatingSystem.IsLinux())
                Process.Start("xdg-open", htmlPath);
        }
        catch
        {
            AnsiConsole.MarkupLine($"[yellow]Could not open browser automatically. Please open: {htmlPath}[/]");
        }
    }

    private async Task GenerateOverrideList(List<ValidationResult> results, string outputFile)
    {
        AnsiConsole.MarkupLine("\n[cyan]Select images to override:[/]");
        
        var choices = results.Select(r => new
        {
            Display = $"{r.FileName} - Sharpness: {GetSharpnessScore(r):P0}, Quality: {r.QualityScore:P0}",
            FileName = r.FileName
        }).ToList();
        
        var selected = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select images to include in override list")
                .PageSize(10)
                .MoreChoicesText("[grey](Move up and down to reveal more images)[/]")
                .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]")
                .AddChoices(choices.Select(c => c.Display)));
        
        if (selected.Count > 0)
        {
            var fileNames = selected
                .Select(s => choices.First(c => c.Display == s).FileName)
                .ToList();
            
            await File.WriteAllLinesAsync(outputFile, fileNames);
            AnsiConsole.MarkupLine($"[green]Override list saved to: {outputFile}[/]");
            AnsiConsole.MarkupLine($"[cyan]{fileNames.Count} images selected for override[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]No images selected[/]");
        }
    }

    private double GetSharpnessScore(ValidationResult result)
    {
        if (result.Metadata?.TryGetValue("SharpnessScore", out var score) == true)
        {
            return DatasetResultValueParser.TryGetDouble(score) ?? 0.0;
        }
        return 0.0;
    }
    
    private string GetRelativePathForHtml(string absolutePath, string htmlDir)
    {
        try
        {
            // Get the relative path from the HTML directory to the image
            var htmlDirFullPath = Path.GetFullPath(htmlDir);
            var imageFullPath = Path.GetFullPath(absolutePath);
            
            // Calculate relative path
            var relativePath = Path.GetRelativePath(htmlDirFullPath, imageFullPath);
            
            // Convert to forward slashes for HTML
            return relativePath.Replace('\\', '/');
        }
        catch
        {
            // If we can't calculate relative path, return the absolute path
            return absolutePath.Replace('\\', '/');
        }
    }
}

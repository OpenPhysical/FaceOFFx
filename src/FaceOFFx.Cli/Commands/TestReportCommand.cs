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
using System.Text;
using System.Text.Json;
using Spectre.Console;
using Spectre.Console.Cli;

namespace FaceOFFx.Cli.Commands;

/// <summary>
/// Command to generate HTML reports from test results
/// </summary>
[Description("Generate HTML reports from test results")]
internal sealed class TestReportCommand : AsyncCommand<TestReportCommand.Settings>
{
    private const string DefaultTestDirectory = ".faceoffx-test";
    
    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    internal sealed class Settings : CommandSettings
    {
        [CommandOption("-i|--input <FILE>")]
        [Description("Input JSON results file")]
        public string? InputFile { get; set; }

        [CommandOption("-t|--type <TYPE>")]
        [Description("Report type (sharpness-calibration)")]
        [DefaultValue("sharpness-calibration")]
        public string ReportType { get; set; } = "sharpness-calibration";

        [CommandOption("-o|--output <DIR>")]
        [Description("Output directory for reports")]
        public string OutputDirectory { get; set; } = Path.Combine(DefaultTestDirectory, "reports");
        
        [CommandOption("--images-dir <DIR>")]
        [Description("Directory containing test images")]
        public string? ImagesDirectory { get; set; }
        
        [CommandOption("--open")]
        [Description("Open report in browser after generation")]
        [DefaultValue(true)]
        public bool OpenInBrowser { get; set; } = true;
    }

    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken
    )
    {
        // Find input file
        var inputFile = settings.InputFile;
        if (string.IsNullOrWhiteSpace(inputFile))
        {
            // Try to find the most recent results file
            var resultsDir = Path.Combine(DefaultTestDirectory, "test-results");
            if (Directory.Exists(resultsDir))
            {
                inputFile = Directory.GetFiles(resultsDir, "*.json")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .FirstOrDefault();
            }
        }

        if (string.IsNullOrWhiteSpace(inputFile) || !File.Exists(inputFile))
        {
            AnsiConsole.MarkupLine("[red]No input file found. Run 'faceoffx test sharpness' first.[/]");
            return 1;
        }

        AnsiConsole.MarkupLine($"[blue]Input file:[/] {inputFile}");

        // Load results
        var json = await File.ReadAllTextAsync(inputFile);
        var results = JsonSerializer.Deserialize<List<SharpnessTestResult>>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        if (results == null || !results.Any())
        {
            AnsiConsole.MarkupLine("[red]No results found in input file[/]");
            return 1;
        }

        // Generate report based on type
        string reportPath;
        switch (settings.ReportType.ToLowerInvariant())
        {
            case "sharpness-calibration":
            case "sharpness":
                reportPath = await GenerateSharpnessCalibrationReport(results, settings);
                break;
            default:
                AnsiConsole.MarkupLine($"[red]Unknown report type:[/] {settings.ReportType}");
                return 1;
        }

        AnsiConsole.MarkupLine($"[green]✓ Report generated:[/] {reportPath}");

        if (settings.OpenInBrowser)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = reportPath,
                UseShellExecute = true
            });
        }

        return 0;
    }

    private async Task<string> GenerateSharpnessCalibrationReport(List<SharpnessTestResult> results, Settings settings)
    {
        Directory.CreateDirectory(settings.OutputDirectory);
        
        var reportPath = Path.Combine(settings.OutputDirectory, $"sharpness-calibration-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        
        var html = new StringBuilder();
        html.AppendLine(@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>FaceOFFx Sharpness Calibration Report</title>
    <script src=""https://cdn.jsdelivr.net/npm/chart.js""></script>
    <style>
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background-color: #f5f5f5;
            margin: 0;
            padding: 20px;
        }
        .container {
            max-width: 1400px;
            margin: 0 auto;
            background: white;
            padding: 30px;
            border-radius: 10px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
        }
        h1 { color: #333; text-align: center; margin-bottom: 30px; }
        h2 { color: #555; margin-top: 40px; border-bottom: 2px solid #eee; padding-bottom: 10px; }
        .summary {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 20px;
            margin: 30px 0;
        }
        .summary-card {
            background: #f8f9fa;
            padding: 20px;
            border-radius: 8px;
            border: 1px solid #e0e0e0;
            text-align: center;
        }
        .summary-card h3 {
            margin: 0 0 10px 0;
            color: #666;
            font-size: 14px;
            text-transform: uppercase;
        }
        .summary-card .value {
            font-size: 28px;
            font-weight: bold;
            color: #333;
        }
        .chart-container {
            position: relative;
            height: 400px;
            margin: 30px 0;
        }
        .person-section {
            margin: 40px 0;
            padding: 20px;
            background: #fafafa;
            border-radius: 8px;
        }
        .person-name {
            font-size: 24px;
            font-weight: bold;
            color: #444;
            margin-bottom: 20px;
        }
        .blur-grid {
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
            gap: 15px;
            margin-top: 20px;
        }
        .blur-card {
            background: white;
            border: 2px solid #ddd;
            border-radius: 8px;
            padding: 10px;
            text-align: center;
        }
        .blur-card.pass { border-color: #4caf50; }
        .blur-card.fail { border-color: #f44336; }
        .blur-card img {
            width: 100%;
            height: 150px;
            object-fit: cover;
            border-radius: 5px;
            margin-bottom: 10px;
        }
        .blur-card .blur-level {
            font-size: 14px;
            color: #666;
            margin-bottom: 5px;
        }
        .blur-card .score {
            font-size: 20px;
            font-weight: bold;
            margin: 5px 0;
        }
        .blur-card.pass .score { color: #4caf50; }
        .blur-card.fail .score { color: #f44336; }
        .baseline-info {
            background: #e3f2fd;
            border: 1px solid #90caf9;
            border-radius: 8px;
            padding: 15px;
            margin: 10px 0;
        }
        .baseline-info strong { color: #1976d2; }
        table {
            width: 100%;
            border-collapse: collapse;
            margin: 20px 0;
        }
        th, td {
            padding: 10px;
            text-align: left;
            border-bottom: 1px solid #ddd;
        }
        th {
            background: #f8f9fa;
            font-weight: bold;
        }
    </style>
</head>
<body>
    <div class=""container"">
        <h1>FaceOFFx Sharpness Calibration Report</h1>
        <p style=""text-align: center; color: #666;"">Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + @"</p>");

        // Summary statistics
        var totalImages = results.Count;
        var passCount = results.Count(r => r.Pass);
        var threshold = results.FirstOrDefault()?.Threshold ?? 0.7f;
        var baseNames = results.Select(r => r.BaseName).Distinct().Count();
        
        html.AppendLine(@"
        <div class=""summary"">
            <div class=""summary-card"">
                <h3>Total Images</h3>
                <div class=""value"">" + totalImages + @"</div>
            </div>
            <div class=""summary-card"">
                <h3>Pass Rate</h3>
                <div class=""value"">" + (100.0 * passCount / totalImages).ToString("F1") + @"%</div>
            </div>
            <div class=""summary-card"">
                <h3>Threshold</h3>
                <div class=""value"">" + (threshold * 100).ToString("F0") + @"%</div>
            </div>
            <div class=""summary-card"">
                <h3>Test Subjects</h3>
                <div class=""value"">" + baseNames + @"</div>
            </div>
        </div>");

        // Group results by base name
        var groupedResults = results.GroupBy(r => r.BaseName).OrderBy(g => g.Key);
        
        // Charts data
        var chartData = new List<string>();
        
        foreach (var group in groupedResults)
        {
            var personResults = group.OrderBy(r => r.BlurLevel).ToList();
            var baseline = personResults.FirstOrDefault(r => r.BlurLevel == 0);
            
            html.AppendLine($@"
        <div class=""person-section"">
            <div class=""person-name"">{group.Key}</div>");
            
            if (baseline != null)
            {
                html.AppendLine($@"
            <div class=""baseline-info"">
                <strong>Baseline Sharpness:</strong> {baseline.SharpnessScore:P1} 
                (High Frequency Ratio: {baseline.HighFrequencyRatio:F4})
            </div>");
            }
            
            html.AppendLine(@"<div class=""blur-grid"">");
            
            foreach (var result in personResults)
            {
                var passClass = result.Pass ? "pass" : "fail";
                var imagePath = GetRelativeImagePath(result, settings);
                
                html.AppendLine($@"
                <div class=""blur-card {passClass}"">
                    {(File.Exists(imagePath) ? $@"<img src=""{imagePath}"" alt=""{result.FileName}"">" : "")}
                    <div class=""blur-level"">Blur {result.BlurLevel:F2}</div>
                    <div class=""score"">{result.SharpnessScore:P1}</div>
                    <div style=""font-size: 12px; color: #666;"">Ratio: {result.HighFrequencyRatio:F4}</div>
                </div>");
            }
            
            html.AppendLine("</div></div>");
            
            // Add to chart data
            var dataPoints = string.Join(", ", personResults.Select(r => 
                $"{{x: {r.BlurLevel:F2}, y: {(r.SharpnessScore * 100):F1}}}"));
            
            chartData.Add($@"{{
                label: '{group.Key}',
                data: [{dataPoints}],
                borderColor: '{GetColorForIndex(chartData.Count)}',
                backgroundColor: '{GetColorForIndex(chartData.Count)}33',
                tension: 0.1
            }}");
        }

        // Add chart
        html.AppendLine(@"
        <h2>Blur Progression Comparison</h2>
        <div class=""chart-container"">
            <canvas id=""progressionChart""></canvas>
        </div>

        <h2>Detailed Results</h2>
        <table>
            <thead>
                <tr>
                    <th>Image</th>
                    <th>Blur Level</th>
                    <th>Sharpness Score</th>
                    <th>High Freq Ratio</th>
                    <th>Pass/Fail</th>
                    <th>Delta from Baseline</th>
                </tr>
            </thead>
            <tbody>");

        foreach (var group in groupedResults)
        {
            var baseline = group.FirstOrDefault(r => r.BlurLevel == 0)?.SharpnessScore ?? 0;
            
            foreach (var result in group.OrderBy(r => r.BlurLevel))
            {
                var delta = baseline > 0 ? (result.SharpnessScore - baseline) : 0;
                var deltaStr = delta < 0 ? $"{delta:P1}" : "";
                
                html.AppendLine($@"
                <tr>
                    <td>{result.FileName}</td>
                    <td>{result.BlurLevel:F2}</td>
                    <td style=""color: {(result.Pass ? "#4caf50" : "#f44336")}"">{result.SharpnessScore:P1}</td>
                    <td>{result.HighFrequencyRatio:F4}</td>
                    <td>{(result.Pass ? "✅ Pass" : "❌ Fail")}</td>
                    <td>{deltaStr}</td>
                </tr>");
            }
        }

        html.AppendLine(@"
            </tbody>
        </table>
    </div>

    <script>
        const ctx = document.getElementById('progressionChart').getContext('2d');
        new Chart(ctx, {
            type: 'line',
            data: {
                datasets: [" + string.Join(",\n", chartData) + @"]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    title: {
                        display: true,
                        text: 'Sharpness Score vs Blur Level'
                    },
                    tooltip: {
                        callbacks: {
                            label: function(context) {
                                return context.dataset.label + ': ' + context.parsed.y.toFixed(1) + '%';
                            }
                        }
                    }
                },
                scales: {
                    x: {
                        type: 'linear',
                        title: {
                            display: true,
                            text: 'Blur Level (sigma)'
                        },
                        min: 0,
                        max: 1.1
                    },
                    y: {
                        title: {
                            display: true,
                            text: 'Sharpness Score (%)'
                        },
                        min: 0,
                        max: 105
                    }
                },
                elements: {
                    point: {
                        radius: 4,
                        hoverRadius: 6
                    }
                }
            }
        });
    </script>
</body>
</html>");

        await File.WriteAllTextAsync(reportPath, html.ToString());
        return reportPath;
    }

    private string GetRelativeImagePath(SharpnessTestResult result, Settings settings)
    {
        // Try to find the image file
        var imagesDir = settings.ImagesDirectory ?? Path.Combine(DefaultTestDirectory, "blur-progressions");
        var imagePath = Path.Combine(imagesDir, result.FileName);
        
        if (File.Exists(imagePath))
        {
            // Return relative path from report directory
            return Path.GetRelativePath(settings.OutputDirectory, imagePath);
        }
        
        return "";
    }

    private string GetColorForIndex(int index)
    {
        var colors = new[]
        {
            "rgb(75, 192, 192)",
            "rgb(54, 162, 235)", 
            "rgb(255, 99, 132)",
            "rgb(255, 159, 64)",
            "rgb(153, 102, 255)",
            "rgb(255, 205, 86)"
        };
        
        return colors[index % colors.Length];
    }
}

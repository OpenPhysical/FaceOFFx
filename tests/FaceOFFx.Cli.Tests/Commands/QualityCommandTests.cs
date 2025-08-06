using System.Text.Json;
using FaceOFFx.Cli.Commands;
using FaceOFFx.Core.Domain.Quality;
using FaceOFFx.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Cli.Extensions.DependencyInjection;
using Spectre.Console.Testing;

namespace FaceOFFx.Cli.Tests.Commands;

[TestFixture]
public class QualityCommandTests : UnitTestBase
{
    private const string TestImagePath = "Resources/test-face.jpg";
    
    [SetUp]
    public void Setup()
    {
        // Ensure test image exists
        EnsureTestImage(TestImagePath);
    }
    
    [Test]
    public async Task QualityCommand_WithValidImage_ReturnsSuccess()
    {
        // Arrange
        var (app, console) = CreateApplication();
        
        // Act
        var result = await app.RunAsync(new[] { "quality", "--input", TestImagePath });
        
        // Assert
        result.Should().Be(0);
        var output = console.Output;
        output.Should().Contain("Status:");
        output.Should().Contain("Overall Quality:");
        output.Should().ContainAny("COMPLIANT", "NON-COMPLIANT");
    }
    
    [Test]
    public async Task QualityCommand_WithJsonFormat_ReturnsValidJson()
    {
        // Arrange
        var (app, console) = CreateApplication();
        
        // Act
        var result = await app.RunAsync(new[] { "quality", "--input", TestImagePath, "--format", "json" });
        
        // Assert
        result.Should().Be(0);
        var output = console.Output;
        
        // Verify valid JSON
        var json = JsonDocument.Parse(output);
        var isCompliant = json.RootElement.GetProperty("IsCompliant").ValueKind;
        (isCompliant == JsonValueKind.True || isCompliant == JsonValueKind.False).Should().BeTrue();
        json.RootElement.GetProperty("OverallScore").GetDouble().Should().BeInRange(0, 1);
        json.RootElement.GetProperty("Scores").Should().NotBeNull();
    }
    
    [Test]
    public async Task QualityCommand_WithDetailedFormat_ShowsExtendedInfo()
    {
        // Arrange
        var (app, console) = CreateApplication();
        
        // Act
        var result = await app.RunAsync(new[] { "quality", "--input", TestImagePath, "--format", "detailed" });
        
        // Assert
        result.Should().Be(0);
        var output = console.Output;
        
        output.Should().Contain("Detailed Analysis");
        output.Should().Contain("Regional Sharpness");
        output.Should().Contain("Image Dimensions");
        output.Should().Contain("Assessment completed at");
    }
    
    [Test]
    public async Task QualityCommand_WithDifferentStandards_AppliesCorrectCriteria()
    {
        // Arrange
        var standards = new[] { "piv", "twic", "icao", "cac" };
        
        foreach (var standard in standards)
        {
            var (app, console) = CreateApplication();
            
            // Act
            var result = await app.RunAsync(new[] { "quality", "--input", TestImagePath, "--standard", standard });
            
            // Assert
            result.Should().Be(0);
            var output = console.Output;
            output.Should().Contain("Overall Quality:");
        }
    }
    
    [Test]
    public async Task QualityCommand_WithThreshold_EnforcesMinimumScore()
    {
        // Arrange
        var (app, console) = CreateApplication();
        
        // Act
        var result = await app.RunAsync(new[] { "quality", "--input", TestImagePath, "--threshold", "0.9", "--format", "json" });
        
        // Assert
        result.Should().Be(0);
        var output = console.Output;
        var json = JsonDocument.Parse(output);
        
        // If compliant, score should be >= 0.9
        if (json.RootElement.GetProperty("IsCompliant").GetBoolean())
        {
            json.RootElement.GetProperty("OverallScore").GetDouble().Should().BeGreaterOrEqualTo(0.9);
        }
    }
    
    [Test]
    public async Task QualityCommand_WithStrictMode_AppliesStrictSettings()
    {
        // Arrange
        var (app, console) = CreateApplication();
        
        // Act
        var result = await app.RunAsync(new[] { "quality", "--input", TestImagePath, "--strict", "--format", "json" });
        
        // Assert
        result.Should().Be(0);
        var output = console.Output;
        var json = JsonDocument.Parse(output);
        
        // Strict mode should have higher threshold (0.85)
        if (json.RootElement.GetProperty("IsCompliant").GetBoolean())
        {
            json.RootElement.GetProperty("OverallScore").GetDouble().Should().BeGreaterOrEqualTo(0.85);
        }
    }
    
    [Test]
    public async Task QualityCommand_WithOutputPath_SavesReportToFile()
    {
        // Arrange
        var (app, console) = CreateApplication();
        var outputPath = Path.Combine(Path.GetTempPath(), "quality-report.json");
        
        try
        {
            // Act
            var result = await app.RunAsync(new[] { "quality", "--input", TestImagePath, "--format", "json", "--output", outputPath });
            
            // Assert
            result.Should().Be(0);
            File.Exists(outputPath).Should().BeTrue();
            
            var fileContent = await File.ReadAllTextAsync(outputPath);
            var json = JsonDocument.Parse(fileContent);
            json.RootElement.GetProperty("IsCompliant").Should().NotBeNull();
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }
    
    [Test]
    public async Task QualityCommand_WithNonExistentFile_ReturnsError()
    {
        // Arrange
        var (app, console) = CreateApplication();
        
        // Act
        var result = await app.RunAsync(new[] { "quality", "--input", "non-existent-file.jpg" });
        
        // Assert
        result.Should().Be(1);
        var output = console.Output;
        output.Should().Contain("Error: Input file not found");
    }
    
    [Test]
    public async Task QualityCommand_ShowsViolations_WhenQualityIssuesExist()
    {
        // Arrange
        var (app, console) = CreateApplication();
        
        // Create a low-quality test image if needed
        EnsureTestImage("Resources/low-quality-face.jpg", createLowQuality: true);
        
        // Act
        var result = await app.RunAsync(new[] { "quality", "--input", "Resources/low-quality-face.jpg" });
        
        // Assert
        result.Should().Be(0);
        var output = console.Output;
        
        // Check if violations are shown when present
        if (output.Contains("NON-COMPLIANT"))
        {
            output.Should().Contain("Violations:");
        }
    }
    
    private (CommandApp app, TestConsole console) CreateApplication()
    {
        var services = new ServiceCollection();
        services.AddFaceOffxCli();
        services.AddLogging(builder => builder.AddSimpleConsole());
        
        var registrar = new DependencyInjectionRegistrar(services);
        var console = new TestConsole();
        
        // Configure console output properly
        AnsiConsole.Console = console;
        
        var app = new CommandApp(registrar);
        
        app.Configure(config =>
        {
            config.AddCommand<QualityCommand>("quality");
        });
        
        return (app, console);
    }
    
    private void EnsureTestImage(string path, bool createLowQuality = false)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        if (!File.Exists(path))
        {
            // Create a simple test image
            // In a real test, you would copy an actual test image from test resources
            CreateTestFaceImage(path, createLowQuality);
        }
    }
    
    private void CreateTestFaceImage(string path, bool lowQuality)
    {
        // This is a placeholder - in real tests, use actual test images
        // For now, create a simple image file
        using var image = new Image<Rgba32>(420, 560);
        
        if (lowQuality)
        {
            // Apply blur or compression to simulate low quality
            image.Mutate(ctx => ctx.GaussianBlur(10));
        }
        
        image.SaveAsJpeg(path, new JpegEncoder());
    }
}
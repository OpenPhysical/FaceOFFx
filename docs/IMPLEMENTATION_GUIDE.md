# FaceOFFx Implementation Guide

## Migration from Current System to Universal CLI

This guide provides step-by-step instructions for implementing the universal CLI architecture while maintaining the existing functionality.

## Phase 1: Quality System Refactor (Immediate)

### 1.1 Create Compliance-First Domain Models

#### Replace Combined Scoring with Pass/Fail Assessment
```csharp
// OLD: Confusing combined score
public record Iso19794Assessment(
    QualityScore Overall,  // ← Confusing 42% combined score
    // ...
);

// NEW: Clear pass/fail per metric
public record ComplianceAssessment(
    SymmetryCompliance Symmetry,      // PASS/FAIL + specific reason
    SharpnessCompliance Sharpness,    // PASS/FAIL + specific reason  
    GeometryCompliance Geometry,      // PASS/FAIL + specific reason
    bool IsCompliant =>               // All must pass
        Symmetry.Passed && Sharpness.Passed && Geometry.Passed,
    IReadOnlyList<RejectionReason> Rejections,
    Standard AppliedStandard
);

public record RejectionReason(
    string Metric,           // "Illumination Asymmetry"
    float ActualValue,       // 51%
    float RequiredValue,     // 50%
    Standard Standard,       // PIV
    string Message          // "REJECTED: 51% illumination asymmetry exceeds PIV 50% limit"
);
```

#### Create Metric-Specific Compliance Records
```csharp
public record SymmetryCompliance(
    bool Passed,
    float IlluminationAsymmetryPercent,
    float PoseAsymmetryPercent,
    float RequiredThreshold,
    string? RejectionReason
);

public record SharpnessCompliance(
    bool Passed,
    float SharpnessPercent,
    float RequiredThreshold,
    Dictionary<string, float> RegionalScores,
    string? RejectionReason
);

public record GeometryCompliance(
    bool Passed,
    HeadSizeCompliance HeadSize,
    CenteringCompliance Centering,
    IpdCompliance InterPupillaryDistance,
    string? RejectionReason
);
```

### 1.2 Implement Standard-Specific Thresholds

#### Create Compliance Rules Registry
```csharp
public static class ComplianceStandards
{
    public static readonly Dictionary<string, ComplianceRules> Standards = new()
    {
        ["PIV"] = new ComplianceRules
        {
            MinSymmetryPercent = 60f,
            MinSharpnessPercent = 50f,
            MinHeadSizePercent = 50f,
            MaxHeadSizePercent = 75f,
            MinIpdPixels = 90f,
            MaxIpdPixels = 120f
        },
        ["TWIC"] = new ComplianceRules
        {
            MinSymmetryPercent = 65f,
            MinSharpnessPercent = 55f,
            // ... TWIC-specific thresholds
        },
        ["ICAO"] = new ComplianceRules
        {
            MinSymmetryPercent = 70f,
            MinSharpnessPercent = 60f,
            // ... ICAO-specific thresholds
        }
    };
}
```

### 1.3 Update Quality Assessors

#### Modify Assessors to Return Raw Measurements
```csharp
// OLD: Return normalized scores
public static Result<FacialSymmetryScore> Assess(...)

// NEW: Return raw measurements + compliance assessment
public static Result<SymmetryMeasurement> Assess(...)

public record SymmetryMeasurement(
    float IlluminationAsymmetryPercent,
    float PoseAsymmetryPercent,
    IReadOnlyList<float> GaborResponses
);

// Separate compliance evaluation
public static SymmetryCompliance EvaluateCompliance(
    SymmetryMeasurement measurement, 
    ComplianceRules rules)
{
    var illuminationPassed = measurement.IlluminationAsymmetryPercent >= rules.MinSymmetryPercent;
    var posePassed = measurement.PoseAsymmetryPercent >= rules.MinSymmetryPercent;
    
    return new SymmetryCompliance(
        Passed: illuminationPassed && posePassed,
        IlluminationAsymmetryPercent: measurement.IlluminationAsymmetryPercent,
        PoseAsymmetryPercent: measurement.PoseAsymmetryPercent,
        RequiredThreshold: rules.MinSymmetryPercent,
        RejectionReason: !illuminationPassed 
            ? $"REJECTED: {measurement.IlluminationAsymmetryPercent:F1}% illumination asymmetry < {rules.MinSymmetryPercent}% required"
            : !posePassed 
            ? $"REJECTED: {measurement.PoseAsymmetryPercent:F1}% pose asymmetry < {rules.MinSymmetryPercent}% required"
            : null
    );
}
```

## Phase 2: Universal CLI Commands (Next)

### 2.1 Extend Existing Commands

#### Add Analysis-Only Commands
```csharp
// New validate command - quick pass/fail
[Command("validate")]
public class ValidateCommand : AsyncCommand<ValidateCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var result = await imageData.AssessComplianceAsync(settings.Standard);
        
        if (result.IsSuccess && result.Value.IsCompliant)
        {
            AnsiConsole.MarkupLine($"[green]✅ {settings.Standard.ToUpper()} COMPLIANT[/]");
            return 0;
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]❌ REJECTED for {settings.Standard.ToUpper()} use[/]");
            foreach (var rejection in result.Value.Rejections)
            {
                AnsiConsole.MarkupLine($"[red]  • {rejection.Message}[/]");
            }
            return 1;
        }
    }
}

// New analyze command - detailed report
[Command("analyze")]
public class AnalyzeCommand : AsyncCommand<AnalyzeCommand.Settings>
{
    // Implementation shows detailed metrics, visual overlays, recommendations
}
```

#### Add Batch Processing Commands
```csharp
[Command("batch")]
public class BatchCommand : AsyncCommand<BatchCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var images = Directory.GetFiles(settings.InputDirectory, "*.jpg");
        var results = new List<BatchResult>();
        
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Processing images", true, images.Length);
                
                foreach (var image in images)
                {
                    var result = settings.ProcessingMode switch
                    {
                        "validate-only" => await ProcessValidateOnly(image, settings.Standard),
                        "process" => await ProcessWithCompliance(image, settings),
                        _ => throw new ArgumentException("Invalid processing mode")
                    };
                    
                    results.Add(result);
                    task.Increment(1);
                }
            });
        
        // Generate summary report
        GenerateBatchSummary(results, settings);
        return 0;
    }
}
```

### 2.2 Human-in-the-Loop Enhancements

#### Interactive Improvement Command
```csharp
[Command("improve")]
public class ImproveCommand : AsyncCommand<ImproveCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var assessment = await imageData.AssessComplianceAsync(settings.Standard);
        
        if (assessment.IsCompliant)
        {
            AnsiConsole.MarkupLine("[green]✅ Image is already compliant![/]");
            return 0;
        }
        
        AnsiConsole.MarkupLine($"[yellow]Analyzing {assessment.Rejections.Count} compliance issues...[/]");
        
        foreach (var rejection in assessment.Rejections)
        {
            ShowImprovementSuggestion(rejection);
            
            if (AnsiConsole.Confirm("Would you like to see a visual example?"))
            {
                ShowVisualExample(rejection.Metric);
            }
        }
        
        return 0;
    }
    
    private void ShowImprovementSuggestion(RejectionReason rejection)
    {
        switch (rejection.Metric)
        {
            case "Illumination Asymmetry":
                AnsiConsole.MarkupLine("[blue]💡 Suggestions for better illumination:[/]");
                AnsiConsole.MarkupLine("  • Use even lighting from both sides");
                AnsiConsole.MarkupLine("  • Avoid shadows on one side of face");
                AnsiConsole.MarkupLine("  • Position subject facing the light source");
                break;
                
            case "Sharpness":
                AnsiConsole.MarkupLine("[blue]💡 Suggestions for better sharpness:[/]");
                AnsiConsole.MarkupLine("  • Use faster shutter speed");
                AnsiConsole.MarkupLine("  • Ensure proper focus on eyes/face");
                AnsiConsole.MarkupLine("  • Use tripod to reduce camera shake");
                break;
                
            // ... more suggestions
        }
    }
}
```

## Phase 3: Processing Pipeline Integration

### 3.1 Compliance-Gated Processing

#### Update ProcessingExtensions
```csharp
public static async Task<Result<ProcessingResultDto>> ProcessWithComplianceAsync(
    this byte[] imageData,
    ProcessingOptions processingOptions,
    ComplianceRequirement requirement = ComplianceRequirement.Strict)
{
    // Step 1: Assess compliance first
    var complianceResult = await imageData.AssessComplianceAsync(processingOptions.Standard);
    if (complianceResult.IsFailure)
    {
        return Result.Failure<ProcessingResultDto>($"Compliance assessment failed: {complianceResult.Error}");
    }
    
    var compliance = complianceResult.Value;
    
    // Step 2: Fail fast if strict compliance required
    if (requirement == ComplianceRequirement.Strict && !compliance.IsCompliant)
    {
        var primaryReason = compliance.Rejections.FirstOrDefault()?.Message ?? "Multiple compliance violations";
        return Result.Failure<ProcessingResultDto>($"Image rejected for processing: {primaryReason}");
    }
    
    // Step 3: Process the image
    var processingResult = await ProcessImageInternal(imageData, processingOptions);
    if (processingResult.IsFailure)
    {
        return processingResult;
    }
    
    // Step 4: Add compliance metadata
    var enhancedMetadata = processingResult.Value.Metadata with
    {
        ComplianceAssessment = compliance,
        RequiresManualReview = !compliance.IsCompliant,
        QualityWarnings = compliance.Rejections.Select(r => r.Message).ToList()
    };
    
    return Result.Success(processingResult.Value with { Metadata = enhancedMetadata });
}
```

### 3.2 Enhanced Processing Results

#### Update ProcessingResultDto
```csharp
public sealed record ProcessingResultDto(
    byte[] ImageData,
    ProcessingMetadataDto Metadata,
    ComplianceAssessment? Compliance = null,  // NEW: Full compliance report
    bool RequiresManualReview = false        // NEW: Flag for human review
);

public sealed record ProcessingMetadataDto(
    // ... existing fields ...
    ComplianceAssessment? Compliance,        // NEW: Full compliance report
    bool RequiresManualReview,               // NEW: Flag for human review
    IReadOnlyList<string> QualityWarnings    // NEW: Non-blocking issues
);
```

## Phase 4: CLI Output Improvements

### 4.1 Clear Compliance Reporting

#### Replace Confusing Scores with Clear Status
```csharp
// OLD: Confusing output
// Status: NON-COMPLIANT
// Overall Quality: 42.0%
// Symmetry: 49.4% ❌

// NEW: Clear compliance status
private static void OutputComplianceStatus(ComplianceAssessment assessment)
{
    var standard = assessment.AppliedStandard.Name.ToUpper();
    AnsiConsole.MarkupLine($"[bold]{standard} COMPLIANCE ASSESSMENT[/]");
    AnsiConsole.WriteLine(new string('━', 30));
    
    // Geometry
    var geoStatus = assessment.Geometry.Passed ? "[green]✅ PASS[/]" : "[red]❌ FAIL[/]";
    var geoDetails = $"(Head: {assessment.Geometry.HeadSize.SizePercent:F0}%, IPD: {assessment.Geometry.InterPupillaryDistance.DistancePixels:F0}px)";
    AnsiConsole.MarkupLine($"Geometry      {geoStatus}    {geoDetails}");
    
    // Symmetry
    var symStatus = assessment.Symmetry.Passed ? "[green]✅ PASS[/]" : "[red]❌ FAIL[/]";
    var symDetails = assessment.Symmetry.Passed 
        ? $"(Illumination: {assessment.Symmetry.IlluminationAsymmetryPercent:F0}%, Pose: {assessment.Symmetry.PoseAsymmetryPercent:F0}%)"
        : $"({assessment.Symmetry.RejectionReason})";
    AnsiConsole.MarkupLine($"Symmetry      {symStatus}    {symDetails}");
    
    // Overall result
    AnsiConsole.WriteLine();
    if (assessment.IsCompliant)
    {
        AnsiConsole.MarkupLine($"[green]RESULT: ✅ COMPLIANT for {standard} use[/]");
    }
    else
    {
        AnsiConsole.MarkupLine($"[red]RESULT: ❌ REJECTED for {standard} use ({assessment.Rejections.Count} violations)[/]");
    }
}
```

## Implementation Priority

### High Priority (Week 1)
1. ✅ Fix symmetry assessment (completed)
2. Create compliance-first domain models
3. Update quality assessors to return raw measurements
4. Implement standard-specific thresholds

### Medium Priority (Week 2)
1. Add validate/analyze/inspect commands
2. Update CLI output for clear compliance status
3. Integrate compliance checks into processing pipeline
4. Add batch processing with human review queue

### Lower Priority (Future)
1. Interactive improvement command
2. Educational/training modes
3. API server mode
4. Custom standards support

## Migration Steps

1. **Preserve Existing API**: Keep current commands working during transition
2. **Add New Commands**: Implement validate/analyze alongside existing quality command
3. **Update Output Format**: Gradually migrate from combined scores to compliance status
4. **Extend Processing Pipeline**: Add compliance metadata to results
5. **Deprecate Old Patterns**: Phase out confusing combined scoring system

This approach ensures zero breaking changes while building the foundation for the universal CLI architecture.
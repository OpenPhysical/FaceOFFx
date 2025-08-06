# FaceOFFx Quality System Architecture

## Overview

The FaceOFFx quality system transforms from a confusing combined-score approach to a compliance-first architecture that provides clear pass/fail criteria for government ID standards.

## Current Problems with Combined Scoring

### Issues with Current System
```
Status: NON-COMPLIANT
Overall Quality: 42.0%                    ← Confusing combined score
╭─────────────────┬────────┬────────╮
│ Symmetry        │ 49.4%  │ ❌     │     ← What does 49.4% mean?
│ ├─ Illumination │ 49.8%  │ ❌     │     ← Is this good or bad?
│ └─ Pose         │ 49.1%  │ ❌     │     ← Why exactly did it fail?
│ Sharpness       │ 1.7%   │ ❌     │
│ Geometry        │ 66.7%  │ ⚠️      │
╰─────────────────┴────────┴────────╯

Violations:
• Symmetry: Facial symmetry score 49.4% is below acceptable threshold  ← Generic message
• Illumination: Uneven illumination detected                           ← Not actionable
```

**Problems:**
1. **49.4% symmetry** - User doesn't know if this is close or terrible
2. **"Below acceptable threshold"** - What is the threshold? 
3. **Generic violations** - Doesn't explain how to improve
4. **Combined scoring confuses** actual compliance requirements

## New Compliance-First Architecture

### Clear Pass/Fail with Specific Reasons
```
PIV COMPLIANCE ASSESSMENT
━━━━━━━━━━━━━━━━━━━━━━━━━
✅ Geometry      PASS    (Head: 68%, IPD: 95px, Centering: 100%)
❌ Illumination  FAIL    (51% asymmetry > 50% PIV limit)  
❌ Pose         FAIL    (49% asymmetry > 50% PIV limit)
❌ Sharpness    FAIL    (1.7% < 50% PIV minimum)

RESULT: REJECTED for PIV use (3 violations)

SPECIFIC REJECTIONS:
• REJECTED: 51% illumination asymmetry exceeds PIV 50% limit
• REJECTED: 49% pose asymmetry exceeds PIV 50% limit  
• REJECTED: 1.7% sharpness below PIV 50% minimum
```

**Benefits:**
1. **Clear thresholds** - User knows exactly what's required
2. **Specific reasons** - Actionable feedback for improvement
3. **Standard-specific** - PIV vs ICAO vs TWIC requirements
4. **No confusing combined scores** - Each metric stands alone

## Core Domain Models

### Compliance Assessment
```csharp
public record ComplianceAssessment(
    SymmetryCompliance Symmetry,      // PASS/FAIL + specific reason
    SharpnessCompliance Sharpness,    // PASS/FAIL + specific reason  
    GeometryCompliance Geometry,      // PASS/FAIL + specific reason
    Standard AppliedStandard,         // PIV/TWIC/ICAO/CAC
    IReadOnlyList<RejectionReason> Rejections
)
{
    public bool IsCompliant => Symmetry.Passed && Sharpness.Passed && Geometry.Passed;
    
    public string Summary => IsCompliant 
        ? $"✅ COMPLIANT for {AppliedStandard.Name} use"
        : $"❌ REJECTED for {AppliedStandard.Name} use ({Rejections.Count} violations)";
}
```

### Specific Rejection Reasons
```csharp
public record RejectionReason(
    string Metric,           // "Illumination Asymmetry"
    float ActualValue,       // 51%
    float RequiredValue,     // 50%
    Standard Standard,       // PIV
    ComparisonType Type,     // MustBeGreaterThan, MustBeLessThan, MustBeInRange
    string Message          // "REJECTED: 51% illumination asymmetry exceeds PIV 50% limit"
)
{
    public static RejectionReason Create(string metric, float actual, float required, Standard standard, ComparisonType type)
    {
        var message = type switch
        {
            ComparisonType.MustBeGreaterThan => 
                $"REJECTED: {actual:F1}% {metric.ToLower()} < {required:F0}% {standard.Name} minimum",
            ComparisonType.MustBeLessThan => 
                $"REJECTED: {actual:F1}% {metric.ToLower()} > {required:F0}% {standard.Name} maximum",
            ComparisonType.MustBeInRange => 
                $"REJECTED: {actual:F1}px {metric.ToLower()} outside {standard.Name} range",
            _ => $"REJECTED: {metric} {actual:F1} violates {standard.Name} requirements"
        };
        
        return new RejectionReason(metric, actual, required, standard, type, message);
    }
}
```

### Metric-Specific Compliance
```csharp
public record SymmetryCompliance(
    bool Passed,
    float IlluminationAsymmetryPercent,
    float PoseAsymmetryPercent,
    float RequiredThreshold,
    RejectionReason? Rejection = null
);

public record SharpnessCompliance(
    bool Passed,
    float OverallSharpnessPercent,
    float RequiredThreshold,
    Dictionary<string, float> RegionalScores,
    RejectionReason? Rejection = null
);

public record GeometryCompliance(
    bool Passed,
    HeadSizeCompliance HeadSize,
    CenteringCompliance Centering,
    IpdCompliance InterPupillaryDistance,
    RejectionReason? Rejection = null
);
```

## Standard-Specific Compliance Rules

### Government ID Standards
```csharp
public static class ComplianceStandards
{
    public static readonly Dictionary<string, ComplianceRules> Rules = new()
    {
        ["PIV"] = new ComplianceRules
        {
            Name = "PIV",
            MinSymmetryPercent = 60f,
            MinSharpnessPercent = 50f,
            MinHeadSizePercent = 50f,
            MaxHeadSizePercent = 75f,
            MinIpdPixels = 90f,
            MaxIpdPixels = 120f,
            ExpectedDimensions = new(420, 560)
        },
        ["TWIC"] = new ComplianceRules
        {
            Name = "TWIC", 
            MinSymmetryPercent = 65f,    // Stricter than PIV
            MinSharpnessPercent = 55f,   // Stricter than PIV
            MinHeadSizePercent = 50f,
            MaxHeadSizePercent = 75f,
            MinIpdPixels = 90f,
            MaxIpdPixels = 120f,
            ExpectedDimensions = new(420, 560)
        },
        ["ICAO"] = new ComplianceRules
        {
            Name = "ICAO",
            MinSymmetryPercent = 70f,    // Strictest for passports
            MinSharpnessPercent = 60f,   // Strictest for passports
            MinHeadSizePercent = 50f,
            MaxHeadSizePercent = 75f,
            MinIpdPixels = 90f,
            MaxIpdPixels = 120f,
            ExpectedDimensions = new(413, 531)  // Different size for passports
        },
        ["CAC"] = new ComplianceRules
        {
            Name = "CAC",
            // Same as PIV for military cards
            MinSymmetryPercent = 60f,
            MinSharpnessPercent = 50f,
            MinHeadSizePercent = 50f,
            MaxHeadSizePercent = 75f,
            MinIpdPixels = 90f,
            MaxIpdPixels = 120f,
            ExpectedDimensions = new(420, 560)
        }
    };
}
```

### Custom Organization Standards
```csharp
// Support for organization-specific rules
public static ComplianceRules LoadCustomStandard(string filePath)
{
    var json = File.ReadAllText(filePath);
    return JsonSerializer.Deserialize<ComplianceRules>(json);
}

// Example: my-org.json
{
    "Name": "MyOrg",
    "MinSymmetryPercent": 55.0,     // Slightly more lenient
    "MinSharpnessPercent": 45.0,    // More lenient for older cameras
    "MinHeadSizePercent": 45.0,     // Allow smaller heads
    "MaxHeadSizePercent": 80.0,     // Allow larger heads
    "MinIpdPixels": 85.0,           // Slightly smaller range
    "MaxIpdPixels": 125.0,          // Slightly larger range
    "ExpectedDimensions": { "Width": 400, "Height": 600 }
}
```

## Quality Assessment Pipeline

### Raw Measurement → Compliance Evaluation
```csharp
public static async Task<Result<ComplianceAssessment>> AssessComplianceAsync(
    byte[] imageData,
    string standardName = "PIV")
{
    // Step 1: Get compliance rules for standard
    var rules = ComplianceStandards.Rules[standardName];
    
    // Step 2: Perform raw measurements (no scoring yet)
    var measurements = await PerformRawMeasurementsAsync(imageData);
    if (measurements.IsFailure) return Result.Failure<ComplianceAssessment>(measurements.Error);
    
    // Step 3: Evaluate compliance against rules
    var symmetryCompliance = EvaluateSymmetryCompliance(measurements.Value.Symmetry, rules);
    var sharpnessCompliance = EvaluateSharpnessCompliance(measurements.Value.Sharpness, rules);
    var geometryCompliance = EvaluateGeometryCompliance(measurements.Value.Geometry, rules);
    
    // Step 4: Collect all rejections
    var rejections = new List<RejectionReason>();
    if (symmetryCompliance.Rejection != null) rejections.Add(symmetryCompliance.Rejection);
    if (sharpnessCompliance.Rejection != null) rejections.Add(sharpnessCompliance.Rejection);
    if (geometryCompliance.Rejection != null) rejections.Add(geometryCompliance.Rejection);
    
    return Result.Success(new ComplianceAssessment(
        symmetryCompliance,
        sharpnessCompliance, 
        geometryCompliance,
        rules.ToStandard(),
        rejections
    ));
}

private static SymmetryCompliance EvaluateSymmetryCompliance(
    SymmetryMeasurement measurement, 
    ComplianceRules rules)
{
    var illuminationPassed = measurement.IlluminationAsymmetryPercent >= rules.MinSymmetryPercent;
    var posePassed = measurement.PoseAsymmetryPercent >= rules.MinSymmetryPercent;
    
    RejectionReason? rejection = null;
    if (!illuminationPassed)
    {
        rejection = RejectionReason.Create(
            "Illumination Asymmetry",
            measurement.IlluminationAsymmetryPercent,
            rules.MinSymmetryPercent,
            rules.ToStandard(),
            ComparisonType.MustBeGreaterThan
        );
    }
    else if (!posePassed)
    {
        rejection = RejectionReason.Create(
            "Pose Asymmetry", 
            measurement.PoseAsymmetryPercent,
            rules.MinSymmetryPercent,
            rules.ToStandard(),
            ComparisonType.MustBeGreaterThan
        );
    }
    
    return new SymmetryCompliance(
        Passed: illuminationPassed && posePassed,
        IlluminationAsymmetryPercent: measurement.IlluminationAsymmetryPercent,
        PoseAsymmetryPercent: measurement.PoseAsymmetryPercent,
        RequiredThreshold: rules.MinSymmetryPercent,
        Rejection: rejection
    );
}
```

## Integration with Processing Pipeline

### Fail-Fast Compliance Checking
```csharp
public static async Task<Result<ProcessingResult>> ProcessWithComplianceAsync(
    byte[] imageData,
    ProcessingOptions options,
    ComplianceRequirement requirement = ComplianceRequirement.Strict)
{
    return await imageData
        .AssessComplianceAsync(options.Standard)
        .Bind(compliance => 
            requirement == ComplianceRequirement.Strict && !compliance.IsCompliant
                ? Result.Failure<ComplianceAssessment>($"Image rejected: {compliance.Rejections.First().Message}")
                : Result.Success(compliance))
        .Bind(compliance => ProcessImageWithCompliance(imageData, options, compliance));
}

private static async Task<Result<ProcessingResult>> ProcessImageWithCompliance(
    byte[] imageData,
    ProcessingOptions options, 
    ComplianceAssessment compliance)
{
    var processingResult = await ProcessImageInternal(imageData, options);
    if (processingResult.IsFailure) return processingResult;
    
    // Add compliance metadata to result
    var enhancedMetadata = processingResult.Value.Metadata with
    {
        ComplianceAssessment = compliance,
        RequiresManualReview = !compliance.IsCompliant,
        QualityWarnings = compliance.Rejections.Select(r => r.Message).ToList()
    };
    
    return Result.Success(processingResult.Value with { Metadata = enhancedMetadata });
}
```

## Human-in-the-Loop Integration

### Compliance Coaching
```csharp
public static void ShowComplianceCoaching(ComplianceAssessment assessment)
{
    if (assessment.IsCompliant)
    {
        AnsiConsole.MarkupLine("[green]✅ Excellent! This image meets all compliance requirements.[/]");
        return;
    }
    
    AnsiConsole.MarkupLine($"[yellow]📋 {assessment.Rejections.Count} compliance issues found. Here's how to improve:[/]");
    AnsiConsole.WriteLine();
    
    foreach (var rejection in assessment.Rejections)
    {
        AnsiConsole.MarkupLine($"[red]❌ {rejection.Message}[/]");
        
        var coaching = GetComplianceCoaching(rejection.Metric, rejection.ActualValue, rejection.RequiredValue);
        foreach (var tip in coaching)
        {
            AnsiConsole.MarkupLine($"[blue]   💡 {tip}[/]");
        }
        AnsiConsole.WriteLine();
    }
}

private static List<string> GetComplianceCoaching(string metric, float actual, float required)
{
    return metric switch
    {
        "Illumination Asymmetry" => new List<string>
        {
            "Use even lighting from both sides of the face",
            "Avoid harsh shadows on one side",
            "Position subject directly facing the light source",
            $"Current: {actual:F1}% → Target: ≥{required:F0}%"
        },
        "Sharpness" => new List<string>
        {
            "Use faster shutter speed to reduce motion blur",
            "Ensure proper focus on the eyes",
            "Use a tripod to eliminate camera shake",
            $"Current: {actual:F1}% → Target: ≥{required:F0}%"
        },
        "Head Size" => new List<string>
        {
            "Move closer to subject or zoom in",
            "Ensure head fills appropriate portion of frame",
            "Check framing guidelines for your ID standard",
            $"Current: {actual:F1}% → Target: {required:F0}%"
        },
        _ => new List<string> { "Review general photography guidelines" }
    };
}
```

## Benefits of Compliance-First System

### 1. Crystal Clear Feedback
- **Before**: "Facial symmetry score 49.4% is below acceptable threshold"
- **After**: "REJECTED: 49% illumination asymmetry < 50% PIV minimum"

### 2. Standard-Specific Requirements
- **PIV**: 60% symmetry minimum
- **ICAO**: 70% symmetry minimum  
- **Custom**: Organization-defined thresholds

### 3. Actionable Improvement Guidance
- Specific coaching for each violation
- Visual examples of good vs bad
- Camera settings recommendations

### 4. Professional Integration
- Fail-fast processing for non-compliant images
- Manual review queue for borderline cases  
- Compliance metadata in all results

### 5. No More Confusion
- No combined scores to interpret
- Clear pass/fail per metric
- Specific thresholds for each standard

This architecture transforms FaceOFFx from a confusing quality scoring system into a professional compliance validation platform suitable for government, enterprise, and commercial applications.
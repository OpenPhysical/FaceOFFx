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

using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FaceOFFx.Core.Domain.Transformations;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality;

/// <summary>
/// Compliance rules for a specific standard
/// </summary>
[PublicAPI]
public record ComplianceRules(
    string Name,
    float MinSymmetryPercent,
    float MinSharpnessPercent,
    float MinHeadSizePercent,
    float MaxHeadSizePercent,
    float MinIpdPixels,
    float MaxIpdPixels,
    ImageDimensions ExpectedDimensions)
{
    /// <summary>
    /// Converts compliance rules to a Standard record
    /// </summary>
    public Standard ToStandard() => new(Name, Name, $"{Name} compliance standard");
}

/// <summary>
/// Dual-threshold compliance rules for input validation vs output validation
/// </summary>
[PublicAPI]
public record DualComplianceRules(
    string Name,
    ComplianceRules InputValidation,
    ComplianceRules OutputValidation,
    ImageDimensions ExpectedDimensions)
{
    /// <summary>
    /// Gets rules appropriate for the specified mode
    /// </summary>
    public ComplianceRules GetRulesForMode(bool isInputValidation) =>
        isInputValidation ? InputValidation : OutputValidation;
}

/// <summary>
/// PIV landmark-quality scoring thresholds. Anatomical and acquisition verification use separate evidence.
/// </summary>
[PublicAPI]
public static class ComplianceStandards
{
    /// <summary>
    /// All supported dual compliance standards with input/output validation
    /// </summary>
    public static readonly IReadOnlyDictionary<string, DualComplianceRules> DualStandards = new Dictionary<string, DualComplianceRules>
    {
        ["PIV"] = new DualComplianceRules(
            Name: "PIV",
            InputValidation: new ComplianceRules(
                Name: "PIV-Input",
                MinSymmetryPercent: 40f,     // Lenient for input photos
                MinSharpnessPercent: 15f,    // Lenient for input photos
                MinHeadSizePercent: 25f,     // Wider range for input
                MaxHeadSizePercent: 85f,
                MinIpdPixels: 60f,           // Wider range for varied input scales
                MaxIpdPixels: 200f,
                ExpectedDimensions: new ImageDimensions(480, 640)
            ),
            OutputValidation: new ComplianceRules(
                Name: "PIV-Output",
                MinSymmetryPercent: 60f,     // Engineering quality thresholds
                MinSharpnessPercent: 25f,
                MinHeadSizePercent: 35f,
                MaxHeadSizePercent: 80f,
                MinIpdPixels: 90f,
                MaxIpdPixels: 120f,
                ExpectedDimensions: new ImageDimensions(480, 640)
            ),
            ExpectedDimensions: new ImageDimensions(480, 640)
        )
    };

    /// <summary>
    /// All supported compliance standards (legacy single-threshold)
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ComplianceRules> Standards = new Dictionary<string, ComplianceRules>
    {
        ["PIV"] = new ComplianceRules(
            Name: "PIV",
            MinSymmetryPercent: 60f,
            MinSharpnessPercent: 25f,
            MinHeadSizePercent: 35f,
            MaxHeadSizePercent: 80f,
            MinIpdPixels: 90f,
            MaxIpdPixels: 120f,
            ExpectedDimensions: new ImageDimensions(480, 640)
        )
    };

    /// <summary>
    /// Gets dual compliance rules for a standard by name
    /// </summary>
    /// <param name="standardName">Name of the standard (case-insensitive)</param>
    /// <returns>Dual compliance rules if found, null otherwise</returns>
    public static DualComplianceRules? GetDualRules(string standardName)
    {
        return DualStandards.TryGetValue(standardName.ToUpperInvariant(), out var rules) ? rules : null;
    }

    /// <summary>
    /// Gets compliance rules for a standard and mode
    /// </summary>
    /// <param name="standardName">Name of the standard (case-insensitive)</param>
    /// <param name="isInputValidation">True for input validation, false for output validation</param>
    /// <returns>Compliance rules if found, null otherwise</returns>
    public static ComplianceRules? GetRulesForMode(string standardName, bool isInputValidation)
    {
        var dualRules = GetDualRules(standardName);
        return dualRules?.GetRulesForMode(isInputValidation);
    }

    /// <summary>
    /// Gets compliance rules for a standard by name (legacy single-threshold)
    /// </summary>
    /// <param name="standardName">Name of the standard (case-insensitive)</param>
    /// <returns>Compliance rules if found, null otherwise</returns>
    public static ComplianceRules? GetRules(string standardName)
    {
        return Standards.TryGetValue(standardName.ToUpperInvariant(), out var rules) ? rules : null;
    }

    /// <summary>
    /// Gets compliance rules for a standard with fallback to PIV
    /// </summary>
    /// <param name="standardName">Name of the standard (case-insensitive)</param>
    /// <returns>Compliance rules (defaults to PIV if not found)</returns>
    public static ComplianceRules GetRulesOrDefault(string standardName)
    {
        return GetRules(standardName) ?? Standards["PIV"];
    }

    /// <summary>
    /// Loads custom compliance rules from a JSON file
    /// </summary>
    /// <param name="filePath">Path to JSON file containing compliance rules</param>
    /// <returns>Custom compliance rules</returns>
    public static ComplianceRules LoadCustomStandard(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var rules = JsonSerializer.Deserialize<ComplianceRules>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        return rules ?? throw new InvalidDataException($"Invalid compliance rules file: {filePath}");
    }

    /// <summary>
    /// Validates that a standard name is supported
    /// </summary>
    /// <param name="standardName">Standard name to validate</param>
    /// <returns>True if standard is supported</returns>
    public static bool IsSupported(string standardName)
    {
        return Standards.ContainsKey(standardName.ToUpperInvariant());
    }

    /// <summary>
    /// Gets all supported standard names
    /// </summary>
    /// <returns>Collection of supported standard names</returns>
    public static IEnumerable<string> GetSupportedStandards()
    {
        return Standards.Keys;
    }
}

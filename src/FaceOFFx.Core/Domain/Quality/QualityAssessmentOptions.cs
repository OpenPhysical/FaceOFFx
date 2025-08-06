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

using System;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Transformations;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality;

/// <summary>
/// Options for ISO/IEC 19794-5 quality assessment
/// </summary>
[PublicAPI]
public record QualityAssessmentOptions
{
    /// <summary>
    /// The standard to assess compliance against
    /// </summary>
    public Iso19794Standard Standard { get; init; } = Iso19794Standard.Piv;
    
    /// <summary>
    /// Whether to enforce compliance (fail processing if non-compliant)
    /// </summary>
    public bool EnforceCompliance { get; init; } = false;
    
    /// <summary>
    /// Minimum overall quality threshold (0-1)
    /// </summary>
    public float MinQualityThreshold { get; init; } = 0.7f;
    
    /// <summary>
    /// Enable parallel assessment of different quality metrics
    /// </summary>
    public bool EnableParallelAssessment { get; init; } = true;
    
    /// <summary>
    /// Maximum rotation correction to apply (degrees)
    /// </summary>
    public float MaxRotationCorrection { get; init; } = 15f;
    
    /// <summary>
    /// Strict compliance preset - high quality threshold with enforcement
    /// </summary>
    public static QualityAssessmentOptions Strict => new() 
    { 
        EnforceCompliance = true,
        MinQualityThreshold = 0.85f,
        Standard = Iso19794Standard.Piv
    };
    
    /// <summary>
    /// Lenient compliance preset - lower threshold without enforcement
    /// </summary>
    public static QualityAssessmentOptions Lenient => new()
    {
        EnforceCompliance = false,
        MinQualityThreshold = 0.6f,
        Standard = Iso19794Standard.Piv
    };
    
    /// <summary>
    /// TWIC-specific preset
    /// </summary>
    public static QualityAssessmentOptions Twic => new()
    {
        EnforceCompliance = true,
        MinQualityThreshold = 0.75f,
        Standard = Iso19794Standard.Twic
    };
    
    /// <summary>
    /// ICAO passport preset
    /// </summary>
    public static QualityAssessmentOptions Icao => new()
    {
        EnforceCompliance = true,
        MinQualityThreshold = 0.8f,
        Standard = Iso19794Standard.Icao
    };
    
    /// <summary>
    /// Creates options for a specific standard
    /// </summary>
    public static QualityAssessmentOptions ForStandard(string standard) => standard?.ToLowerInvariant() switch
    {
        "piv" => new() { Standard = Iso19794Standard.Piv },
        "twic" => Twic,
        "icao" or "passport" => Icao,
        "cac" => new() { Standard = Iso19794Standard.Cac },
        _ => new() { Standard = Iso19794Standard.Piv }
    };
    
    /// <summary>
    /// Validates the assessment options
    /// </summary>
    public Result Validate()
    {
        if (MinQualityThreshold < 0f || MinQualityThreshold > 1f)
        {
            return Result.Failure($"MinQualityThreshold must be between 0 and 1, but was {MinQualityThreshold}");
        }
        
        if (MaxRotationCorrection < 0f || MaxRotationCorrection > 180f)
        {
            return Result.Failure($"MaxRotationCorrection must be between 0 and 180 degrees, but was {MaxRotationCorrection}");
        }
        
        if (Standard == null)
        {
            return Result.Failure("Assessment standard cannot be null");
        }
        
        return Result.Success();
    }
}

/// <summary>
/// ISO/IEC 19794-5 standard variants
/// </summary>
[PublicAPI]
public record Iso19794Standard
{
    /// <summary>
    /// The name of the standard (e.g., "PIV", "TWIC", "ICAO")
    /// </summary>
    public string Name { get; }
    
    /// <summary>
    /// Expected image dimensions in pixels for this standard
    /// </summary>
    public ImageDimensions ExpectedDimensions { get; }
    
    /// <summary>
    /// Minimum acceptable head width as ratio of image width
    /// </summary>
    public float MinHeadWidthRatio { get; }
    
    /// <summary>
    /// Maximum acceptable head width as ratio of image width
    /// </summary>
    public float MaxHeadWidthRatio { get; }
    
    /// <summary>
    /// Minimum inter-pupillary distance in pixels
    /// </summary>
    public float MinInterPupillaryDistance { get; }
    
    /// <summary>
    /// Maximum inter-pupillary distance in pixels
    /// </summary>
    public float MaxInterPupillaryDistance { get; }
    
    private Iso19794Standard(
        string name,
        ImageDimensions expectedDimensions,
        float minHeadWidthRatio,
        float maxHeadWidthRatio,
        float minIpd,
        float maxIpd)
    {
        Name = name;
        ExpectedDimensions = expectedDimensions;
        MinHeadWidthRatio = minHeadWidthRatio;
        MaxHeadWidthRatio = maxHeadWidthRatio;
        MinInterPupillaryDistance = minIpd;
        MaxInterPupillaryDistance = maxIpd;
    }
    
    /// <summary>
    /// PIV (Personal Identity Verification) standard
    /// </summary>
    public static Iso19794Standard Piv { get; } = new(
        "PIV",
        new ImageDimensions(420, 560),
        minHeadWidthRatio: 0.5f,
        maxHeadWidthRatio: 0.75f,
        minIpd: 90f,
        maxIpd: 120f
    );
    
    /// <summary>
    /// TWIC (Transportation Worker Identification Credential) standard
    /// </summary>
    public static Iso19794Standard Twic { get; } = new(
        "TWIC",
        new ImageDimensions(420, 560),
        minHeadWidthRatio: 0.5f,
        maxHeadWidthRatio: 0.75f,
        minIpd: 90f,
        maxIpd: 120f
    );
    
    /// <summary>
    /// ICAO (International Civil Aviation Organization) passport standard
    /// </summary>
    public static Iso19794Standard Icao { get; } = new(
        "ICAO",
        new ImageDimensions(413, 531), // 35mm x 45mm at 300 DPI
        minHeadWidthRatio: 0.5f,
        maxHeadWidthRatio: 0.75f,
        minIpd: 90f,
        maxIpd: 120f
    );
    
    /// <summary>
    /// CAC (Common Access Card) standard
    /// </summary>
    public static Iso19794Standard Cac { get; } = new(
        "CAC",
        new ImageDimensions(420, 560),
        minHeadWidthRatio: 0.5f,
        maxHeadWidthRatio: 0.75f,
        minIpd: 90f,
        maxIpd: 120f
    );
}
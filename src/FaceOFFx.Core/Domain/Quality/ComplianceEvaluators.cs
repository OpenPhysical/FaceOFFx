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
using System.Collections.Generic;
using System.Linq;
using CSharpFunctionalExtensions;
using JetBrains.Annotations;

namespace FaceOFFx.Core.Domain.Quality;

/// <summary>
/// Evaluators that convert raw measurements to compliance assessments
/// </summary>
[PublicAPI]
public static class ComplianceEvaluators
{
    /// <summary>
    /// Evaluates symmetry compliance against standard rules
    /// </summary>
    public static SymmetryCompliance EvaluateSymmetryCompliance(
        SymmetryMeasurement measurement,
        ComplianceRules rules,
        bool countsTowardsCompliance = true)
    {
        var standard = rules.ToStandard();
        var illuminationPassed = measurement.IlluminationAsymmetryPercent >= rules.MinSymmetryPercent;
        var posePassed = measurement.PoseAsymmetryPercent >= rules.MinSymmetryPercent;
        
        var rejection = Maybe<RejectionReason>.None;
        if (countsTowardsCompliance && !illuminationPassed)
        {
            rejection = Maybe<RejectionReason>.From(RejectionReason.Create(
                "Illumination Asymmetry",
                measurement.IlluminationAsymmetryPercent,
                rules.MinSymmetryPercent,
                standard,
                ComparisonType.MustBeGreaterThan,
                MetricUnit.Percentage
            ));
        }
        else if (countsTowardsCompliance && !posePassed)
        {
            rejection = Maybe<RejectionReason>.From(RejectionReason.Create(
                "Pose Asymmetry",
                measurement.PoseAsymmetryPercent,
                rules.MinSymmetryPercent,
                standard,
                ComparisonType.MustBeGreaterThan,
                MetricUnit.Percentage
            ));
        }
        
        return new SymmetryCompliance(
            Passed: illuminationPassed && posePassed,
            IlluminationAsymmetryPercent: measurement.IlluminationAsymmetryPercent,
            PoseAsymmetryPercent: measurement.PoseAsymmetryPercent,
            RequiredThreshold: rules.MinSymmetryPercent,
            Rejection: rejection,
            CountsTowardsCompliance: countsTowardsCompliance
        );
    }

    /// <summary>
    /// Evaluates sharpness compliance against standard rules
    /// </summary>
    public static SharpnessCompliance EvaluateSharpnessCompliance(
        SharpnessMeasurement measurement,
        ComplianceRules rules)
    {
        var standard = rules.ToStandard();
        var passed = measurement.OverallSharpnessPercent >= rules.MinSharpnessPercent;
        
        var rejection = Maybe<RejectionReason>.None;
        if (!passed)
        {
            rejection = Maybe<RejectionReason>.From(RejectionReason.Create(
                "Sharpness",
                measurement.OverallSharpnessPercent,
                rules.MinSharpnessPercent,
                standard,
                ComparisonType.MustBeGreaterThan,
                MetricUnit.Percentage
            ));
        }
        
        return new SharpnessCompliance(
            Passed: passed,
            OverallSharpnessPercent: measurement.OverallSharpnessPercent,
            RequiredThreshold: rules.MinSharpnessPercent,
            RegionalScores: measurement.RegionalScores,
            Rejection: rejection
        );
    }

    /// <summary>
    /// Evaluates head size compliance against standard rules
    /// </summary>
    public static HeadSizeCompliance EvaluateHeadSizeCompliance(
        float headSizePercent,
        ComplianceRules rules)
    {
        var standard = rules.ToStandard();
        var passed = headSizePercent >= rules.MinHeadSizePercent && headSizePercent <= rules.MaxHeadSizePercent;
        
        var rejection = Maybe<RejectionReason>.None;
        if (headSizePercent < rules.MinHeadSizePercent)
        {
            rejection = Maybe<RejectionReason>.From(RejectionReason.Create(
                "Head Size",
                headSizePercent,
                rules.MinHeadSizePercent,
                standard,
                ComparisonType.MustBeGreaterThan,
                MetricUnit.Percentage
            ));
        }
        else if (headSizePercent > rules.MaxHeadSizePercent)
        {
            rejection = Maybe<RejectionReason>.From(RejectionReason.Create(
                "Head Size",
                headSizePercent,
                rules.MaxHeadSizePercent,
                standard,
                ComparisonType.MustBeLessThan,
                MetricUnit.Percentage
            ));
        }
        
        return new HeadSizeCompliance(
            Passed: passed,
            SizePercent: headSizePercent,
            MinPercent: rules.MinHeadSizePercent,
            MaxPercent: rules.MaxHeadSizePercent,
            Rejection: rejection
        );
    }

    /// <summary>
    /// Evaluates centering compliance (simplified - assumes adequate centering for now)
    /// </summary>
    public static CenteringCompliance EvaluateCenteringCompliance(
        float centeringPercent,
        ComplianceRules rules)
    {
        var standard = rules.ToStandard();
        const float requiredThreshold = 80f; // 80% centering required
        var passed = centeringPercent >= requiredThreshold;
        
        var rejection = Maybe<RejectionReason>.None;
        if (!passed)
        {
            rejection = Maybe<RejectionReason>.From(RejectionReason.Create(
                "Centering",
                centeringPercent,
                requiredThreshold,
                standard,
                ComparisonType.MustBeGreaterThan,
                MetricUnit.Percentage
            ));
        }
        
        return new CenteringCompliance(
            Passed: passed,
            CenteringPercent: centeringPercent,
            RequiredThreshold: requiredThreshold,
            Rejection: rejection
        );
    }

    /// <summary>
    /// Evaluates IPD compliance against standard rules
    /// </summary>
    public static IpdCompliance EvaluateIpdCompliance(
        float ipdPixels,
        ComplianceRules rules,
        bool countsTowardsCompliance = true)
    {
        var standard = rules.ToStandard();
        var passed = ipdPixels >= rules.MinIpdPixels && ipdPixels <= rules.MaxIpdPixels;

        if (!countsTowardsCompliance)
        {
            return new IpdCompliance(
                Passed: true,
                DistancePixels: ipdPixels,
                MinPixels: rules.MinIpdPixels,
                MaxPixels: rules.MaxIpdPixels,
                Rejection: Maybe<RejectionReason>.None,
                CountsTowardsCompliance: false);
        }
        
        var rejection = Maybe<RejectionReason>.None;
        if (ipdPixels < rules.MinIpdPixels)
        {
            rejection = Maybe<RejectionReason>.From(RejectionReason.Create(
                "Inter-Pupillary Distance",
                ipdPixels,
                rules.MinIpdPixels,
                standard,
                ComparisonType.MustBeGreaterThan,
                MetricUnit.Pixels
            ));
        }
        else if (ipdPixels > rules.MaxIpdPixels)
        {
            rejection = Maybe<RejectionReason>.From(RejectionReason.Create(
                "Inter-Pupillary Distance",
                ipdPixels,
                rules.MaxIpdPixels,
                standard,
                ComparisonType.MustBeLessThan,
                MetricUnit.Pixels
            ));
        }
        
        return new IpdCompliance(
            Passed: passed,
            DistancePixels: ipdPixels,
            MinPixels: rules.MinIpdPixels,
            MaxPixels: rules.MaxIpdPixels,
            Rejection: rejection,
            CountsTowardsCompliance: true
        );
    }

    /// <summary>
    /// Evaluates geometry compliance combining all geometric metrics
    /// </summary>
    public static GeometryCompliance EvaluateGeometryCompliance(
        GeometryMeasurement measurement,
        ComplianceRules rules,
        bool ipdCountsTowardsCompliance = true)
    {
        var headSizeCompliance = EvaluateHeadSizeCompliance(measurement.HeadSizePercent, rules);
        var centeringCompliance = EvaluateCenteringCompliance(measurement.CenteringPercent, rules);
        var ipdCompliance = EvaluateIpdCompliance(
            measurement.IpdPixels,
            rules,
            ipdCountsTowardsCompliance);
        
        var allPassed = headSizeCompliance.Passed
            && centeringCompliance.Passed
            && (!ipdCompliance.CountsTowardsCompliance || ipdCompliance.Passed);
        
        // Use the first rejection found as the primary geometry rejection
        var rejection = headSizeCompliance.Rejection.HasValue
            ? headSizeCompliance.Rejection
            : centeringCompliance.Rejection.HasValue
                ? centeringCompliance.Rejection
                : ipdCompliance.Rejection;
        
        return new GeometryCompliance(
            Passed: allPassed,
            HeadSize: headSizeCompliance,
            Centering: centeringCompliance,
            InterPupillaryDistance: ipdCompliance,
            Rejection: rejection
        );
    }

    /// <summary>
    /// Performs complete compliance assessment from raw measurements
    /// </summary>
    public static ComplianceAssessment AssessCompliance(
        SymmetryMeasurement symmetryMeasurement,
        SharpnessMeasurement sharpnessMeasurement,
        GeometryMeasurement geometryMeasurement,
        ComplianceRules rules,
        bool symmetryCountsTowardsCompliance = true,
        bool ipdCountsTowardsCompliance = true)
    {
        var symmetryCompliance = EvaluateSymmetryCompliance(
            symmetryMeasurement,
            rules,
            symmetryCountsTowardsCompliance);
        var sharpnessCompliance = EvaluateSharpnessCompliance(sharpnessMeasurement, rules);
        var geometryCompliance = EvaluateGeometryCompliance(
            geometryMeasurement,
            rules,
            ipdCountsTowardsCompliance);
        
        // Collect all rejections
        var rejections = new List<RejectionReason>();
        if (symmetryCompliance.Rejection.HasValue) rejections.Add(symmetryCompliance.Rejection.Value);
        if (sharpnessCompliance.Rejection.HasValue) rejections.Add(sharpnessCompliance.Rejection.Value);
        if (geometryCompliance.Rejection.HasValue) rejections.Add(geometryCompliance.Rejection.Value);
        
        return new ComplianceAssessment(
            symmetryCompliance,
            sharpnessCompliance,
            geometryCompliance,
            rules.ToStandard(),
            rejections
        );
    }
}

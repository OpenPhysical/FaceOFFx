using System;
using System.Collections.Immutable;
using System.Linq;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using JetBrains.Annotations;
using SixLabors.ImageSharp;

namespace FaceOFFx.Core.Domain.Detection;

/// <summary>
/// Represents an elliptical face region for analysis
/// </summary>
[PublicAPI]
public record FaceEllipse(
    Point2D Center,
    float Width,
    float Height,
    float AngleRadians)
{
    /// <summary>
    /// Checks if a point is inside the ellipse
    /// </summary>
    public bool Contains(Point2D point)
    {
        // Translate to ellipse center
        var dx = point.X - Center.X;
        var dy = point.Y - Center.Y;
        
        // Rotate by negative angle to align with axes
        var cos = MathF.Cos(-AngleRadians);
        var sin = MathF.Sin(-AngleRadians);
        var rx = dx * cos - dy * sin;
        var ry = dx * sin + dy * cos;
        
        // Check ellipse equation
        var a = Width / 2f;
        var b = Height / 2f;
        return (rx * rx) / (a * a) + (ry * ry) / (b * b) <= 1f;
    }
    
    /// <summary>
    /// Gets the axis-aligned bounding box of the rotated ellipse
    /// </summary>
    public Rectangle GetBoundingBox()
    {
        var cos = MathF.Abs(MathF.Cos(AngleRadians));
        var sin = MathF.Abs(MathF.Sin(AngleRadians));
        var a = Width / 2f;
        var b = Height / 2f;
        
        var halfWidth = a * cos + b * sin;
        var halfHeight = a * sin + b * cos;
        
        return new Rectangle(
            (int)(Center.X - halfWidth),
            (int)(Center.Y - halfHeight),
            (int)(2 * halfWidth),
            (int)(2 * halfHeight));
    }
}

/// <summary>
/// Functional operations for calculating face ellipses from landmarks
/// </summary>
[PublicAPI]
public static class FaceEllipseCalculator
{
    /// <summary>
    /// Calculates an elliptical face region from 68-point landmarks using jaw-based approach
    /// </summary>
    public static Result<FaceEllipse> CalculateFromLandmarks(FaceLandmarks68 landmarks)
    {
        return CreateFaceOvalFromJaw(landmarks);
    }
    
    /// <summary>
    /// Creates a proper face oval from jaw landmarks - always taller than wide
    /// </summary>
    private static Result<FaceEllipse> CreateFaceOvalFromJaw(FaceLandmarks68 landmarks)
    {
        if (landmarks.Points.Count != 68)
        {
            return Result.Failure<FaceEllipse>("Invalid landmark count - must be exactly 68 points");
        }
        
        // Extract jaw line points (0-16) - these define the face boundary
        var jawPoints = landmarks.Points.Take(17).ToList();
        
        // Find jaw bounds
        var minX = jawPoints.Min(p => p.X);
        var maxX = jawPoints.Max(p => p.X);
        var minY = jawPoints.Min(p => p.Y);
        var maxY = jawPoints.Max(p => p.Y);
        
        // Calculate jaw width and vertical span
        var jawWidth = maxX - minX;
        var jawHeight = maxY - minY;
        
        // Get eye positions for height calculation
        var leftEye = landmarks.LeftEyeCenter;
        var rightEye = landmarks.RightEyeCenter;
        var eyeY = (leftEye.Y + rightEye.Y) / 2f;
        
        // Get forehead estimate (extrapolate above eyebrows)
        var eyebrowPoints = landmarks.Points.Skip(17).Take(10).ToList(); // Points 17-26
        var eyebrowTop = eyebrowPoints.Min(p => p.Y);
        var foreheadY = eyebrowTop - (jawHeight * 0.3f);
        
        // Calculate face center
        var centerX = (minX + maxX) / 2f;
        var centerY = (foreheadY + maxY) / 2f;
        
        // Face oval dimensions - smaller to focus on core facial features
        var faceWidth = jawWidth * 0.9f; // Reduce padding to 90% of jaw width
        var totalHeight = maxY - foreheadY;
        var faceHeight = Math.Max(totalHeight * 0.85f, faceWidth * 1.3f); // 85% of calculated height, ensure height >= 1.3x width
        
        // Force upright orientation (angle = 0)
        var angle = 0f;
        
        return Result.Success(new FaceEllipse(
            new Point2D(centerX, centerY),
            faceWidth,
            faceHeight,
            angle
        ));
    }
}
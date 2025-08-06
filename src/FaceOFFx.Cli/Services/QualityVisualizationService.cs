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

using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Quality;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing.Processors.Drawing;

namespace FaceOFFx.Cli.Services;

/// <summary>
/// Provides visualization services for quality assessment results.
/// Creates visual overlays showing quality issues and scores on images.
/// </summary>
public static class QualityVisualizationService
{
    /// <summary>
    /// Creates a quality assessment overlay on the provided image.
    /// </summary>
    /// <param name="sourceImage">The original image to overlay quality information on.</param>
    /// <param name="assessment">The quality assessment results to visualize.</param>
    /// <param name="showScores">Whether to show numeric scores on the overlay.</param>
    /// <param name="showViolations">Whether to highlight violation areas.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    /// <returns>A Result containing the annotated image or an error message.</returns>
    public static Result<Image<Rgba32>> CreateQualityOverlay(
        Image<Rgba32> sourceImage,
        Iso19794Assessment assessment,
        bool showScores = true,
        bool showViolations = true,
        ILogger? logger = null)
    {
        logger?.LogDebug("Starting quality overlay creation");

        try
        {
            var annotatedImage = sourceImage.Clone(ctx =>
            {
                // Draw quality score panel
                if (showScores)
                {
                    DrawQualityScorePanel(ctx, sourceImage.Width, sourceImage.Height, assessment, logger);
                }

                // Highlight quality issues
                if (showViolations && assessment.Violations.Any())
                {
                    DrawViolationIndicators(ctx, sourceImage.Width, sourceImage.Height, assessment, logger);
                }

                // Draw overall compliance status
                DrawComplianceStatus(ctx, sourceImage.Width, sourceImage.Height, assessment, logger);
            });

            logger?.LogDebug("Successfully created quality overlay");
            return Result.Success(annotatedImage);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to create quality overlay");
            return Result.Failure<Image<Rgba32>>($"Failed to create quality overlay: {ex.Message}");
        }
    }

    private static void DrawQualityScorePanel(
        IImageProcessingContext ctx, 
        int imageWidth, 
        int imageHeight,
        Iso19794Assessment assessment,
        ILogger? logger)
    {
        logger?.LogDebug("Drawing quality score panel");

        // Create semi-transparent background panel
        var panelWidth = 250;
        var panelHeight = 200;
        var panelX = imageWidth - panelWidth - 10;
        var panelY = 10;

        var panelRect = new RectangleF(panelX, panelY, panelWidth, panelHeight);
        ctx.Fill(Color.FromRgba(0, 0, 0, 180), panelRect);

        // Draw scores as colored bars
        var barX = panelX + 10;
        var barY = panelY + 10;
        var barWidth = panelWidth - 20;
        var barHeight = 20;
        var barSpacing = 25;

        // Overall score
        DrawScoreBar(ctx, "Overall", assessment.Overall.Value, barX, barY, barWidth, barHeight);
        barY += barSpacing;

        // Symmetry score
        DrawScoreBar(ctx, "Symmetry", assessment.Symmetry.Overall.Value, barX, barY, barWidth, barHeight);
        barY += barSpacing;

        // Sharpness score
        DrawScoreBar(ctx, "Sharpness", assessment.Sharpness.Overall.Value, barX, barY, barWidth, barHeight);
        barY += barSpacing;

        // Geometry score
        DrawScoreBar(ctx, "Geometry", assessment.Geometry.Overall.Value, barX, barY, barWidth, barHeight);
        barY += barSpacing;

        // Sub-scores
        var subBarWidth = barWidth * 0.8f;
        var subBarX = barX + barWidth * 0.2f;

        // Illumination
        DrawScoreBar(ctx, "Illumination", assessment.Symmetry.Illumination.Value, subBarX, barY, subBarWidth, 15, 0.6f);
        barY += 20;

        // Pose
        DrawScoreBar(ctx, "Pose", assessment.Symmetry.Pose.Value, subBarX, barY, subBarWidth, 15, 0.6f);

        logger?.LogDebug("Quality score panel drawn");
    }

    private static void DrawScoreBar(
        IImageProcessingContext ctx,
        string label,
        float score,
        float x,
        float y,
        float width,
        float height,
        float opacity = 1.0f)
    {
        // Background bar
        var bgColor = Color.FromRgba(60, 60, 60, (byte)(255 * opacity));
        ctx.Fill(bgColor, new RectangleF(x, y, width, height));

        // Score bar
        var scoreWidth = width * score;
        var scoreColor = GetScoreColor(score, opacity);
        ctx.Fill(scoreColor, new RectangleF(x, y, scoreWidth, height));

        // Border
        ctx.Draw(Color.FromRgba(200, 200, 200, (byte)(255 * opacity)), 1, new RectangleF(x, y, width, height));
    }

    private static Color GetScoreColor(float score, float opacity = 1.0f)
    {
        byte alpha = (byte)(255 * opacity);
        
        if (score >= 0.8f)
            return Color.FromRgba(0, 200, 0, alpha); // Green
        else if (score >= 0.6f)
            return Color.FromRgba(255, 165, 0, alpha); // Orange
        else
            return Color.FromRgba(255, 0, 0, alpha); // Red
    }

    private static void DrawViolationIndicators(
        IImageProcessingContext ctx,
        int imageWidth,
        int imageHeight,
        Iso19794Assessment assessment,
        ILogger? logger)
    {
        logger?.LogDebug("Drawing violation indicators");

        // Group violations by category
        var violationsByCategory = assessment.Violations.GroupBy(v => v.Category);

        foreach (var group in violationsByCategory)
        {
            var category = group.Key;
            var severity = group.Max(v => v.Severity);
            
            // Draw indicator based on category
            switch (category.ToLower())
            {
                case "sharpness":
                    DrawSharpnessIndicator(ctx, imageWidth, imageHeight, severity);
                    break;
                case "illumination":
                    DrawIlluminationIndicator(ctx, imageWidth, imageHeight, severity);
                    break;
                case "geometry":
                case "centering":
                case "head_size":
                    DrawGeometryIndicator(ctx, imageWidth, imageHeight, severity);
                    break;
                case "pose":
                    DrawPoseIndicator(ctx, imageWidth, imageHeight, severity);
                    break;
            }
        }

        logger?.LogDebug("Violation indicators drawn");
    }

    private static void DrawSharpnessIndicator(
        IImageProcessingContext ctx,
        int imageWidth,
        int imageHeight,
        ViolationSeverity severity)
    {
        // Draw blur effect overlay on edges
        var color = GetViolationColor(severity, 0.3f);
        var thickness = severity == ViolationSeverity.Critical ? 8 : 4;
        
        // Top and bottom edge blur indicators
        ctx.Fill(color, new RectangleF(0, 0, imageWidth, thickness));
        ctx.Fill(color, new RectangleF(0, imageHeight - thickness, imageWidth, thickness));
        
        // Left and right edge blur indicators
        ctx.Fill(color, new RectangleF(0, 0, thickness, imageHeight));
        ctx.Fill(color, new RectangleF(imageWidth - thickness, 0, thickness, imageHeight));
    }

    private static void DrawIlluminationIndicator(
        IImageProcessingContext ctx,
        int imageWidth,
        int imageHeight,
        ViolationSeverity severity)
    {
        // Draw gradient overlay to indicate uneven illumination
        var color = GetViolationColor(severity, 0.2f);
        
        // Create diagonal gradient pattern
        for (int i = 0; i < 5; i++)
        {
            var x = imageWidth * i / 5f;
            var width = imageWidth / 10f;
            ctx.Fill(color, new RectangleF(x, 0, width, imageHeight));
        }
    }

    private static void DrawGeometryIndicator(
        IImageProcessingContext ctx,
        int imageWidth,
        int imageHeight,
        ViolationSeverity severity)
    {
        // Draw center crosshair and boundary box
        var color = GetViolationColor(severity, 0.8f);
        var centerX = imageWidth / 2f;
        var centerY = imageHeight / 2f;
        
        // Crosshair
        var horizontalPath = new SixLabors.ImageSharp.Drawing.Path(new LinearLineSegment(
            new PointF(centerX - 20, centerY), 
            new PointF(centerX + 20, centerY)));
        ctx.Draw(color, 2, horizontalPath);
        
        var verticalPath = new SixLabors.ImageSharp.Drawing.Path(new LinearLineSegment(
            new PointF(centerX, centerY - 20), 
            new PointF(centerX, centerY + 20)));
        ctx.Draw(color, 2, verticalPath);
        
        // Expected face region boundary
        var expectedWidth = imageWidth * 0.6f;
        var expectedHeight = imageHeight * 0.7f;
        var expectedX = (imageWidth - expectedWidth) / 2f;
        var expectedY = (imageHeight - expectedHeight) / 2f;
        
        var dashPattern = new float[] { 5, 5 };
        ctx.Draw(color, 2, new RectangleF(expectedX, expectedY, expectedWidth, expectedHeight));
    }

    private static void DrawPoseIndicator(
        IImageProcessingContext ctx,
        int imageWidth,
        int imageHeight,
        ViolationSeverity severity)
    {
        // Draw rotation indicator
        var color = GetViolationColor(severity, 0.7f);
        var centerX = imageWidth / 2f;
        var centerY = imageHeight * 0.3f;
        
        // Draw arc to indicate rotation
        var radius = 40;
        var startAngle = -30;
        var endAngle = 30;
        
        // Simple arc approximation with lines
        for (int angle = startAngle; angle <= endAngle; angle += 5)
        {
            var rad = angle * MathF.PI / 180f;
            var x = centerX + radius * MathF.Cos(rad);
            var y = centerY + radius * MathF.Sin(rad);
            
            if (angle > startAngle)
            {
                var prevRad = (angle - 5) * MathF.PI / 180f;
                var prevX = centerX + radius * MathF.Cos(prevRad);
                var prevY = centerY + radius * MathF.Sin(prevRad);
                var arcSegment = new SixLabors.ImageSharp.Drawing.Path(new LinearLineSegment(
                    new PointF(prevX, prevY), 
                    new PointF(x, y)));
                ctx.Draw(color, 2, arcSegment);
            }
        }
    }

    private static Color GetViolationColor(ViolationSeverity severity, float opacity = 1.0f)
    {
        byte alpha = (byte)(255 * opacity);
        
        return severity switch
        {
            ViolationSeverity.Critical => Color.FromRgba(255, 0, 0, alpha),
            ViolationSeverity.Moderate => Color.FromRgba(255, 165, 0, alpha),
            _ => Color.FromRgba(255, 255, 0, alpha)
        };
    }

    private static void DrawComplianceStatus(
        IImageProcessingContext ctx,
        int imageWidth,
        int imageHeight,
        Iso19794Assessment assessment,
        ILogger? logger)
    {
        logger?.LogDebug("Drawing compliance status");

        // Draw compliance badge
        var badgeSize = 80;
        var badgeX = 10;
        var badgeY = 10;
        
        if (assessment.IsCompliant)
        {
            // Green checkmark
            ctx.Fill(Color.FromRgba(0, 200, 0, 200), new EllipsePolygon(badgeX + badgeSize/2, badgeY + badgeSize/2, badgeSize/2));
            
            // Draw checkmark
            var checkPoints = new PointF[]
            {
                new PointF(badgeX + badgeSize * 0.3f, badgeY + badgeSize * 0.5f),
                new PointF(badgeX + badgeSize * 0.45f, badgeY + badgeSize * 0.65f),
                new PointF(badgeX + badgeSize * 0.7f, badgeY + badgeSize * 0.35f)
            };
            // Draw checkmark as two separate lines
            var line1 = new SixLabors.ImageSharp.Drawing.Path(new LinearLineSegment(
                checkPoints[0], checkPoints[1]));
            ctx.Draw(Color.White, 4, line1);
            
            var line2 = new SixLabors.ImageSharp.Drawing.Path(new LinearLineSegment(
                checkPoints[1], checkPoints[2]));
            ctx.Draw(Color.White, 4, line2);
        }
        else
        {
            // Red X
            ctx.Fill(Color.FromRgba(255, 0, 0, 200), new EllipsePolygon(badgeX + badgeSize/2, badgeY + badgeSize/2, badgeSize/2));
            
            // Draw X
            var margin = badgeSize * 0.3f;
            // Draw X line 1
            var xLine1 = new SixLabors.ImageSharp.Drawing.Path(new LinearLineSegment(
                new PointF(badgeX + margin, badgeY + margin), 
                new PointF(badgeX + badgeSize - margin, badgeY + badgeSize - margin)));
            ctx.Draw(Color.White, 4, xLine1);
            
            // Draw X line 2
            var xLine2 = new SixLabors.ImageSharp.Drawing.Path(new LinearLineSegment(
                new PointF(badgeX + badgeSize - margin, badgeY + margin), 
                new PointF(badgeX + margin, badgeY + badgeSize - margin)));
            ctx.Draw(Color.White, 4, xLine2);
        }

        logger?.LogDebug("Compliance status drawn");
    }
}
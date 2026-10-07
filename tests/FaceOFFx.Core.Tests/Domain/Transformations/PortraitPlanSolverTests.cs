using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

[TestFixture]
public class PortraitPlanSolverTests
{
    internal static FaceLandmarks68 Landmarks(float scale = 1, float centerX = 500, float centerY = 600)
    {
        var points = Enumerable.Repeat(Point2D.Zero, 68).ToArray();
        for (var index = 0; index <= 16; index++)
        {
            var angle = index * MathF.PI / 16;
            points[index] = new Point2D(500 - 200 * MathF.Cos(angle), 460 + 340 * MathF.Sin(angle));
        }
        for (var index = 17; index <= 21; index++) points[index] = new Point2D(350 + (index - 17) * 25, 475);
        for (var index = 22; index <= 26; index++) points[index] = new Point2D(550 + (index - 22) * 25, 475);
        for (var index = 27; index <= 30; index++) points[index] = new Point2D(500, 530 + (index - 27) * 35);
        for (var index = 31; index <= 35; index++) points[index] = new Point2D(460 + (index - 31) * 20, 650);
        for (var eye = 0; eye < 2; eye++)
        for (var index = 0; index < 6; index++)
        {
            var angle = index * MathF.PI / 3;
            points[36 + eye * 6 + index] = new Point2D(410 + eye * 180 + 25 * MathF.Cos(angle), 520 + 12 * MathF.Sin(angle));
        }
        for (var index = 48; index < 68; index++)
        {
            var inner = index >= 60;
            var angle = (index - (inner ? 60 : 48)) * 2 * MathF.PI / (inner ? 8 : 12);
            points[index] = new Point2D(500 + (inner ? 35 : 65) * MathF.Cos(angle), 700 + (inner ? 10 : 28) * MathF.Sin(angle));
        }
        return new FaceLandmarks68(points.Select(point => new Point2D(centerX + (point.X - 500) * scale,
            centerY + (point.Y - 600) * scale)).ToArray());
    }

    [Test]
    public void RegionPolicyPreservesNativeCropAndBudgetIndependentGeometry()
    {
        var minimum = ProfileSpecifications.Piv;
        var preferred = minimum.WithFileSizeTarget(ProfileFileSizeTargets.PivPreferred).Value;
        var sourceDimensions = new ImageDimensions(1000, 1200);
        var sourceLandmarks = Landmarks();
        var first = PortraitPlanSolver.Solve(sourceDimensions, sourceLandmarks, minimum.Portrait).Value;
        var second = PortraitPlanSolver.Solve(sourceDimensions, sourceLandmarks, preferred.Portrait).Value;
        var extended = PortraitPlanSolver.Solve(sourceDimensions, sourceLandmarks,
            minimum.Portrait with { FaceRegion = PivFaceRegion.ExtendedFace }).Value;
        Assert.That(first.CropRectangle, Is.EqualTo(second.CropRectangle));
        Assert.That(first.CropRectangle, Is.EqualTo(extended.CropRectangle));
        Assert.That(first.GeometryEvidence!.SourceToOutputScale, Is.EqualTo(second.GeometryEvidence!.SourceToOutputScale));
        Assert.That(first.GeometryEvidence.HeadWidthCC, Is.EqualTo(extended.GeometryEvidence!.HeadWidthCC));
        Assert.That(first.GeometryEvidence.OriginalPixelSupportVerified, Is.True);
        Assert.That(extended.GeometryEvidence.OriginalPixelSupportVerified, Is.True);
        Assert.That(first.FaceRegion, Is.EqualTo(PivFaceRegion.LandmarkFace));
        Assert.That(extended.FaceRegion, Is.EqualTo(PivFaceRegion.ExtendedFace));
        var firstMask = AnatomicalFaceRoi.Create(new FaceLandmarks68(sourceLandmarks.Points
            .Select(first.TransformMap.MapSourceToOutput).ToArray()), 480, 640, first.FaceRegion).Value;
        var secondMask = AnatomicalFaceRoi.Create(new FaceLandmarks68(sourceLandmarks.Points
            .Select(second.TransformMap.MapSourceToOutput).ToArray()), 480, 640, second.FaceRegion).Value;
        Assert.That(firstMask.Mask!.ToArray(), Is.EqualTo(secondMask.Mask!.ToArray()));
    }

    [Test]
    public void PivCandidateRecordsNativeScaleAndEstimatedAnatomicalGates()
    {
        var result = PortraitPlanSolver.Solve(new ImageDimensions(1000, 1200), Landmarks(), ProfileSpecifications.Piv.Portrait);
        Assert.That(result.IsSuccess, Is.True, result.IsFailure ? result.Error.Message : string.Empty);
        var evidence = result.Value.GeometryEvidence!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Value.OutputDimensions, Is.EqualTo(new ImageDimensions(480, 640)));
            Assert.That(evidence.SourceToOutputScale, Is.LessThanOrEqualTo(1));
            Assert.That(evidence.OutputJawWidth, Is.GreaterThanOrEqualTo(240));
            Assert.That(evidence.HeadWidthCC.Status, Is.EqualTo(GeometryEvidenceStatus.Estimated));
            Assert.That(evidence.HeadHeightDD.Status, Is.EqualTo(GeometryEvidenceStatus.Estimated));
            Assert.That(evidence.VerificationRequirements, Has.Some.Contains("ear-to-head"));
            Assert.That(evidence.OriginalPixelSupportVerified, Is.True);
        });
        var transform = result.Value.TransformMap.SourceToOutput.Decompose();
        Assert.That(transform.ScaleX, Is.EqualTo(transform.ScaleY).Within(0.00001));
        foreach (var point in result.Value.TransformMap.OutputBoundsOnSource())
        {
            Assert.That(point.X, Is.InRange(0, 1000));
            Assert.That(point.Y, Is.InRange(0, 1200));
        }
    }

    [Test]
    public void SuppliedEarAndCrownMeasurementsHaveDistinctMeasuredEvidence()
    {
        var anatomy = new AnatomicalGeometry(new Point2D(280, 460), new Point2D(720, 460), new Point2D(500, 230), "Reviewed fixture points");
        var result = PortraitPlanSolver.Solve(new ImageDimensions(1000, 1200), Landmarks(), ProfileSpecifications.Piv.Portrait, anatomy);
        Assert.That(result.IsSuccess, Is.True, result.IsFailure ? result.Error.Message : string.Empty);
        Assert.That(result.Value.GeometryEvidence!.HeadWidthCC.Status, Is.EqualTo(GeometryEvidenceStatus.Measured));
        Assert.That(result.Value.GeometryEvidence.HeadWidthCC.SourcePixels, Is.EqualTo(440));
        Assert.That(result.Value.GeometryEvidence.HeadHeightDD.Status, Is.EqualTo(GeometryEvidenceStatus.Measured));
        Assert.That(result.Value.GeometryEvidence.VerificationRequirements, Has.Some.Contains("acquisition"));
    }

    [Test]
    public void MeasuredEarWidthBelow240RequiresHigherResolutionAcquisition()
    {
        var anatomy = new AnatomicalGeometry(new Point2D(390, 460), new Point2D(610, 460), MeasurementSource: "Reviewed fixture points");
        var result = PortraitPlanSolver.Solve(new ImageDimensions(1000, 1200), Landmarks(), ProfileSpecifications.Piv.Portrait, anatomy);
        Assert.That(result.IsFailure, Is.True);
    }

    [Test]
    public void LowResolutionFaceRequiresNativeDetailInsteadOfEnlargement()
    {
        var result = PortraitPlanSolver.Solve(new ImageDimensions(1000, 1200), Landmarks(0.5f), ProfileSpecifications.Piv.Portrait);
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Message, Does.Contain("enlargement"));
    }

    [Test]
    public void Native512SourceCanUseExplicitSquareCanvas()
    {
        var specification = ProfileSpecifications.Piv.Portrait with
        {
            OutputDimensions = new ImageDimensions(448, 448), TargetEyeLineFromTopRatio = 0.48f
        };
        var result = PortraitPlanSolver.Solve(new ImageDimensions(512, 512), Landmarks(0.6f, 256, 256), specification);
        Assert.That(result.IsSuccess, Is.True, result.IsFailure ? result.Error.Message : string.Empty);
        Assert.That(result.Value.GeometryEvidence!.SourceToOutputScale, Is.LessThanOrEqualTo(1));
        Assert.That(result.Value.GeometryEvidence.HeadWidthCC.OutputPixels, Is.GreaterThanOrEqualTo(240));
    }

    [TestCase(420)]
    [TestCase(413)]
    public void PivCanvasWidthMustExceed420(int width)
    {
        var specification = ProfileSpecifications.Piv.Portrait with { OutputDimensions = new ImageDimensions(width, 640) };
        var result = PortraitPlanSolver.Solve(new ImageDimensions(1000, 1200), Landmarks(), specification);
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Message, Does.Contain("greater than 420"));
    }

    [Test]
    public void RejectsOriginalSourcePixelsOutsideRotatedCropPolygon()
    {
        var sourceDimensions = new ImageDimensions(1000, 1200);
        var points = Landmarks().Points.Select(point => new Point2D(point.X - 250, point.Y - 100)).ToArray();
        var landmarks = new FaceLandmarks68(points);
        var result = PortraitPlanSolver.Solve(sourceDimensions, landmarks, ProfileSpecifications.Piv.Portrait);
        Assert.That(result.IsFailure, Is.True);
    }

    [Test]
    public void RotatedNativeCropMovesToOriginalSupportWhileKeepingNormativeEyeInterval()
    {
        var dimensions = new ImageDimensions(1000, 1200);
        var landmarks = new FaceLandmarks68(Landmarks(centerY: 430).Points
            .Select(point => RotatePoint(point, 4, dimensions)).ToArray());
        var crown = RotatePoint(new Point2D(500, 70), 4, dimensions);
        var result = PortraitPlanSolver.Solve(dimensions, landmarks, ProfileSpecifications.Piv.Portrait,
            new AnatomicalGeometry(Crown: crown, MeasurementSource: "Reviewed rotated fixture crown"));
        Assert.That(result.IsSuccess, Is.True, result.IsFailure ? result.Error.Message : string.Empty);
        Assert.That(result.Value.GeometryEvidence!.EyeLineFromTopRatio, Is.InRange(0.3f, 0.5f));
        Assert.That(result.Value.GeometryEvidence.EyeLineFromTopRatio, Is.LessThan(0.37f));
        Assert.That(result.Value.GeometryEvidence.SourceToOutputScale, Is.LessThanOrEqualTo(1));
        foreach (var point in result.Value.TransformMap.OutputBoundsOnSource())
        {
            Assert.That(point.X, Is.InRange(-0.001f, 1000.001f));
            Assert.That(point.Y, Is.InRange(-0.001f, 1200.001f));
        }
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void NonfiniteLandmarksProduceGeometryFailure(float coordinate)
    {
        var points = Landmarks().Points.ToArray();
        points[8] = new Point2D(coordinate, 800);
        var result = PortraitPlanSolver.Solve(new ImageDimensions(1000, 1200), new FaceLandmarks68(points), ProfileSpecifications.Piv.Portrait);
        Assert.That(result.IsFailure, Is.True);
    }

    [TestCase(0f)]
    [TestCase(0.05f)]
    [TestCase(4f)]
    public void RendererUsesTheSameLandmarkGeometryAsItsForwardMap(float sourceRoll)
    {
        var sourceDimensions = new ImageDimensions(1000, 1200);
        var landmarks = new FaceLandmarks68(Landmarks().Points.Select(point =>
            RotatePoint(point, sourceRoll, sourceDimensions)).ToArray());
        var plan = PortraitPlanSolver.Solve(sourceDimensions, landmarks, ProfileSpecifications.Piv.Portrait).Value;
        var faceBox = FaceBox.Create(300, 400, 400, 400).Value;
        var geometry = new CanonicalFaceGeometry(new DetectedFace(faceBox, 1, Maybe<FaceLandmarks5>.None),
            new FaceLandmarks5(landmarks.LeftEyeCenter, landmarks.RightEyeCenter, landmarks.Points[30], landmarks.Points[48], landmarks.Points[54]),
            sourceDimensions, landmarks, landmarks, TransformationMatrix.Identity, TransformationMatrix.Identity);
        using var source = new Image<Rgba32>(1000, 1200);
        var rendered = PortraitRenderer.Render(source, geometry, plan);
        Assert.That(rendered.IsSuccess, Is.True, rendered.IsFailure ? rendered.Error.Message : string.Empty);
        using var output = rendered.Value.Image;
        Assert.That(rendered.Value.RoiSet.Mask, Is.Not.Null);
        for (var index = 0; index < 68; index++)
        {
            var expected = plan.TransformMap.MapSourceToOutput(landmarks.Points[index]);
            Assert.That(rendered.Value.Landmarks.Points[index].X, Is.EqualTo(expected.X).Within(0.001));
            Assert.That(rendered.Value.Landmarks.Points[index].Y, Is.EqualTo(expected.Y).Within(0.001));
        }
    }

    [TestCase(0f)]
    [TestCase(0.05f)]
    [TestCase(4f)]
    public void RendererMapsPixelMarkersInOnePassAndPreservesTheSource(float sourceRoll)
    {
        var dimensions = new ImageDimensions(1000, 1200);
        var landmarks = new FaceLandmarks68(Landmarks().Points.Select(point =>
            RotatePoint(point, sourceRoll, dimensions)).ToArray());
        var plan = PortraitPlanSolver.Solve(dimensions, landmarks, ProfileSpecifications.Piv.Portrait).Value;
        var faceBox = FaceBox.Create(300, 400, 400, 400).Value;
        var geometry = new CanonicalFaceGeometry(new DetectedFace(faceBox, 1, Maybe<FaceLandmarks5>.None),
            new FaceLandmarks5(landmarks.LeftEyeCenter, landmarks.RightEyeCenter, landmarks.Points[30], landmarks.Points[48], landmarks.Points[54]),
            dimensions, landmarks, landmarks, TransformationMatrix.Identity, TransformationMatrix.Identity);
        using var source = new Image<Rgba32>(1000, 1200, new Rgba32(0, 0, 0, 255));
        var markers = new[] { landmarks.LeftEyeCenter, landmarks.RightEyeCenter, landmarks.Points[30] }
            .Select(point => new Point2D(MathF.Floor(point.X) + 0.5f, MathF.Floor(point.Y) + 0.5f)).ToArray();
        for (var marker = 0; marker < markers.Length; marker++)
        {
            var center = markers[marker];
            for (var y = (int)center.Y - 8; y <= (int)center.Y + 8; y++)
            for (var x = (int)center.X - 8; x <= (int)center.X + 8; x++)
            {
                if (MathF.Pow(x + 0.5f - center.X, 2) + MathF.Pow(y + 0.5f - center.Y, 2) > 64) continue;
                source[x, y] = marker switch
                {
                    0 => new Rgba32(255, 0, 0, 255),
                    1 => new Rgba32(0, 255, 0, 255),
                    _ => new Rgba32(0, 0, 255, 255)
                };
            }
        }
        var originalPixels = new byte[source.Width * source.Height * 4];
        source.CopyPixelDataTo(originalPixels);
        var rendered = PortraitRenderer.Render(source, geometry, plan);
        Assert.That(rendered.IsSuccess, Is.True, rendered.IsFailure ? rendered.Error.Message : string.Empty);
        using var output = rendered.Value.Image;
        for (var marker = 0; marker < markers.Length; marker++)
        {
            double weight = 0, xSum = 0, ySum = 0;
            for (var y = 0; y < output.Height; y++)
            for (var x = 0; x < output.Width; x++)
            {
                var pixel = output[x, y];
                var energy = marker == 0 ? pixel.R : marker == 1 ? pixel.G : pixel.B;
                weight += energy;
                xSum += (x + 0.5) * energy;
                ySum += (y + 0.5) * energy;
            }
            var expected = plan.TransformMap.MapSourceToOutput(markers[marker]);
            Assert.That(weight, Is.GreaterThan(0));
            Assert.That(xSum / weight, Is.EqualTo(expected.X).Within(0.6), $"Marker {marker}, X");
            Assert.That(ySum / weight, Is.EqualTo(expected.Y).Within(0.6), $"Marker {marker}, Y");
        }
        var remainingPixels = new byte[originalPixels.Length];
        source.CopyPixelDataTo(remainingPixels);
        Assert.That(remainingPixels, Is.EqualTo(originalPixels));
    }

    private static Point2D RotatePoint(Point2D point, float degrees, ImageDimensions dimensions)
    {
        var angle = degrees * Math.PI / 180;
        var x = point.X - dimensions.Width / 2f;
        var y = point.Y - dimensions.Height / 2f;
        return new Point2D((float)(x * Math.Cos(angle) - y * Math.Sin(angle) + dimensions.Width / 2f),
            (float)(x * Math.Sin(angle) + y * Math.Cos(angle) + dimensions.Height / 2f));
    }

    [TestCase("minimum")]
    [TestCase("maximum")]
    [TestCase("eye")]
    [TestCase("margin")]
    [TestCase("dimensions")]
    [TestCase("region")]
    public void InvalidPortraitControlsReturnGeometryFailure(string control)
    {
        var specification = ProfileSpecifications.Piv.Portrait;
        specification = control switch
        {
            "minimum" => specification with { MinimumHeadWidthRatio = float.NaN },
            "maximum" => specification with { MaximumHeadWidthRatio = float.PositiveInfinity },
            "eye" => specification with { TargetEyeLineFromTopRatio = float.NaN },
            "margin" => specification with { MinimumSideMargin = -1 },
            "dimensions" => specification with { OutputDimensions = new ImageDimensions(int.MaxValue, int.MaxValue) },
            "region" => specification with { FaceRegion = (PivFaceRegion)99 },
            _ => throw new ArgumentOutOfRangeException(nameof(control))
        };
        var result = PortraitPlanSolver.Solve(new ImageDimensions(1000, 1200), Landmarks(), specification);
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error.Message, Does.Contain("finite portrait candidate controls"));
    }
}

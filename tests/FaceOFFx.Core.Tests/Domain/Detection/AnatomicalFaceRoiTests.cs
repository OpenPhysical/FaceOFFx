using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Tests.Domain.Transformations;
using NUnit.Framework;

namespace FaceOFFx.Core.Tests.Domain.Detection;

[TestFixture]
public class AnatomicalFaceRoiTests
{
    [Test]
    public void ReviewedMaskImportNormalizesAndOwnsPixelStorage()
    {
        var pixels = new byte[] { 0, 2, 255, 0 };
        var mask = FacialRoiMask.FromBytes(2, 2, pixels);
        Array.Fill(pixels, (byte)0);
        Assert.That(mask.ToArray(), Is.EqualTo(new byte[] { 0, 1, 1, 0 }));
        Assert.That(mask.PixelCount, Is.EqualTo(2));
        Assert.Throws<ArgumentException>(() => FacialRoiMask.FromBytes(0, 2, new byte[0]));
        Assert.Throws<ArgumentException>(() => FacialRoiMask.FromBytes(2, 2, new byte[3]));
        Assert.Throws<ArgumentException>(() => FacialRoiMask.FromBytes(2, 2, new byte[4]));
    }

    [Test]
    public void EstimatedMaskIncludesExtendedForeheadEarsAndAllDetectedFeatures()
    {
        var landmarks = PortraitPlanSolverTests.Landmarks(0.6f, 240, 300);
        var result = AnatomicalFaceRoi.Create(landmarks, 480, 640, PivFaceRegion.ExtendedFace);
        Assert.That(result.IsSuccess, Is.True, result.IsFailure ? result.Error : string.Empty);
        var roi = result.Value;
        Assert.Multiple(() =>
        {
            Assert.That(roi.AllRegions.Count, Is.EqualTo(1));
            Assert.That(roi.PixelCount, Is.GreaterThan(0));
            Assert.That(roi.PixelCount, Is.LessThan((long)roi.InnerRegion.BoundingBox.Width * roi.InnerRegion.BoundingBox.Height));
            Assert.That(roi.Coverage!.Status, Is.EqualTo(GeometryEvidenceStatus.Estimated));
            Assert.That(roi.Coverage.Algorithm, Is.EqualTo("jaw-forehead-ear-hull-v2"));
            Assert.That(roi.Coverage.FaceRegion, Is.EqualTo(PivFaceRegion.ExtendedFace));
            Assert.That(roi.Mask!.Contains(240, 110), Is.True);
            Assert.That(roi.Mask.Contains(90, 290), Is.True);
            Assert.That(roi.Mask.Contains(390, 290), Is.True);
            Assert.That(roi.Mask.Contains(0, 0), Is.False);
        });
        foreach (var point in landmarks.Points) Assert.That(roi.Mask!.Contains((int)point.X, (int)point.Y), Is.True);
    }

    [Test]
    public void EarEnvelopeUsesEyeAlignedGeometryForRolledLandmarks()
    {
        var landmarks = PortraitPlanSolverTests.Landmarks(0.6f, 240, 300);
        Point2D Rotate(Point2D point)
        {
            var angle = 4 * Math.PI / 180;
            var x = point.X - 240;
            var y = point.Y - 300;
            return new Point2D((float)(240 + x * Math.Cos(angle) - y * Math.Sin(angle)),
                (float)(300 + x * Math.Sin(angle) + y * Math.Cos(angle)));
        }
        var rotated = new FaceLandmarks68(landmarks.Points.Select(Rotate).ToArray());
        var result = AnatomicalFaceRoi.Create(rotated, 480, 640, PivFaceRegion.ExtendedFace);
        Assert.That(result.IsSuccess, Is.True, result.IsFailure ? result.Error : string.Empty);
        foreach (var point in new[] { new Point2D(240, 110), new Point2D(90, 290), new Point2D(390, 290) })
        {
            var expected = Rotate(point);
            Assert.That(result.Value.Mask!.Contains((int)MathF.Floor(expected.X), (int)MathF.Floor(expected.Y)), Is.True);
        }
    }

    [Test]
    public void CompleteEarEnvelopeRequiresMoreSpaceEvenWhenDetectedFeaturesFit()
    {
        var landmarks = PortraitPlanSolverTests.Landmarks(0.6f, 140, 300);
        Assert.That(landmarks.Points.All(point => point.X >= 0 && point.X < 480 && point.Y >= 0 && point.Y < 640), Is.True);
        var result = AnatomicalFaceRoi.Create(landmarks, 480, 640, PivFaceRegion.ExtendedFace);
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Does.Contain("complete estimated facial boundary"));
    }

    [Test]
    public void LandmarkFaceDefinesTheInnermostFeatureHullBeforeAllocation()
    {
        var landmarks = PortraitPlanSolverTests.Landmarks(0.6f, 240, 300);
        var landmark = AnatomicalFaceRoi.Create(landmarks, 480, 640).Value;
        var extended = AnatomicalFaceRoi.Create(landmarks, 480, 640, PivFaceRegion.ExtendedFace).Value;
        Assert.Multiple(() =>
        {
            Assert.That(landmark.Coverage!.FaceRegion, Is.EqualTo(PivFaceRegion.LandmarkFace));
            Assert.That(landmark.Coverage.Algorithm, Is.EqualTo("landmark-face-hull-v1"));
            Assert.That(landmark.Coverage.ForeheadExtensionRatio, Is.Zero);
            Assert.That(landmark.Coverage.PaddingRatio, Is.EqualTo(0.03f));
            Assert.That(landmark.PixelCount, Is.LessThan(extended.PixelCount));
            Assert.That(landmark.AllRegions, Has.Count.EqualTo(1));
            Assert.That(landmark.Mask!.Width, Is.EqualTo(480));
            Assert.That(landmark.Mask.Height, Is.EqualTo(640));
            Assert.That(landmark.Coverage.VerificationRequirements, Has.Some.Contains("brows, eyes, nose, mouth"));
        });
        foreach (var point in landmarks.Points)
            Assert.That(landmark.Mask!.Contains((int)point.X, (int)point.Y), Is.True);
    }

    [TestCase(PivFaceRegion.LandmarkFace)]
    [TestCase(PivFaceRegion.ExtendedFace)]
    public void FixedPolicyOwnsMaskStorageAndCoversRolledFeatures(PivFaceRegion policy)
    {
        var original = PortraitPlanSolverTests.Landmarks(0.6f, 240, 300);
        var angle = 4 * Math.PI / 180;
        var rolled = new FaceLandmarks68(original.Points.Select(point => new Point2D(
            (float)(240 + (point.X - 240) * Math.Cos(angle) - (point.Y - 320) * Math.Sin(angle)),
            (float)(320 + (point.X - 240) * Math.Sin(angle) + (point.Y - 320) * Math.Cos(angle)))).ToArray());
        var first = AnatomicalFaceRoi.Create(rolled, 480, 640, policy).Value;
        var second = AnatomicalFaceRoi.Create(rolled, 480, 640, policy).Value;
        var detached = first.Mask!.ToArray();
        Array.Clear(detached);
        Assert.That(second.Mask!.ToArray(), Is.EqualTo(first.Mask.ToArray()));
        foreach (var point in rolled.Points)
            Assert.That(first.Mask.Contains((int)point.X, (int)point.Y), Is.True);
    }

    [Test]
    public void UnsupportedFaceRegionRequiresExplicitPolicySelection()
    {
        var result = AnatomicalFaceRoi.Create(PortraitPlanSolverTests.Landmarks(), 1000, 1200, (PivFaceRegion)99);
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Does.Contain("LandmarkFace or ExtendedFace"));
    }

    [Test]
    public void PixelStorageIsIndependentAndRegionConstructionIsDeterministic()
    {
        var landmarks = PortraitPlanSolverTests.Landmarks(0.6f, 240, 300);
        var first = AnatomicalFaceRoi.Create(landmarks, 480, 640).Value;
        var second = AnatomicalFaceRoi.Create(landmarks, 480, 640).Value;
        var original = first.Mask!.ToArray();
        var callerCopy = first.Mask.ToArray();
        Array.Fill(callerCopy, (byte)0);
        Assert.That(first.Mask.ToArray(), Is.EqualTo(original));
        Assert.That(second.Mask!.ToArray(), Is.EqualTo(original));
        Assert.That(second.PixelCount, Is.EqualTo(first.PixelCount));
    }

    [Test]
    public void EntireFacialBoundaryRequiresCanvasCoverage()
    {
        var landmarks = PortraitPlanSolverTests.Landmarks(0.6f, 110, 300);
        var result = AnatomicalFaceRoi.Create(landmarks, 480, 640);
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error, Does.Contain("complete estimated facial boundary"));
    }

    [Test]
    public void NonfiniteAndDegenerateGeometryProduceExplicitFailure()
    {
        var points = PortraitPlanSolverTests.Landmarks().Points.ToArray();
        points[8] = new Point2D(float.NaN, 300);
        Assert.That(AnatomicalFaceRoi.Create(new FaceLandmarks68(points), 480, 640).IsFailure, Is.True);
        Assert.That(AnatomicalFaceRoi.Create(new FaceLandmarks68(Enumerable.Repeat(new Point2D(200, 200), 68).ToArray()), 480, 640).IsFailure, Is.True);
    }
}

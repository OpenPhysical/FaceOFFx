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

using FaceOFFx.Core.Domain.Quality.Assessors;
using AwesomeAssertions;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Core.Tests.Quality;

[TestFixture]
public class SharpnessAssessorTests
{
    private static string SampleImagePath(string fileName) =>
        Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "tests", "sample_images", fileName
        );

    private static Image<Rgba32> LoadSampleImage(string fileName) =>
        Image.Load<Rgba32>(SampleImagePath(fileName));

    private static Image<Rgba32> CreateBlurredVariant(string fileName, float sigma)
    {
        var image = LoadSampleImage(fileName);
        if (sigma > 0)
        {
            image.Mutate(ctx => ctx.GaussianBlur(sigma));
        }

        return image;
    }

    [Test]
    public void Assess_WithSharpImage_ReturnsHighScore()
    {
        using var image = LoadSampleImage("johnson_photo.jpg");

        var result = SharpnessAssessor.Assess(image);

        result.IsSuccess.Should().BeTrue();
        result.Value.Overall.Value.Should().BeGreaterThan(0.8f);
    }

    [Test]
    public void Assess_WithBlurredImage_ReturnsLowerScore()
    {
        using var sharpImage = CreateBlurredVariant("johnson_photo.jpg", 0f);
        using var blurredImage = CreateBlurredVariant("johnson_photo.jpg", 1f);

        var sharpResult = SharpnessAssessor.Assess(sharpImage);
        var blurredResult = SharpnessAssessor.Assess(blurredImage);

        sharpResult.IsSuccess.Should().BeTrue();
        blurredResult.IsSuccess.Should().BeTrue();
        blurredResult.Value.Overall.Value.Should().BeLessThan(sharpResult.Value.Overall.Value);
    }

    [Test]
    [TestCase(0.0f)]
    [TestCase(0.5f)]
    [TestCase(0.63f)]
    [TestCase(0.7f)]
    [TestCase(1.0f)]
    public void Assess_WithGeneratedBlurLevels_ReturnsNormalizedScore(float blurSigma)
    {
        using var image = CreateBlurredVariant("johnson_photo.jpg", blurSigma);

        var result = SharpnessAssessor.Assess(image);

        result.IsSuccess.Should().BeTrue();
        result.Value.Overall.Value.Should().BeInRange(0f, 1f);
    }

    [Test]
    [TestCase("johnson_photo.jpg")]
    [TestCase("bush_photo.jpg")]
    public void Assess_BlurProgression_ShowsMonotonicDecrease(string baseImage)
    {
        var blurLevels = new[] { 0f, 0.3f, 0.5f, 0.63f, 0.7f, 1.0f };
        var scores = new List<(float blurLevel, float score)>();

        foreach (var blurLevel in blurLevels)
        {
            using var image = CreateBlurredVariant(baseImage, blurLevel);
            var result = SharpnessAssessor.Assess(image);
            result.IsSuccess.Should().BeTrue();
            scores.Add((blurLevel, result.Value.Overall.Value));
        }

        for (var i = 1; i < scores.Count; i++)
        {
            scores[i].score.Should().BeLessThanOrEqualTo(
                scores[i - 1].score,
                $"blur {scores[i].blurLevel:0.00} should not score higher than blur {scores[i - 1].blurLevel:0.00}"
            );
        }
    }

    [Test]
    public void Assess_Blur063FallsBetweenModerateAndHeavyBlur()
    {
        using var moderate = CreateBlurredVariant("johnson_photo.jpg", 0.5f);
        using var calibration = CreateBlurredVariant("johnson_photo.jpg", 0.63f);
        using var heavy = CreateBlurredVariant("johnson_photo.jpg", 1.0f);

        var moderateScore = SharpnessAssessor.Assess(moderate).Value.Overall.Value;
        var calibrationScore = SharpnessAssessor.Assess(calibration).Value.Overall.Value;
        var heavyScore = SharpnessAssessor.Assess(heavy).Value.Overall.Value;

        calibrationScore.Should().BeLessThan(moderateScore);
        calibrationScore.Should().BeGreaterThan(heavyScore);
    }

    [Test]
    public void Assess_WithRegionOfInterest_AnalyzesOnlySpecifiedRegion()
    {
        using var image = CreateTestPattern(64, 64);
        var roi = new Rectangle(16, 16, 32, 32);

        var fullResult = SharpnessAssessor.Assess(image);
        var roiResult = SharpnessAssessor.Assess(image, roi);

        fullResult.IsSuccess.Should().BeTrue();
        roiResult.IsSuccess.Should().BeTrue();
        roiResult.Value.Overall.Value.Should().BeApproximately(fullResult.Value.Overall.Value, 0.000001f);
    }

    [Test]
    public void Assess_WithTooSmallImage_ReturnsFailure()
    {
        using var image = new Image<Rgba32>(4, 4);

        var result = SharpnessAssessor.Assess(image);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("too small");
    }

    [Test]
    public void Assess_WithNullImage_ReturnsFailure()
    {
        var result = SharpnessAssessor.Assess(null!);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("No image data");
    }

    [Test]
    [TestCase("Center", 2.0f)]
    [TestCase("Top", 1.0f)]
    [TestCase("Bottom", 1.0f)]
    [TestCase("Left", 1.0f)]
    [TestCase("Right", 1.0f)]
    public void Assess_RegionalWeighting_CenterWeightedMoreHeavily(string regionName, float expectedWeight)
    {
        using var image = CreateTestPattern(64, 64);

        var measurement = SharpnessAssessor.MeasureSharpness(image);

        measurement.IsSuccess.Should().BeTrue();
        measurement.Value.RegionalScores.Should().ContainKey(regionName);
        TestContext.WriteLine($"Region {regionName} expected weight: {expectedWeight}x");
    }

    private static Image<Rgba32> CreateTestPattern(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < width; x++)
                {
                    var isWhite = ((x / 8) + (y / 8)) % 2 == 0;
                    row[x] = isWhite ? Color.White : Color.Black;
                }
            }
        });

        return image;
    }
}

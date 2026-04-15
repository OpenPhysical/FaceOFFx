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

using FaceOFFx.Core.Domain.Quality.Sharpness;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Core.Tests.Domain.Quality.Sharpness;

[TestFixture]
public class LaplacianAnalyzerTests
{
    private ILogger<LaplacianAnalyzerTests> _logger = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        _logger = loggerFactory.CreateLogger<LaplacianAnalyzerTests>();
    }

    private static string SampleImagePath(string fileName) =>
        Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "tests", "sample_images", fileName
        );

    private static Image<Rgba32> CreateBlurredVariant(string fileName, float sigma)
    {
        var image = Image.Load<Rgba32>(SampleImagePath(fileName));
        if (sigma > 0)
        {
            image.Mutate(ctx => ctx.GaussianBlur(sigma));
        }

        return image;
    }

    [Test]
    public void Analyze_WithProgressiveBlur_ShowsMonotonicDecrease()
    {
        var blurLevels = new[] { 0f, 0.5f, 0.63f, 0.7f, 1.0f };
        var results = new List<(float blurLevel, float variance, float normalized)>();

        foreach (var blurLevel in blurLevels)
        {
            using var image = CreateBlurredVariant("johnson_photo.jpg", blurLevel);
            var grayscaleResult = image.ToGrayscale();
            grayscaleResult.IsSuccess.Should().BeTrue();

            var analysisResult = LaplacianAnalyzer.Analyze(grayscaleResult.Value, _logger);
            analysisResult.IsSuccess.Should().BeTrue();

            var metrics = analysisResult.Value;
            results.Add((blurLevel, metrics.Variance, metrics.NormalizedVariance));
        }

        results.Should().HaveCount(blurLevels.Length);

        for (var i = 1; i < results.Count; i++)
        {
            results[i].variance.Should().BeLessThanOrEqualTo(results[i - 1].variance);
        }

        (results.First().variance / results.Last().variance).Should().BeGreaterThan(2f);
    }

    [Test]
    public void Analyze_WithSharpProfessionalPhoto_ScoresWell()
    {
        using var image = Image.Load<Rgba32>(SampleImagePath("johnson_photo.jpg"));

        var grayscaleResult = image.ToGrayscale();
        grayscaleResult.IsSuccess.Should().BeTrue();

        var analysisResult = LaplacianAnalyzer.Analyze(grayscaleResult.Value, _logger);

        analysisResult.IsSuccess.Should().BeTrue();
        var metrics = analysisResult.Value;
        metrics.NormalizedVariance.Should().BeGreaterThan(0.3f);
        metrics.Kurtosis.Should().BeGreaterThan(1f);
    }

    [Test]
    public void Analyze_WithUniformImage_HandlesGracefully()
    {
        using var image = new Image<Rgba32>(100, 100, new Rgba32(128, 128, 128));

        var grayscaleResult = image.ToGrayscale();
        grayscaleResult.IsSuccess.Should().BeTrue();

        var analysisResult = LaplacianAnalyzer.Analyze(grayscaleResult.Value, _logger);

        analysisResult.IsFailure.Should().BeTrue();
        analysisResult.Error.Should().Contain("insufficient contrast");
    }

    [Test]
    public void Analyze_WithHighContrastPattern_ShowsHighVariance()
    {
        using var image = new Image<Rgba32>(100, 100);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < 100; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < 100; x++)
                {
                    var isBlack = ((x / 10) + (y / 10)) % 2 == 0;
                    row[x] = isBlack ? new Rgba32(0, 0, 0) : new Rgba32(255, 255, 255);
                }
            }
        });

        var grayscaleResult = image.ToGrayscale();
        var analysisResult = LaplacianAnalyzer.Analyze(grayscaleResult.Value, _logger);

        analysisResult.IsSuccess.Should().BeTrue();
        analysisResult.Value.NormalizedVariance.Should().BeGreaterThan(0.8f);
    }
}

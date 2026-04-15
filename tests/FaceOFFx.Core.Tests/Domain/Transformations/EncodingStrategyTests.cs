using AwesomeAssertions;
using CSharpFunctionalExtensions;
using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using NSubstitute;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Tests.Domain.Transformations;

/// <summary>
/// Tests for encoding strategy implementations
/// </summary>
[TestFixture]
public class EncodingStrategyTests
{
    private IJpeg2000Encoder _mockEncoder = null!;
    private Image<Rgba32> _testImage = null!;
    private FacialRoiSet _testRoiSet = null!;
    private ProcessingOptions _testOptions = null!;

    /// <inheritdoc/>
    [SetUp]
    public void SetUp()
    {
        _mockEncoder = Substitute.For<IJpeg2000Encoder>();
        _testImage = new Image<Rgba32>(420, 560);
        var testRoiRegion = new RoiRegion(
            "Test",
            3,
            new RoiBoundingBox(0, 0, 100, 100),
            new List<int> { 0, 1, 2 }
        );
        _testRoiSet = new FacialRoiSet(testRoiRegion);
        _testOptions = ProcessingOptions.PivBalanced;
    }

    /// <inheritdoc/>
    [TearDown]
    public void TearDown()
    {
        _testImage?.Dispose();
    }

    /// <summary>
    /// Tests that FixedRate factory method creates correct strategy type
    /// </summary>
    [Test]
    public void FixedRate_CreatesCorrectStrategyType()
    {
        var strategy = EncodingStrategy.FixedRate(1.5f);

        strategy.Should().BeOfType<FixedRateStrategy>();
        ((FixedRateStrategy)strategy).Rate.Should().Be(1.5f);
    }

    /// <summary>
    /// Tests that TargetSize factory method creates correct strategy type
    /// </summary>
    [Test]
    public void TargetSize_CreatesCorrectStrategyType()
    {
        var strategy = EncodingStrategy.TargetSize(20000);

        strategy.Should().BeOfType<TargetSizeStrategy>();
        ((TargetSizeStrategy)strategy).TargetBytes.Should().Be(20000);
    }

    /// <summary>
    /// Tests that FixedRateStrategy executes encoding with specified rate
    /// </summary>
    [Test]
    public void FixedRateStrategy_Execute_CallsEncoderWithCorrectRate()
    {
        var testData = new byte[] { 1, 2, 3, 4, 5 };
        _mockEncoder
            .EncodeWithRoi(Arg.Any<Image<Rgba32>>(), Arg.Any<FacialRoiSet>(), 2.0f, 3, true, false)
            .Returns(SuccessBytes(testData));

        var strategy = new FixedRateStrategy(2.0f);
        var result = strategy.Execute(_testImage, _testRoiSet, _mockEncoder, _testOptions);

        result.IsSuccess.Should().BeTrue();
        result.Value.Data.Should().Equal(testData);
        result.Value.ActualRate.Should().Be(2.0f);
        result.Value.TargetSize.HasValue.Should().BeFalse();

        _mockEncoder.Received(1).EncodeWithRoi(_testImage, _testRoiSet, 2.0f, 3, true, false);
    }

    /// <summary>
    /// Tests that FixedRateStrategy propagates encoder failures
    /// </summary>
    [Test]
    public void FixedRateStrategy_Execute_PropagatesEncoderFailure()
    {
        _mockEncoder
            .EncodeWithRoi(
                Arg.Any<Image<Rgba32>>(),
                Arg.Any<FacialRoiSet>(),
                Arg.Any<float>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(FailureBytes("Encoding failed"));

        var strategy = new FixedRateStrategy(1.0f);
        var result = strategy.Execute(_testImage, _testRoiSet, _mockEncoder, _testOptions);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Encoding failed");
    }

    /// <summary>
    /// Tests that TargetSizeStrategy finds exact match when possible
    /// </summary>
    [Test]
    public void TargetSizeStrategy_Execute_FindsExactMatch()
    {
        var targetSize = 20000;
        var testData = new byte[19800]; // Within 5% of target (20000 * 0.95 = 19000)

        // Mock encoder to return sizes for different rates
        // Return oversized for higher rates and target-sized for 0.68f (from CompressionSteps)
        _mockEncoder
            .EncodeWithRoi(
                Arg.Any<Image<Rgba32>>(),
                _testRoiSet,
                Arg.Is<float>(r => r > 0.68f),
                3,
                true,
                false
            )
            .Returns(SuccessBytes(new byte[25000])); // Too big
        _mockEncoder
            .EncodeWithRoi(Arg.Any<Image<Rgba32>>(), _testRoiSet, 0.68f, 3, true, false)
            .Returns(SuccessBytes(testData));

        var strategy = new TargetSizeStrategy(targetSize);
        var result = strategy.Execute(_testImage, _testRoiSet, _mockEncoder, _testOptions);

        result.IsSuccess.Should().BeTrue();
        result.Value.Data.Should().Equal(testData);
        result.Value.ActualRate.Should().Be(0.68f);
        result.Value.TargetSize.HasValue.Should().BeTrue();
        result.Value.TargetSize.Value.Should().Be(targetSize);
    }

    /// <summary>
    /// Tests that TargetSizeStrategy finds best fit when no exact match
    /// </summary>
    [Test]
    public void TargetSizeStrategy_Execute_FindsBestFit()
    {
        var targetSize = 15000;

        // Mock multiple encoding attempts with different sizes
        // Using actual rates from CompressionSteps array
        _mockEncoder
            .EncodeWithRoi(Arg.Any<Image<Rgba32>>(), _testRoiSet, 0.36f, 3, true, false)
            .Returns(SuccessBytes(new byte[12000])); // Too small
        _mockEncoder
            .EncodeWithRoi(Arg.Any<Image<Rgba32>>(), _testRoiSet, 0.46f, 3, true, false)
            .Returns(SuccessBytes(new byte[14500])); // Best fit under target
        _mockEncoder
            .EncodeWithRoi(Arg.Any<Image<Rgba32>>(), _testRoiSet, 0.55f, 3, true, false)
            .Returns(SuccessBytes(new byte[17000])); // Too big
        // Mock all other rates to return too big
        _mockEncoder
            .EncodeWithRoi(
                Arg.Any<Image<Rgba32>>(),
                _testRoiSet,
                Arg.Is<float>(r => r > 0.55f),
                3,
                true,
                false
            )
            .Returns(SuccessBytes(new byte[20000])); // Too big

        var strategy = new TargetSizeStrategy(targetSize);
        var result = strategy.Execute(_testImage, _testRoiSet, _mockEncoder, _testOptions);

        result.IsSuccess.Should().BeTrue();
        result.Value.Data.Length.Should().Be(14500);
        result.Value.ActualRate.Should().Be(0.46f);
        result.Value.TargetSize.Value.Should().Be(targetSize);
    }

    /// <summary>
    /// Tests that TargetSizeStrategy fails when no compression step fits target
    /// </summary>
    [Test]
    public void TargetSizeStrategy_Execute_FailsWhenNoFit()
    {
        var targetSize = 5000; // Very small target

        // Mock all attempts to return sizes larger than target
        _mockEncoder
            .EncodeWithRoi(
                Arg.Any<Image<Rgba32>>(),
                Arg.Any<FacialRoiSet>(),
                Arg.Any<float>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(SuccessBytes(new byte[10000])); // All too big

        var strategy = new TargetSizeStrategy(targetSize);
        var result = strategy.Execute(_testImage, _testRoiSet, _mockEncoder, _testOptions);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Cannot compress image to 5000 bytes");
    }

    /// <summary>
    /// Tests that TargetSizeStrategy handles encoder failures gracefully
    /// </summary>
    [Test]
    public void TargetSizeStrategy_Execute_HandlesEncoderFailures()
    {
        var targetSize = 20000;

        // Mock some encoding failures and one success
        // Using actual rates from CompressionSteps array
        _mockEncoder
            .EncodeWithRoi(Arg.Any<Image<Rgba32>>(), _testRoiSet, 0.85f, 3, true, false)
            .Returns(FailureBytes("First failure"));
        _mockEncoder
            .EncodeWithRoi(Arg.Any<Image<Rgba32>>(), _testRoiSet, 0.75f, 3, true, false)
            .Returns(FailureBytes("Second failure"));
        _mockEncoder
            .EncodeWithRoi(Arg.Any<Image<Rgba32>>(), _testRoiSet, 0.68f, 3, true, false)
            .Returns(SuccessBytes(new byte[18000])); // Success under target
        // Mock lower rates to return smaller sizes
        _mockEncoder
            .EncodeWithRoi(
                Arg.Any<Image<Rgba32>>(),
                _testRoiSet,
                Arg.Is<float>(r => r < 0.68f),
                3,
                true,
                false
            )
            .Returns(SuccessBytes(new byte[15000]));

        var strategy = new TargetSizeStrategy(targetSize);
        var result = strategy.Execute(_testImage, _testRoiSet, _mockEncoder, _testOptions);

        result.IsSuccess.Should().BeTrue();
        result.Value.Data.Length.Should().Be(18000);
        result.Value.ActualRate.Should().Be(0.68f);
    }

    /// <summary>
    /// Tests that TargetSizeStrategy uses correct compression steps
    /// </summary>
    [Test]
    public void TargetSizeStrategy_Execute_UsesCorrectCompressionSteps()
    {
        var targetSize = 20000;
        // For 20KB target with intelligent retry algorithm (MaxRetries=2, so 3 total tries):
        // 1. Safety margin: 20,000 × 0.95 = 19,000 bytes
        // 2. Expected rate: 0.55 bpp (produces ~17,700 bytes, fits under 19,000)
        // 3. Distribution: Floor(3/2) = 1 upper try, Ceiling(3/2) = 2 lower tries
        // 4. Sequence: 0.68 (upper), 0.55 (target), 0.46 (lower)
        var expectedRates = new[]
        {
            0.68f,
            0.55f,
            0.46f, // Intelligent retry sequence
        };

        // Mock encoder to always return too-large sizes so we test the steps
        _mockEncoder
            .EncodeWithRoi(
                Arg.Any<Image<Rgba32>>(),
                Arg.Any<FacialRoiSet>(),
                Arg.Any<float>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<bool>()
            )
            .Returns(SuccessBytes(new byte[25000])); // Always too big

        var strategy = new TargetSizeStrategy(targetSize);
        var result = strategy.Execute(_testImage, _testRoiSet, _mockEncoder, _testOptions);

        // Should fail since no rate produces small enough file
        result.IsFailure.Should().BeTrue();

        // Verify it tried the expected rates in order
        foreach (var rate in expectedRates)
        {
            _mockEncoder
                .Received()
                .EncodeWithRoi(Arg.Any<Image<Rgba32>>(), _testRoiSet, rate, 3, true, false);
        }
    }

    private static Result<byte[], PipelineError> SuccessBytes(byte[] data) =>
        Result.Success<byte[], PipelineError>(data);

    private static Result<byte[], PipelineError> FailureBytes(string message) =>
        Result.Failure<byte[], PipelineError>(new RenderError(message, "test"));
}

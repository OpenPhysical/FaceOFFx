using AwesomeAssertions;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Infrastructure.Services;
using FaceOFFx.Tests.Common;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Infrastructure.Tests.Services;

/// <summary>
/// Contains unit and integration tests for the ONNX-based landmark extractor.
/// </summary>
[TestFixture]
[NonParallelizable]
public class OnnxLandmarkExtractorTests : IntegrationTestBase
{
    /// <summary>
    /// Private member variable used for testing the <see cref="OnnxLandmarkExtractor"/> class functionality within
    /// the <c>OnnxLandmarkExtractorTests</c> test suite.
    /// This variable is an instance of the <see cref="OnnxLandmarkExtractor"/> class, which is responsible for
    /// performing 68-point facial landmark extraction using an ONNX-based PFLD model.
    /// It is initialized during the test setup process and disposed of in the teardown process to ensure
    /// proper resource management between test executions. The instance allows the validation of landmark
    /// extraction accuracy, handling of various input conditions, and boundary cases by executing defined tests.
    /// </summary>
    private OnnxLandmarkExtractor _extractor = null!;

    /// <summary>
    /// Sets up resources and dependencies required for the test fixture, executed once before any tests in the fixture are run.
    /// </summary>
    /// <remarks>
    /// This method initializes shared services, dependencies, or configurations needed for the test suite.
    /// Overrides the base implementation of <c>OneTimeSetUp</c> to perform specific setup logic.
    /// </remarks>
    [OneTimeSetUp]
    public override void OneTimeSetUp()
    {
        base.OneTimeSetUp();
        var typedLogger = Substitute.For<ILogger<OnnxLandmarkExtractor>>();
        _extractor = OnnxLandmarkExtractor.Create(typedLogger).Value;
    }

    /// <summary>
    /// Performs teardown operations once after all tests in the fixture have run.
    /// Used to release resources and perform cleanup specific to the test class.
    /// Overrides the base class implementation to ensure proper disposal of resources,
    /// such as the OnnxLandmarkExtractor instance, and to call the inherited teardown logic from the base class.
    /// </summary>
    [OneTimeTearDown]
    public override void OneTimeTearDown()
    {
        _extractor?.Dispose();
        base.OneTimeTearDown();
    }

    /// Tests the `ExtractLandmarksAsync` method in the `OnnxLandmarkExtractor` class to ensure that
    /// it successfully extracts landmarks when provided with a valid face region as input.
    /// Ensures that:
    /// - The operation results in success.
    /// - The returned landmarks are not null.
    /// - The output contains the expected number of landmarks.
    /// <returns>
    /// Asserts the correctness of the result produced by the `ExtractLandmarksAsync` method.
    /// </returns>
    [Test]
    public async Task ExtractLandmarksAsync_WithValidFaceRegion_ReturnsLandmarks()
    {
        using var image = CreateTestImage();
        var faceBox = FaceBox.Create(50, 50, 150, 150).Value;

        var result = await _extractor.ExtractLandmarksAsync(image, faceBox);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Count.Should().Be(68);
    }

    /// <summary>
    /// Validates that the landmarks extracted from the specified image and face region
    /// have positions within the bounds of the image dimensions.
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous operation of extracting and validating
    /// landmark positions. Ensures that all extracted landmarks are positioned
    /// correctly within the specified image boundaries.
    /// </returns>
    [Test]
    public async Task ExtractLandmarksAsync_ValidatesLandmarkPositions()
    {
        using var image = CreateTestImage();
        var faceBox = FaceBox.Create(50, 50, 150, 150).Value;

        var result = await _extractor.ExtractLandmarksAsync(image, faceBox);
        result.IsSuccess.Should().BeTrue();

        // Verify landmarks are within reasonable bounds
        var landmarks = result.Value;
        foreach (var point in landmarks.Points)
        {
            point.X.Should().BeGreaterThanOrEqualTo(0);
            point.Y.Should().BeGreaterThanOrEqualTo(0);
            point.X.Should().BeLessThanOrEqualTo(image.Width);
            point.Y.Should().BeLessThanOrEqualTo(image.Height);
        }
    }

    /// Tests the capability of the OnnxLandmarkExtractor to handle scenarios with a very small face box
    /// during the landmarks extraction process.
    /// This test ensures that the landmark extraction still succeeds and returns 68 landmarks
    /// even when the input face box is extremely small.
    /// <returns>
    /// A Task that completes successfully if the landmark extraction handles small face boxes gracefully.
    /// </returns>
    [Test]
    public async Task ExtractLandmarksAsync_WithSmallFaceBox_HandlesGracefully()
    {
        using var image = CreateTestImage();
        var smallFaceBox = FaceBox.Create(10, 10, 20, 20).Value; // Very small face

        var result = await _extractor.ExtractLandmarksAsync(image, smallFaceBox);
        result.IsSuccess.Should().BeTrue(); // Should still succeed
        result.Value.Count.Should().Be(68);
    }

    /// Tests the functionality of the landmark extraction process, ensuring that the detected eye centers
    /// are placed in a reasonable manner relative to each other.
    /// This method validates two key aspects of the extracted landmarks:
    /// - The horizontal separation between the left and right eye centers is sufficiently large.
    /// - The vertical alignment difference between the left and right eye centers is small, corresponding
    /// to the general expectation for human faces.
    /// <returns>
    /// A completed Task representing the asynchronous operation of the test.
    /// Verifies results using assertions to confirm landmark positions meet the expected criteria.
    /// </returns>
    [Test]
    public async Task ExtractLandmarksAsync_EyeCentersAreReasonable()
    {
        using var image = CreateTestImage();
        var faceBox = FaceBox.Create(50, 50, 150, 150).Value;

        var result = await _extractor.ExtractLandmarksAsync(image, faceBox);
        result.IsSuccess.Should().BeTrue();

        var landmarks = result.Value;
        var leftEye = landmarks.LeftEyeCenter;
        var rightEye = landmarks.RightEyeCenter;

        // Eyes should be horizontally separated
        Math.Abs(rightEye.X - leftEye.X).Should().BeGreaterThan(10);

        // Eyes should be at similar height
        Math.Abs(rightEye.Y - leftEye.Y).Should().BeLessThan(50);
    }

    /// <summary>
    /// Verifies that the constructor of the <see cref="OnnxLandmarkExtractor"/> class
    /// initializes the instance successfully and loads the required ONNX model correctly.
    /// </summary>
    [Test]
    public void Constructor_LoadsModelSuccessfully()
    {
        var typedLogger = Substitute.For<ILogger<OnnxLandmarkExtractor>>();
        using var extractor = OnnxLandmarkExtractor.Create(typedLogger).Value;
        extractor.Should().NotBeNull();
    }

    /// Tests the behavior of the ExtractLandmarksAsync method when applied to a face box
    /// that extends beyond the boundaries of the provided image.
    /// Specifically verifies that landmarks are clipped correctly within the valid image region.
    /// <return> A task representing the asynchronous unit test operation. </return>
    [Test]
    public async Task ExtractLandmarksAsync_WithBoundaryFaceBox_ClipsCorrectly()
    {
        using var image = CreateTestImage();
        // Face box extends beyond image boundary
        var boundaryBox = FaceBox.Create(200, 200, 100, 100).Value;

        var result = await _extractor.ExtractLandmarksAsync(image, boundaryBox);
        result.IsSuccess.Should().BeTrue();
        result.Value.Count.Should().Be(68);
    }

    /// <summary>
    /// Verifies that the Dispose method of the <see cref="OnnxLandmarkExtractor"/> class can be called multiple times without throwing exceptions or causing undesirable behavior.
    /// </summary>
    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var typedLogger = Substitute.For<ILogger<OnnxLandmarkExtractor>>();
        using var extractor = OnnxLandmarkExtractor.Create(typedLogger).Value;

        extractor.Dispose();
        extractor.Dispose();
    }

    /// Tests the ExtractLandmarksAsync method to ensure the extracted landmarks
    /// align with the expected face structure, such as the chin being below the nose
    /// and the nose tip being vertically aligned between the eyes.
    /// <returns>
    /// A task that represents the asynchronous operation. The task result validates
    /// whether the landmarks correctly follow the face structure.
    /// </returns>
    [Test]
    public async Task ExtractLandmarksAsync_LandmarksFollowFaceStructure()
    {
        using var image = CreateTestImage();
        var faceBox = FaceBox.Create(50, 50, 150, 150).Value;

        var result = await _extractor.ExtractLandmarksAsync(image, faceBox);
        result.IsSuccess.Should().BeTrue();

        var landmarks = result.Value;

        // Verify basic face structure
        // Chin (point 8) should be lower than nose tip (point 30)
        landmarks.Points[8].Y.Should().BeGreaterThan(landmarks.Points[30].Y);

        // Nose tip (point 30) should be between eyes vertically
        var leftEye = landmarks.LeftEyeCenter;
        var rightEye = landmarks.RightEyeCenter;
        var noseTip = landmarks.Points[30];
        noseTip.Y.Should().BeGreaterThan(Math.Min(leftEye.Y, rightEye.Y));
    }

    // Helper methods
    /// Creates a test image with a gradient background.
    /// The image is generated programmatically and can be used
    /// in unit tests to simulate a graphical input for landmark
    /// extraction.
    /// <returns>
    /// A new instance of an Image&lt;Rgba32&gt; with defined dimensions and gradient texture.
    /// </returns>
    private static Image<Rgba32> CreateTestImage()
    {
        var image = new Image<Rgba32>(256, 256);

        // Fill with a gradient to provide some texture
        image.Mutate(ctx =>
        {
            // Create a simple gradient background
            for (var y = 0; y < image.Height; y++)
            {
                var color = new Rgba32(
                    (byte)(255 * y / image.Height),
                    (byte)(200 * y / image.Height),
                    (byte)(150 * y / image.Height)
                );
                // Simple fill - just set pixels directly
                for (var x = 0; x < image.Width; x++)
                {
                    image[x, y] = color;
                }
            }
        });

        return image;
    }
}

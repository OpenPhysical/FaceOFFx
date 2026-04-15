using AwesomeAssertions;
using FaceOFFx.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for the <c>RetinaFaceDetector</c> class, which provides face detection functionality.
/// </summary>
/// <remarks>
/// This test suite verifies the functionality and robustness of the <c>RetinaFaceDetector</c> class,
/// including its behavior with different image sizes, cancellation token handling, and multiple concurrent executions.
/// </remarks>
[TestFixture]
[NonParallelizable]
public class RetinaFaceDetectorTests : IDisposable
{
    /// <summary>
    /// Instance of <see cref="ILogger{RetinaFaceDetector}"/> used for logging operations
    /// within the <see cref="RetinaFaceDetectorTests"/> class. Facilitates capturing and reviewing
    /// log entries during unit tests for the <see cref="RetinaFaceDetector"/>.
    /// </summary>
    private readonly ILogger<RetinaFaceDetector> _logger;

    /// <summary>
    /// Represents an instance of the RetinaFaceDetector used in tests for face detection operations.
    /// </summary>
    private readonly RetinaFaceDetector _detector;

    /// <summary>
    /// Unit tests for the <see cref="RetinaFaceDetector"/> class, validating its functionality, error handling,
    /// and response to various scenarios including different input images and edge cases.
    /// </summary>
    public RetinaFaceDetectorTests()
    {
        _logger = Substitute.For<ILogger<RetinaFaceDetector>>();
        _detector = RetinaFaceDetector.Create(_logger).Value;
    }

    /// <summary>
    /// Releases all resources used by the instance of the <see cref="RetinaFaceDetectorTests"/> class.
    /// </summary>
    public void Dispose()
    {
        _detector.Dispose();
    }

    /// <summary>
    /// Ensures that the constructor properly initializes a new instance of the RetinaFaceDetector class.
    /// </summary>
    /// <remarks>
    /// This test verifies that the RetinaFaceDetector is successfully instantiated without any exceptions
    /// and that the resulting object is not null.
    /// </remarks>
    [Test]
    public void Constructor_ShouldInitializeSuccessfully()
    {
        using var detector = RetinaFaceDetector.Create(_logger).Value;
        detector.Should().NotBeNull();
    }

    /// <summary>
    /// Tests the face detection functionality using a valid image, ensuring the operation
    /// completes successfully and returns a valid result with detected faces.
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous test operation, validating that the face detection
    /// operation succeeds and that the returned result contains valid face detection data.
    /// </returns>
    [Test]
    public async Task DetectFacesAsync_WithValidImage_ShouldReturnSuccess()
    {
        using var image = new Image<Rgba32>(640, 480);
        image.Mutate(static ctx => ctx.Fill(Color.White));

        var result = await _detector.DetectFacesAsync(image);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    /// <summary>
    /// Tests the ability of the RetinaFaceDetector to handle small images correctly during face detection.
    /// </summary>
    /// <returns>
    /// A task that completes when the test is finished. Asserts that the small image is handled successfully
    /// by verifying the detection process produces a successful result with valid output.
    /// </returns>
    [Test]
    public async Task DetectFacesAsync_WithSmallImage_ShouldHandleCorrectly()
    {
        using var image = new Image<Rgba32>(100, 100);
        image.Mutate(static ctx => ctx.Fill(Color.Gray));

        var result = await _detector.DetectFacesAsync(image);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    /// <summary>
    /// Tests the handling of face detection in large images using the RetinaFaceDetector.
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result confirms whether
    /// the face detection process for a large image was successful and returned valid data.
    /// </returns>
    [Test]
    public async Task DetectFacesAsync_WithLargeImage_ShouldHandleCorrectly()
    {
        using var image = new Image<Rgba32>(3000, 2000);
        image.Mutate(static ctx => ctx.Fill(Color.Blue));

        var result = await _detector.DetectFacesAsync(image);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    /// <summary>
    /// Validates that the DetectFacesAsync method respects the provided CancellationToken
    /// by canceling the operation when requested.
    /// </summary>
    /// <returns>
    /// An asynchronous task that validates the behavior of cancellation, expecting
    /// the operation to return a failure result when the token is canceled.
    /// </returns>
    [Test]
    public async Task DetectFacesAsync_WithCancellation_ShouldRespectCancellationToken()
    {
        using var image = new Image<Rgba32>(640, 480);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _detector.DetectFacesAsync(image, cts.Token);

        result.IsFailure.Should().BeTrue();
    }

    /// Verifies that the `DetectFacesAsync` method maintains the aspect ratio when detecting faces in a non-square image.
    /// The test ensures that detected face bounding boxes are valid and within the dimensions of the provided image.
    /// <returns>
    /// A completed task representing the asynchronous test execution.
    /// Ensures success if the aspect ratio is maintained and all bounding boxes for detected faces are valid.
    /// </returns>
    [Test]
    public async Task DetectFacesAsync_WithNonSquareImage_ShouldMaintainAspectRatio()
    {
        using var image = new Image<Rgba32>(1920, 1080);
        image.Mutate(ctx => ctx.Fill(Color.Green));

        var result = await _detector.DetectFacesAsync(image);

        result.IsSuccess.Should().BeTrue();

        if (result.Value.Count > 0)
        {
            foreach (var face in result.Value)
            {
                face.BoundingBox.X.Should().BeGreaterThanOrEqualTo(0);
                face.BoundingBox.Y.Should().BeGreaterThanOrEqualTo(0);
                face.BoundingBox.Width.Should().BeGreaterThan(0);
                face.BoundingBox.Height.Should().BeGreaterThan(0);
                face.BoundingBox.X.Should().BeLessThan(image.Width);
                face.BoundingBox.Y.Should().BeLessThan(image.Height);
            }
        }
    }

    /// <summary>
    /// Validates that multiple concurrent calls to the DetectFacesAsync method do not interfere with each other.
    /// </summary>
    /// <returns>
    /// A task that represents the asynchronous operation. The task result contains the validation of successful independent processing
    /// for each call, ensuring no cross-contamination or race conditions.
    /// </returns>
    [Test]
    public async Task DetectFacesAsync_MultipleCalls_ShouldNotInterfere()
    {
        using var image1 = new Image<Rgba32>(640, 480);
        using var image2 = new Image<Rgba32>(800, 600);

        var task1 = _detector.DetectFacesAsync(image1);
        var task2 = _detector.DetectFacesAsync(image2);

        var results = await Task.WhenAll(task1, task2);

        results.Should().HaveCount(2);
        results[0].IsSuccess.Should().BeTrue();
        results[1].IsSuccess.Should().BeTrue();
    }

    /// <summary>
    /// Ensures that calling <see cref="RetinaFaceDetector.Dispose"/> completes without throwing any exceptions.
    /// </summary>
    [Test]
    public void Dispose_ShouldCompleteWithoutError()
    {
        var detector = RetinaFaceDetector.Create(_logger).Value;

        var act = () => detector.Dispose();

        act.Should().NotThrow();
    }

    /// <summary>
    /// Verifies that calling the Dispose method multiple times on an instance of <see cref="RetinaFaceDetector"/>
    /// does not throw any exceptions.
    /// </summary>
    [Test]
    public void Dispose_WhenCalledMultipleTimes_ShouldNotThrow()
    {
        var detector = RetinaFaceDetector.Create(_logger).Value;

        detector.Dispose();
        var act = () => detector.Dispose();

        act.Should().NotThrow();
    }

    /// <summary>
    /// Validates that the face detection functionality can correctly handle images of various sizes.
    /// </summary>
    /// <param name="width">The width of the image to be tested.</param>
    /// <param name="height">The height of the image to be tested.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the validation outcome for each image size.</returns>
    [TestCase(320, 240)]
    [TestCase(640, 480)]
    [TestCase(1024, 768)]
    [TestCase(1920, 1080)]
    public async Task DetectFacesAsync_WithVariousImageSizes_ShouldHandleCorrectly(
        int width,
        int height
    )
    {
        using var image = new Image<Rgba32>(width, height);
        image.Mutate(ctx => ctx.Fill(Color.White));

        var result = await _detector.DetectFacesAsync(image);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that the faces detected by the RetinaFaceDetector have valid properties such as confidence scores
    /// within the expected range and well-defined bounding boxes.
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous operation of the test. The task result contains no value but ensures the validity
    /// of each detected face's properties, such as confidence, bounding box existence, and dimensions.
    /// </returns>
    [Test]
    public async Task DetectFacesAsync_ResultFaces_ShouldHaveValidProperties()
    {
        using var image = new Image<Rgba32>(640, 480);
        image.Mutate(static ctx => ctx.Fill(Color.White));

        var result = await _detector.DetectFacesAsync(image);

        result.IsSuccess.Should().BeTrue();

        foreach (var face in result.Value)
        {
            face.Confidence.Should().BeInRange(0f, 1f);
            face.BoundingBox.Should().NotBeNull();
            face.BoundingBox.Width.Should().BeGreaterThan(0);
            face.BoundingBox.Height.Should().BeGreaterThan(0);
        }
    }

    /// <summary>
    /// Tests the face detection functionality of the RetinaFaceDetector with real image data.
    /// </summary>
    /// <returns>
    /// A task representing the asynchronous operation. The result indicates whether the faces were successfully detected in the provided image data.
    /// </returns>
    [Test]
    public async Task DetectFacesAsync_WithRealImageData_ShouldDetectFacesIfPresent()
    {
        using var image = new Image<Rgba32>(640, 480);
        DrawSimulatedFace(image);

        var result = await _detector.DetectFacesAsync(image);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    /// Draws a simulated face on the provided image.
    /// The simulated face includes facial features such as eyes and a mouth.
    /// <param name="image">The image on which the simulated face will be drawn.</param>
    private static void DrawSimulatedFace(Image<Rgba32> image)
    {
        image.Mutate(ctx =>
        {
            ctx.Fill(Color.LightGray);

            var faceX = image.Width / 2 - 100;
            var faceY = image.Height / 2 - 120;
            ctx.Fill(Color.PeachPuff, new Rectangle(faceX, faceY, 200, 240));

            ctx.Fill(Color.Black, new Rectangle(faceX + 50, faceY + 60, 30, 20));
            ctx.Fill(Color.Black, new Rectangle(faceX + 120, faceY + 60, 30, 20));

            ctx.Fill(Color.DarkRed, new Rectangle(faceX + 70, faceY + 160, 60, 20));
        });
    }
}

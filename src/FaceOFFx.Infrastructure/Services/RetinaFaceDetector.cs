using FaceOFFx.Models;
using FaceOFFx.Core.Domain.Common;
using JetBrains.Annotations;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FaceOFFx.Infrastructure.Services;

/// <summary>
/// RetinaFace face detector implementation
/// </summary>
[PublicAPI]
public sealed class RetinaFaceDetector : IFaceDetector, IDisposable
{
    private readonly ILogger<RetinaFaceDetector> _logger;
    private readonly InferenceSession _session;
    private bool _disposed;

    private const int ModelInputSize = 640;
    private const float ConfidenceThreshold = 0.9f; // High confidence threshold for quality faces
    private const float NmsThreshold = 0.4f;
    private const int TopK = 5000;
    private const int KeepTopK = 750;

    // RetinaFace normalization constants
    private static readonly float[] MeanValues = { 104f, 117f, 123f };

    // Variance values for decoding
    private static readonly float[] Variances = { 0.1f, 0.2f };

    private RetinaFaceDetector(ILogger<RetinaFaceDetector> logger, InferenceSession session)
    {
        _logger = logger;
        _session = session;
    }

    /// <summary>
    /// Creates a new instance of the <see cref="RetinaFaceDetector"/> class.
    /// </summary>
    /// <param name="logger">The logger instance for diagnostic output.</param>
    public static Result<RetinaFaceDetector, PipelineError> Create(ILogger<RetinaFaceDetector> logger)
    {
        try
        {
            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                EnableCpuMemArena = true,
                EnableMemoryPattern = true,
            };

            var modelBytesResult = ModelRegistry.TryGetModel(ModelRegistry.FaceDetector);
            if (modelBytesResult.IsFailure)
            {
                return Result.Failure<RetinaFaceDetector, PipelineError>(modelBytesResult.Error);
            }

            return Result.Success<RetinaFaceDetector, PipelineError>(
                new RetinaFaceDetector(logger, new InferenceSession(modelBytesResult.Value, sessionOptions)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize RetinaFace detector");
            return Result.Failure<RetinaFaceDetector, PipelineError>(
                new ConfigurationError($"Failed to initialize RetinaFace detector: {ex.Message}", "retinaface"));
        }
    }

    /// <summary>
    /// Detects faces in an image using RetinaFace model.
    /// </summary>
    /// <param name="image">The image to process.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result containing the list of detected faces or an error.</returns>
    /// <remarks>
    /// This implementation uses the RetinaFace model which provides:
    /// - Face bounding boxes
    /// - Detection confidence scores
    /// - 5-point facial landmarks (eyes, nose, mouth corners)
    /// The model processes images at 640x640 resolution.
    /// </remarks>
    public async Task<Result<IReadOnlyList<DetectedFace>, PipelineError>> DetectFacesAsync(
        Image<Rgba32> image,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogDebug(
            "DetectFacesAsync started with image size: {Width}x{Height}",
            image.Width,
            image.Height
        );

        // Prepare input tensor and get scale factors
        var (inputTensor, scaleX, scaleY) = PrepareInputTensor(image);

        // Get input name
        var inputName = _session.InputMetadata.Keys.First();

        // Create input
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputName, inputTensor),
        };

        // Run inference
        try
        {
            using var results = await Task.Run(() => _session.Run(inputs), cancellationToken)
                .ConfigureAwait(false);

            // Process outputs
            var detections = ProcessOutput(results, image.Width, image.Height, scaleX, scaleY);

            _logger.LogDebug("Detected {Count} faces", detections.Count);
            _logger.LogInformation(
                "DetectFacesAsync completed successfully with {Count} faces detected",
                detections.Count
            );

            return Result.Success<IReadOnlyList<DetectedFace>, PipelineError>(detections);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Face detection was cancelled");
            return Result.Failure<IReadOnlyList<DetectedFace>, PipelineError>(
                new DetectionError("Face detection was cancelled", "retinaface"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Face detection failed");
            return Result.Failure<IReadOnlyList<DetectedFace>, PipelineError>(
                new DetectionError($"Face detection failed: {ex.Message}", "retinaface"));
        }
    }

    /// <summary>
    /// Prepares an input tensor from the provided image for use in the RetinaFace model.
    /// </summary>
    /// <param name="image">The input image from which the tensor will be prepared.</param>
    /// <returns>The input tensor and scale factors for coordinate conversion.</returns>
    private (DenseTensor<float> tensor, float scaleX, float scaleY) PrepareInputTensor(
        Image<Rgba32> image
    )
    {
        // Work directly with ImageSharp Image
        using var img = image.Clone();

        // Calculate scale to fit in 640x640 while preserving aspect ratio
        var scaleX = (float)ModelInputSize / image.Width;
        var scaleY = (float)ModelInputSize / image.Height;
        var scale = Math.Min(scaleX, scaleY);

        var newWidth = (int)(image.Width * scale);
        var newHeight = (int)(image.Height * scale);

        // Resize image
        using var resizedImage = img.Clone(x => x.Resize(newWidth, newHeight));

        // Create 640x640 image with mean color background
        using var paddedImage = new Image<Rgba32>(ModelInputSize, ModelInputSize);

        // Calculate padding
        var padX = (ModelInputSize - newWidth) / 2;
        var padY = (ModelInputSize - newHeight) / 2;

        // Paste the resized image
        paddedImage.Mutate(ctx =>
        {
            ctx.DrawImage(resizedImage, new Point(padX, padY), 1f);
        });

        // Convert to tensor [1, 3, 640, 640] with mean subtraction
        var tensor = new DenseTensor<float>([1, 3, ModelInputSize, ModelInputSize]);

        for (var y = 0; y < ModelInputSize; y++)
        {
            for (var x = 0; x < ModelInputSize; x++)
            {
                var pixel = paddedImage[x, y];
                // Apply mean subtraction as in original RetinaFace
                tensor[0, 0, y, x] = pixel.R - MeanValues[0];
                tensor[0, 1, y, x] = pixel.G - MeanValues[1];
                tensor[0, 2, y, x] = pixel.B - MeanValues[2];
            }
        }

        return (tensor, scale, scale);
    }

    /// <summary>
    /// Processes the output of the RetinaFace detection model and converts it into a list of detected faces.
    /// </summary>
    /// <param name="results">The output from the RetinaFace model.</param>
    /// <param name="imageWidth">The width of the original input image.</param>
    /// <param name="imageHeight">The height of the original input image.</param>
    /// <param name="scaleX">The X scale factor used during preprocessing.</param>
    /// <param name="scaleY">The Y scale factor used during preprocessing.</param>
    /// <returns>A list of detected faces represented as <see cref="DetectedFace"/> objects.</returns>
    private List<DetectedFace> ProcessOutput(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
        int imageWidth,
        int imageHeight,
        float scaleX,
        float scaleY
    )
    {
        var detections = new List<DetectedFace>();

        // RetinaFace typically has 3 outputs: bbox, confidence, landmark
        var outputs = results.ToList();
        if (outputs.Count < 2)
        {
            _logger.LogWarning(
                "Unexpected number of outputs from RetinaFace model: {Count}",
                outputs.Count
            );
            return detections;
        }

        // Get output tensors
        var bboxOutput = outputs
            .FirstOrDefault(o => o.Name.Contains("bbox") || o.Name.Contains("loc"))
            ?.AsTensor<float>();
        var confOutput = outputs
            .FirstOrDefault(o => o.Name.Contains("conf") || o.Name.Contains("score"))
            ?.AsTensor<float>();
        var landmarkOutput = outputs
            .FirstOrDefault(o => o.Name.Contains("landmark") || o.Name.Contains("landm"))
            ?.AsTensor<float>();

        if (bboxOutput == null || confOutput == null)
        {
            _logger.LogWarning(
                "Could not find required outputs (bbox and confidence) from RetinaFace model"
            );
            return detections;
        }

        // Get dimensions
        var numDetections = confOutput.Dimensions[1];
        _logger.LogDebug("RetinaFace outputs - Num detections: {NumDetections}", numDetections);

        // Generate prior boxes (anchors)
        var priors = GeneratePriorBoxes(ModelInputSize, ModelInputSize);

        if (priors.Count != numDetections)
        {
            _logger.LogWarning(
                "Prior box count {PriorCount} doesn't match detection count {DetCount}",
                priors.Count,
                numDetections
            );
            return detections;
        }

        var candidateDetections =
            new List<(FaceBox box, float confidence, Maybe<FaceLandmarks5> landmarks)>();

        // Process each detection
        for (int i = 0; i < numDetections && i < priors.Count; i++)
        {
            // Get confidence score - RetinaFace typically uses sigmoid on output
            // Check both possible formats: [background, face] or just [face_score]
            float confidence;
            if (confOutput.Dimensions[2] == 2)
            {
                // Binary classification: background vs face
                confidence = confOutput[0, i, 1];
            }
            else
            {
                // Single score output
                confidence = confOutput[0, i, 0];
            }

            if (confidence < ConfidenceThreshold)
            {
                continue;
            }

            // Decode bounding box
            var prior = priors[i];
            var tx = bboxOutput[0, i, 0];
            var ty = bboxOutput[0, i, 1];
            var tw = bboxOutput[0, i, 2];
            var th = bboxOutput[0, i, 3];

            var decodedBox = DecodeBoundingBox(
                tx,
                ty,
                tw,
                th,
                prior.X,
                prior.Y,
                prior.Width,
                prior.Height
            );

            // Convert to image coordinates
            var scale = Math.Min(scaleX, scaleY);
            var padX = (ModelInputSize - imageWidth * scale) / 2f;
            var padY = (ModelInputSize - imageHeight * scale) / 2f;

            var x1 = Math.Max(0, (decodedBox.x1 - padX) / scale);
            var y1 = Math.Max(0, (decodedBox.y1 - padY) / scale);
            var x2 = Math.Min(imageWidth - 1, (decodedBox.x2 - padX) / scale);
            var y2 = Math.Min(imageHeight - 1, (decodedBox.y2 - padY) / scale);

            var width = x2 - x1;
            var height = y2 - y1;

            if (!(width > 0) || !(height > 0) || !(width >= 20) || !(height >= 20))
            {
                continue;
            }

            var faceBoxResult = FaceBox.Create((int)x1, (int)y1, (int)width, (int)height);
            if (!faceBoxResult.IsSuccess)
            {
                continue;
            }

            // Decode landmarks if available
            Maybe<FaceLandmarks5> landmarks = Maybe<FaceLandmarks5>.None;
            if (landmarkOutput != null && landmarkOutput.Dimensions.Length >= 3)
            {
                var landmarkTensor =
                    landmarkOutput as DenseTensor<float>
                    ?? new DenseTensor<float>(
                        landmarkOutput.ToArray(),
                        landmarkOutput.Dimensions.ToArray()
                    );
                var decodedLandmarks = DecodeLandmarks(
                    landmarkTensor,
                    i,
                    prior.X,
                    prior.Y,
                    prior.Width,
                    prior.Height
                );

                if (decodedLandmarks != null)
                {
                    // Convert landmark coordinates
                    landmarks = Maybe<FaceLandmarks5>.From(
                        new FaceLandmarks5(
                            new Point2D(
                                (decodedLandmarks[0] - padX) / scale,
                                (decodedLandmarks[1] - padY) / scale
                            ),
                            new Point2D(
                                (decodedLandmarks[2] - padX) / scale,
                                (decodedLandmarks[3] - padY) / scale
                            ),
                            new Point2D(
                                (decodedLandmarks[4] - padX) / scale,
                                (decodedLandmarks[5] - padY) / scale
                            ),
                            new Point2D(
                                (decodedLandmarks[6] - padX) / scale,
                                (decodedLandmarks[7] - padY) / scale
                            ),
                            new Point2D(
                                (decodedLandmarks[8] - padX) / scale,
                                (decodedLandmarks[9] - padY) / scale
                            )
                        )
                    );
                }
            }

            candidateDetections.Add((faceBoxResult.Value, confidence, landmarks));
        }

        // Apply NMS
        var nmsResults = ApplyNonMaxSuppression(candidateDetections, NmsThreshold);

        // Convert to DetectedFace objects
        foreach (var (box, confidence, landmarks) in nmsResults.Take(KeepTopK))
        {
            var detectedFace = new DetectedFace(box, confidence, landmarks);

            detections.Add(detectedFace);
        }

        return detections;
    }

    /// <summary>
    /// Generates prior boxes (anchors) for RetinaFace model.
    /// </summary>
    private List<(float X, float Y, float Width, float Height)> GeneratePriorBoxes(
        int imageWidth,
        int imageHeight
    )
    {
        var priors = new List<(float X, float Y, float Width, float Height)>();

        // RetinaFace uses multiple feature maps with different strides
        var minSizes = new[] { new[] { 16, 32 }, new[] { 64, 128 }, new[] { 256, 512 } };
        var steps = new[] { 8, 16, 32 };
        var featureMapSizes = new[]
        {
            new[] { imageHeight / steps[0], imageWidth / steps[0] },
            new[] { imageHeight / steps[1], imageWidth / steps[1] },
            new[] { imageHeight / steps[2], imageWidth / steps[2] },
        };

        for (int k = 0; k < minSizes.Length; k++)
        {
            var minSizeList = minSizes[k];
            var step = steps[k];
            var featureMapHeight = featureMapSizes[k][0];
            var featureMapWidth = featureMapSizes[k][1];

            for (int i = 0; i < featureMapHeight; i++)
            {
                for (int j = 0; j < featureMapWidth; j++)
                {
                    foreach (var minSize in minSizeList)
                    {
                        // Calculate center in pixel coordinates
                        var centerX = (j + 0.5f) * step;
                        var centerY = (i + 0.5f) * step;

                        // Size is just the minSize
                        var boxWidth = minSize;
                        var boxHeight = minSize;

                        // Normalize everything to [0, 1]
                        var normalizedCx = centerX / imageWidth;
                        var normalizedCy = centerY / imageHeight;
                        var normalizedW = (float)boxWidth / imageWidth;
                        var normalizedH = (float)boxHeight / imageHeight;

                        priors.Add((normalizedCx, normalizedCy, normalizedW, normalizedH));
                    }
                }
            }
        }

        return priors;
    }

    /// <summary>
    /// Decodes bounding box predictions using prior boxes.
    /// </summary>
    private (float x1, float y1, float x2, float y2) DecodeBoundingBox(
        float tx,
        float ty,
        float tw,
        float th,
        float priorCx,
        float priorCy,
        float priorW,
        float priorH
    )
    {
        // Decode center offset: prior center + prediction * variance * prior size
        var cx = priorCx + tx * Variances[0] * priorW;
        var cy = priorCy + ty * Variances[0] * priorH;

        // Decode size: prior size * exp(prediction * variance)
        var w = priorW * (float)Math.Exp(tw * Variances[1]);
        var h = priorH * (float)Math.Exp(th * Variances[1]);

        // Convert from center-size to corner format
        var x1 = cx - w / 2;
        var y1 = cy - h / 2;
        var x2 = cx + w / 2;
        var y2 = cy + h / 2;

        // Convert from normalized [0,1] to pixel coordinates
        x1 *= ModelInputSize;
        y1 *= ModelInputSize;
        x2 *= ModelInputSize;
        y2 *= ModelInputSize;

        return (x1, y1, x2, y2);
    }

    /// <summary>
    /// Decodes landmark predictions using prior boxes.
    /// </summary>
    private float[]? DecodeLandmarks(
        DenseTensor<float> landmarkOutput,
        int index,
        float priorCx,
        float priorCy,
        float priorW,
        float priorH
    )
    {
        var landmarks = new float[10]; // 5 landmarks * 2 coordinates

        for (int i = 0; i < 5; i++)
        {
            // Decode each landmark point offset in normalized space
            var lx = landmarkOutput[0, index, i * 2];
            var ly = landmarkOutput[0, index, i * 2 + 1];

            // Apply offset relative to prior box (in normalized coordinates)
            var normalizedX = priorCx + lx * Variances[0] * priorW;
            var normalizedY = priorCy + ly * Variances[0] * priorH;

            // Convert to pixel coordinates
            landmarks[i * 2] = normalizedX * ModelInputSize;
            landmarks[i * 2 + 1] = normalizedY * ModelInputSize;
        }

        return landmarks;
    }

    /// <summary>
    /// Applies Non-Max Suppression to reduce overlapping detections.
    /// </summary>
    private List<(
        FaceBox box,
        float confidence,
        Maybe<FaceLandmarks5> landmarks
    )> ApplyNonMaxSuppression(
        List<(FaceBox box, float confidence, Maybe<FaceLandmarks5> landmarks)> detections,
        float threshold
    )
    {
        if (detections.Count == 0)
        {
            return detections;
        }

        // Sort by confidence
        var sorted = detections.OrderByDescending(d => d.confidence).ToList();
        var selected = new List<(FaceBox, float, Maybe<FaceLandmarks5>)>();

        while (sorted.Count > 0)
        {
            var current = sorted[0];
            selected.Add(current);
            sorted.RemoveAt(0);

            // Remove overlapping boxes
            sorted.RemoveAll(detection =>
            {
                var iou = CalculateIoU(
                    current.box.X,
                    current.box.Y,
                    current.box.X + current.box.Width,
                    current.box.Y + current.box.Height,
                    detection.box.X,
                    detection.box.Y,
                    detection.box.X + detection.box.Width,
                    detection.box.Y + detection.box.Height
                );
                return iou > threshold;
            });
        }

        return selected;
    }

    /// <summary>
    /// Calculates the Intersection over Union (IoU) between two bounding boxes.
    /// </summary>
    private static float CalculateIoU(
        float x1a,
        float y1a,
        float x2a,
        float y2a,
        float x1b,
        float y1b,
        float x2b,
        float y2b
    )
    {
        var intersectionX1 = Math.Max(x1a, x1b);
        var intersectionY1 = Math.Max(y1a, y1b);
        var intersectionX2 = Math.Min(x2a, x2b);
        var intersectionY2 = Math.Min(y2a, y2b);

        if (intersectionX2 < intersectionX1 || intersectionY2 < intersectionY1)
        {
            return 0;
        }

        var intersectionArea =
            (intersectionX2 - intersectionX1) * (intersectionY2 - intersectionY1);
        var areaA = (x2a - x1a) * (y2a - y1a);
        var areaB = (x2b - x1b) * (y2b - y1b);
        var unionArea = areaA + areaB - intersectionArea;

        return intersectionArea / unionArea;
    }

    /// <summary>
    /// Disposes of the ONNX inference session and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            _logger.LogDebug("Dispose called but already disposed");
            return;
        }

        try
        {
            _logger.LogDebug("Disposing RetinaFace detector");
            _session?.Dispose();
            _logger.LogDebug("InferenceSession disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disposal");
        }
        finally
        {
            _disposed = true;
            GC.SuppressFinalize(this);
            _logger.LogInformation("RetinaFace detector disposed");
        }
    }
}

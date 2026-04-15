using FaceOFFx.Core.Abstractions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>
/// Encoding strategy for JPEG 2000 compression
/// </summary>
[PublicAPI]
public abstract record EncodingStrategy
{
    /// <summary>
    /// Creates a fixed compression rate strategy
    /// </summary>
    /// <param name="rate">Compression rate in bits per pixel</param>
    /// <returns>Fixed rate encoding strategy</returns>
    public static EncodingStrategy FixedRate(float rate) => new FixedRateStrategy(rate);

    /// <summary>
    /// Creates a target file size strategy
    /// </summary>
    /// <param name="sizeBytes">Target file size in bytes</param>
    /// <returns>Target size encoding strategy</returns>
    public static EncodingStrategy TargetSize(int sizeBytes) => new TargetSizeStrategy(sizeBytes);

    /// <summary>
    /// Execute the encoding strategy
    /// </summary>
    /// <param name="image">Image to encode</param>
    /// <param name="roiSet">ROI regions for prioritized encoding</param>
    /// <param name="encoder">JPEG 2000 encoder</param>
    /// <param name="options">Processing options</param>
    /// <param name="logger">Logger for debug output</param>
    /// <returns>Encoding result with compressed data and metadata</returns>
    public abstract Result<EncodingResult> Execute(
        Image<Rgba32> image,
        FacialRoiSet roiSet,
        IJpeg2000Encoder encoder,
        ProcessingOptions options,
        ILogger? logger = null
    );
}

/// <summary>
/// Fixed compression rate strategy
/// </summary>
/// <param name="Rate">Compression rate in bits per pixel</param>
[PublicAPI]
public sealed record FixedRateStrategy(float Rate) : EncodingStrategy
{
    /// <summary>
    /// Execute fixed rate encoding
    /// </summary>
    public override Result<EncodingResult> Execute(
        Image<Rgba32> image,
        FacialRoiSet roiSet,
        IJpeg2000Encoder encoder,
        ProcessingOptions options,
        ILogger? logger = null
    )
    {
        return encoder
            .EncodeWithRoi(
                image,
                roiSet,
                Rate,
                options.RoiStartLevel,
                options.EnableRoi,
                options.AlignRoi
            )
            .ToResult()
            .Map(data => new EncodingResult(data, Rate, Maybe<int>.None));
    }
}

/// <summary>
/// Target file size strategy with intelligent retry-based rate selection
/// </summary>
/// <param name="TargetBytes">Target file size in bytes</param>
/// <remarks>
/// This strategy uses an intelligent calculated approach rather than looping through all rates:
///
/// **Algorithm:**
/// 1. Add 5% safety margin to target size (e.g., 20,000 → 19,000 bytes)
/// 2. Map the adjusted target to an expected compression rate using empirical data
/// 3. Calculate retry distribution around the expected rate based on MaxRetries
/// 4. Test rates strategically: higher rates (expected to fail) → target rate → lower rates
/// 5. Return immediately on first success (since testing high-to-low, first success is optimal)
///
/// **Retry Distribution:**
/// - MaxRetries = 0: Single attempt using calculated expected rate
/// - MaxRetries > 0: Total tries = MaxRetries + 1
///   - Upper tries = Floor(total tries / 2) (rates above target - expected to exceed target)
///   - Lower tries = Ceiling(total tries / 2) (includes target rate and rates below - expected to succeed)
///
/// **Example: Target 20,000 bytes with MaxRetries = 4 (5 total tries):**
/// - Safety margin: 20,000 × 0.95 = 19,000 bytes
/// - Expected rate: 0.55 bpp (quantization level 4 - fits 19,000 bytes)
/// - Distribution: Floor(5/2) = 2 upper tries, Ceiling(5/2) = 3 lower tries
/// - Test sequence (Price is Right method - closest without going over):
///   1. 0.75 bpp (level 6) → ~23,570 bytes (upper try 2, exceeds 20,000 - continue)
///   2. 0.68 bpp (level 5) → ~20,650 bytes (upper try 1, exceeds 20,000 - continue)
///   3. 0.55 bpp (level 4) → ~17,700 bytes (target rate, fits under 20,000 - SUCCESS, exit here)
///   4. 0.46 bpp (level 3) → ~14,780 bytes (lower try 1, not tested - already found solution)
///   5. 0.36 bpp (level 2) → ~11,830 bytes (lower try 2, not tested - already found solution)
/// - Result: Use level 4 (0.55 bpp) producing ~17,700 bytes
///
/// This approach is much more efficient than testing all 19+ quantization levels.
/// </remarks>
[PublicAPI]
public sealed record TargetSizeStrategy(int TargetBytes) : EncodingStrategy
{
    /// <summary>
    /// Execute target size encoding using intelligent rate selection
    /// </summary>
    public override Result<EncodingResult> Execute(
        Image<Rgba32> image,
        FacialRoiSet roiSet,
        IJpeg2000Encoder encoder,
        ProcessingOptions options,
        ILogger? logger = null
    )
    {
        logger ??= NullLogger.Instance;

        // Step 1: Calculate target with 5% margin for safety
        var targetWithMargin = (int)(TargetBytes * 0.95f);
        logger.LogDebug(
            "TargetSizeStrategy: Original target={TargetBytes} bytes, with 5% margin={TargetWithMargin} bytes",
            TargetBytes,
            targetWithMargin
        );

        // Step 2: Find the expected rate for the target with margin
        var expectedRate = CompressionMapping.GetRateForTargetSize(targetWithMargin);
        var expectedIndex = CompressionMapping.GetIndexForRate(expectedRate);

        logger.LogDebug(
            "Expected rate for target: {ExpectedRate} bpp (index {ExpectedIndex})",
            expectedRate,
            expectedIndex
        );

        // Step 3: Calculate retry distribution
        var totalTries = options.MaxRetries + 1; // Add 1 for the base attempt

        if (totalTries == 1)
        {
            // Single try - use expected rate (calculated with 5% margin) directly
            logger.LogDebug(
                "Single try mode - using expected rate {ExpectedRate} (calculated for {TargetWithMargin} bytes)",
                expectedRate,
                targetWithMargin
            );
            return TryEncodeAndReturn(expectedRate, image, roiSet, encoder, options, logger);
        }

        // Multiple tries - distribute around expected rate
        var upperTries = (int)Math.Floor(totalTries / 2.0); // Floor for upper tries (above target, expected to exceed)
        var lowerTries = (int)Math.Ceiling(totalTries / 2.0); // Ceiling for lower tries (includes target, expected to succeed)

        logger.LogDebug(
            "Retry distribution: {TotalTries} total tries = {UpperTries} upper + {LowerTries} lower (includes target)",
            totalTries,
            upperTries,
            lowerTries
        );

        // Step 4: Calculate rate indices to test
        var ratesToTest = new List<(int Index, float Rate, string Purpose)>();
        var allRates = CompressionMapping.GetAllRates();

        // Add upper tries (higher rates, expected to exceed target)
        for (var i = 1; i <= upperTries; i++)
        {
            var upperIndex = Math.Min(expectedIndex + i, allRates.Length - 1);
            if (upperIndex < allRates.Length)
            {
                ratesToTest.Add(
                    (upperIndex, allRates[upperIndex], $"Upper try {i} (expected to exceed)")
                );
            }
        }

        // Add lower tries (at and below target rate, expected to succeed)
        for (var i = 0; i < lowerTries; i++)
        {
            var lowerIndex = Math.Max(expectedIndex - i, 0);
            if (lowerIndex >= 0)
            {
                var purpose =
                    i == 0
                        ? "Target rate (expected to succeed)"
                        : $"Lower try {i} (expected to succeed)";
                ratesToTest.Add((lowerIndex, allRates[lowerIndex], purpose));
            }
        }

        // Step 5: Test rates in order and stop at first success
        // Since we test from high to low rates, the first rate that fits is the best rate
        foreach (var (index, rate, purpose) in ratesToTest)
        {
            logger.LogDebug(
                "Testing {Purpose}: rate {Rate} bpp (index {Index})",
                purpose,
                rate,
                index
            );

            var result = TryEncodeAtRate(rate, image, roiSet, encoder, options, logger);
            if (result.HasValue)
            {
                var (encodedData, size) = result.Value;
                logger.LogDebug("Rate {Rate} produced {Size} bytes", rate, size);

                if (size <= TargetBytes)
                {
                    // Found a rate that fits - this is our result since we test high to low
                    logger.LogInformation(
                        "TargetSizeStrategy succeeded: {Size} bytes using {Rate} bpp (target was {TargetBytes})",
                        size,
                        rate,
                        TargetBytes
                    );
                    return Result.Success(new EncodingResult(encodedData, rate, TargetBytes));
                }
                else
                {
                    logger.LogDebug(
                        "Rate {Rate} exceeds target ({Size} > {TargetBytes}) - continuing to next rate",
                        rate,
                        size,
                        TargetBytes
                    );
                }
            }
            else
            {
                logger.LogWarning("Rate {Rate} encoding failed unexpectedly", rate);
            }
        }

        // Step 6: If we reach here, no rate worked
        logger.LogError(
            "TargetSizeStrategy failed: No rate produced a file size ≤ {TargetBytes} bytes",
            TargetBytes
        );
        return Result.Failure<EncodingResult>(
            $"Cannot compress image to {TargetBytes} bytes. Try a larger target size or reduce image complexity."
        );
    }

    /// <summary>
    /// Helper method for single-try encoding
    /// </summary>
    private static Result<EncodingResult> TryEncodeAndReturn(
        float rate,
        Image<Rgba32> image,
        FacialRoiSet roiSet,
        IJpeg2000Encoder encoder,
        ProcessingOptions options,
        ILogger logger
    )
    {
        var result = TryEncodeAtRate(rate, image, roiSet, encoder, options, logger);
        if (result.HasValue)
        {
            var (data, size) = result.Value;
            logger.LogInformation(
                "Single-try encoding succeeded: {Size} bytes using {Rate} bpp",
                size,
                rate
            );
            return Result.Success(new EncodingResult(data, rate, Maybe<int>.None));
        }

        logger.LogError("Single-try encoding failed at rate {Rate} bpp", rate);
        return Result.Failure<EncodingResult>($"Encoding failed at rate {rate} bpp");
    }

    /// <summary>
    /// Attempts to encode at a specific compression rate
    /// </summary>
    /// <param name="rate">Compression rate to try</param>
    /// <param name="image">Image to encode</param>
    /// <param name="roiSet">ROI regions</param>
    /// <param name="encoder">JPEG 2000 encoder</param>
    /// <param name="options">Processing options</param>
    /// <param name="logger">Logger for debug output</param>
    /// <returns>Encoded data and size if successful</returns>
    private static Maybe<(byte[] Data, int Size)> TryEncodeAtRate(
        float rate,
        Image<Rgba32> image,
        FacialRoiSet roiSet,
        IJpeg2000Encoder encoder,
        ProcessingOptions options,
        ILogger? logger = null
    )
    {
        logger ??= NullLogger.Instance;
        logger.LogDebug("TryEncodeAtRate: Calling encoder.EncodeWithRoi for rate {Rate}", rate);

        // Clone the image before encoding to prevent disposal issues
        // The JPEG 2000 encoder may dispose the image after encoding
        using var imageClone = image.Clone();
        logger.LogDebug("TryEncodeAtRate: Created image clone for rate {Rate}", rate);

        var result = encoder.EncodeWithRoi(
            imageClone,
            roiSet,
            rate,
            options.RoiStartLevel,
            options.EnableRoi,
            options.AlignRoi
        );

        if (result.IsFailure)
        {
            logger.LogDebug(
                "TryEncodeAtRate: Encoder returned failure for rate {Rate}: {Error}",
                rate,
                result.Error
            );
            return Maybe<(byte[], int)>.None;
        }

        var encodedData = result.Value;
        if (encodedData == null)
        {
            logger.LogDebug("TryEncodeAtRate: Encoder returned null data for rate {Rate}", rate);
            return Maybe<(byte[], int)>.None;
        }
        logger.LogDebug(
            "TryEncodeAtRate: Success for rate {Rate}, data length: {Length}",
            rate,
            encodedData.Length
        );
        return Maybe<(byte[], int)>.From((encodedData, encodedData.Length));
    }
}

/// <summary>
/// Result of encoding operation
/// </summary>
/// <param name="Data">Encoded image data</param>
/// <param name="ActualRate">Compression rate used</param>
/// <param name="TargetSize">Target size if using target size strategy</param>
[PublicAPI]
public sealed record EncodingResult(byte[] Data, float ActualRate, Maybe<int> TargetSize);

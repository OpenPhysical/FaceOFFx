using CSharpFunctionalExtensions;
using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;
using FaceOFFx.Core.Domain.Transformations;
using FaceOFFx.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.CodeAnalysis;

namespace FaceOFFx;

/// <summary>An immutable ceiling for the complete JPEG 2000 image, including its JP2 boxes.</summary>
/// <remarks>
/// Credential wrapping is verified separately. Targets reserve 46 FAC bytes, 88 CBEFF bytes
/// and 750 signature bytes, plus the size-dependent BC/FE tag encoding. The issuer verifies actual
/// signature length and available card capacity.
/// </remarks>
public sealed record PivFileSizeTarget
{
    /// <summary>Complete JP2 byte ceiling for minimum-capacity cards: 11,820 bytes.</summary>
    public static PivFileSizeTarget Minimum { get; } = new(11_820);
    /// <summary>Complete JP2 byte ceiling for a larger card allowance: 22,000 bytes.</summary>
    public static PivFileSizeTarget Preferred { get; } = new(22_000);
    /// <summary>Maximum length of the returned JP2 image.</summary>
    public int MaximumBytes { get; }
    /// <summary>BC capacity required by the JP2 ceiling and the 884-byte record/signature reservation.</summary>
    public int RequiredBiometricValueBytes => checked(MaximumBytes + 884);
    /// <summary>Object capacity required by the BC reservation and its BER length plus the mandatory FE tag.</summary>
    public int RequiredObjectBytes => checked(RequiredBiometricValueBytes + ObjectTagBytes);
    private int ObjectTagBytes => RequiredBiometricValueBytes <= 65_535 ? 6 :
        RequiredBiometricValueBytes <= 16_777_215 ? 7 : 8;

    private PivFileSizeTarget(int maximumBytes) => MaximumBytes = maximumBytes;

    /// <summary>Creates a custom complete-JP2 byte ceiling.</summary>
    /// <param name="maximumBytes">Positive JP2 allowance with room for up to 892 wrapper bytes in a signed 32-bit size.</param>
    /// <returns>A target using the same balanced recipe and geometry checks as the named targets.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The allowance is zero, negative, or exceeds the supported size arithmetic.</exception>
    public static PivFileSizeTarget FromBytes(int maximumBytes)
    {
        if (maximumBytes <= 0 || maximumBytes > int.MaxValue - 892)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), "Supply a positive JP2 allowance with room for credential-wrapper size arithmetic.");
        return maximumBytes switch
        {
            11_820 => Minimum,
            22_000 => Preferred,
            _ => new PivFileSizeTarget(maximumBytes)
        };
    }

    internal ProfileSpecification ResolveProfile()
    {
        var budget = new PivCardImageBudget(maximumBiometricValueBytes: RequiredBiometricValueBytes,
            maximumObjectBytes: RequiredObjectBytes, objectTagBytes: ObjectTagBytes);
        var id = MaximumBytes == Minimum.MaximumBytes ? "minimum" :
            MaximumBytes == Preferred.MaximumBytes ? "preferred" : "custom";
        var target = new ProfileFileSizeTarget("piv", id, $"PIV JP2 allowance: {MaximumBytes} bytes",
            MaximumBytes, "Required capacity arithmetic; issuer signature and available card storage require verification.")
        { CardBudget = budget };
        return ProfileSpecifications.Piv.WithFileSizeTarget(target).Value;
    }
}

/// <summary>The encoded PIV candidate and the evidence available for issuer review.</summary>
/// <remarks>
/// Successful encoding verifies the byte cap and source-supported geometry. Capture, anatomical measurements,
/// credential wrapping and acceptance of the recorded regional attribution method remain explicit review gates.
/// </remarks>
public sealed class PivEncodingResult
{
    /// <summary>Owned encoded JP2 bytes, ready for storage or independent decoding.</summary>
    public byte[] ImageData { get; }
    /// <summary>Encoded image media type.</summary>
    public string MimeType { get; }
    /// <summary>The complete JP2 allowance used for this image.</summary>
    public PivFileSizeTarget FileSizeTarget { get; }
    /// <summary>Stored image width and height.</summary>
    public ImageDimensions OutputDimensions { get; }
    /// <summary>Landmarks mapped through the actual crop and resize transform.</summary>
    public FaceLandmarks68 OutputLandmarks { get; }
    /// <summary>Applied in-plane source rotation in degrees.</summary>
    public float RotationDegrees { get; }
    /// <summary>Confidence reported by the source face detector.</summary>
    public float FaceConfidence { get; }
    /// <summary>Actual file, packet and operational regional-compression measurements.</summary>
    public EncodingDecision Encoding { get; }
    /// <summary>Source-supported crop candidates considered by the geometry solver.</summary>
    public IReadOnlyList<CandidateTrace> CandidateTraces { get; }
    /// <summary>Source color interpretation, including an explicit record of an untagged sRGB assumption.</summary>
    public PivSourceColorEvidence? SourceColorEvidence { get; }
    /// <summary>Estimated or measured geometry and remaining capture requirements.</summary>
    public PivGeometryEvidence? GeometryEvidence { get; }
    /// <summary>Fixed face-region construction and anatomical coverage review.</summary>
    public FacialRoiCoverage? RoiCoverage { get; }
    /// <summary>Distinct remaining issuer, capture, color and anatomical verification requirements.</summary>
    public IReadOnlyList<string> VerificationRequirements { get; }

    internal PivEncodingResult(ProfileEncodingResult result, PivFileSizeTarget target)
    {
        ImageData = result.ImageData;
        MimeType = result.MimeType;
        FileSizeTarget = target;
        OutputDimensions = result.OutputDimensions;
        OutputLandmarks = result.OutputLandmarks;
        RotationDegrees = result.RotationDegrees;
        FaceConfidence = result.FaceConfidence;
        Encoding = result.Encoding;
        CandidateTraces = Array.AsReadOnly(result.CandidateTraces.ToArray());
        SourceColorEvidence = result.SourceColorEvidence;
        GeometryEvidence = result.GeometryEvidence;
        RoiCoverage = result.RoiCoverage;
        VerificationRequirements = Array.AsReadOnly((GeometryEvidence?.VerificationRequirements ?? Array.Empty<string>())
            .Concat(RoiCoverage?.VerificationRequirements ?? Array.Empty<string>())
            .Concat(SourceColorEvidence?.VerificationRequirements ?? Array.Empty<string>())
            .Concat(new[]
            {
                "Verify actual issuer signature length, credential wrapping and available card storage for the selected JP2 allowance.",
                "Review the 24:1 face-region requirement using the recorded operational shared-wavelet attribution method and issuer acceptance policy."
            }).Distinct(StringComparer.Ordinal).ToArray());
    }
}

/// <summary>Prepares bounded PIV JPEG 2000 images from source bytes or files.</summary>
/// <remarks>Results retain geometry, regional-compression and source-review evidence for credential issuance.</remarks>
public sealed class PivImageEncoder : IDisposable
{
    private readonly OnnxFacialProcessingServiceFactory _services;
    private readonly ProfileEncoder _encoder;
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed",
        Justification = "Managed synchronization stays alive while queued calls unwind after disposal; AvailableWaitHandle is unused.")]
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lifecycle = new();
    private volatile bool _disposed;

    /// <summary>Creates a reusable local ONNX-backed PIV encoder.</summary>
    /// <param name="loggerFactory">Optional caller-owned logging factory; omitted values use silent logging.</param>
    /// <remarks>Calls on one encoder are serialized. Dispose the encoder after outstanding work completes.</remarks>
    public PivImageEncoder(ILoggerFactory? loggerFactory = null)
    {
        var logging = loggerFactory ?? NullLoggerFactory.Instance;
        _services = new OnnxFacialProcessingServiceFactory(logging);
        var geometry = new FaceGeometryPipeline(_services, logging.CreateLogger<FaceGeometryPipeline>());
        _encoder = new ProfileEncoder(geometry, _services, logging.CreateLogger<ProfileEncoder>());
    }

    /// <summary>Encodes source bytes with the fixed balanced recipe and a complete-JP2 byte ceiling.</summary>
    /// <param name="imageData">Single-frame source image bytes, preserved by the pipeline.</param>
    /// <param name="fileSizeTarget">Complete JP2 ceiling; omitted values use <see cref="PivFileSizeTarget.Minimum"/>.</param>
    /// <param name="cancellationToken">Cancels queued or ongoing processing.</param>
    /// <returns>A bounded PIV candidate and explicit review evidence, or a typed input, geometry or encoding error.</returns>
    /// <remarks>Untagged source samples are interpreted as sRGB and recorded as an assumption. Supported embedded RGB profiles are converted before analysis.</remarks>
    /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public async Task<Result<PivEncodingResult, PipelineError>> EncodeAsync(
        byte[] imageData, PivFileSizeTarget? fileSizeTarget = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var target = fileSizeTarget ?? PivFileSizeTarget.Minimum;
        if (imageData is null || imageData.Length == 0)
            return Result.Failure<PivEncodingResult, PipelineError>(new InputError("Supply source image bytes.", nameof(imageData)));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var result = await _encoder.ProcessAsync(imageData, target.ResolveProfile(), cancellationToken).ConfigureAwait(false);
            return result.IsFailure
                ? Result.Failure<PivEncodingResult, PipelineError>(result.Error)
                : Result.Success<PivEncodingResult, PipelineError>(new PivEncodingResult(result.Value, target));
        }
        finally { _gate.Release(); }
    }

    /// <summary>Reads a source photo and returns a bounded PIV candidate using the fixed balanced recipe.</summary>
    /// <param name="inputPath">Source image file, preserved unchanged.</param>
    /// <param name="fileSizeTarget">Complete JP2 ceiling; omitted values use <see cref="PivFileSizeTarget.Minimum"/>.</param>
    /// <param name="cancellationToken">Cancels file reading, queued work or processing.</param>
    /// <returns>The encoded candidate and review evidence, or a typed error.</returns>
    /// <exception cref="ObjectDisposedException">The encoder has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public async Task<Result<PivEncodingResult, PipelineError>> EncodeFileAsync(
        string inputPath, PivFileSizeTarget? fileSizeTarget = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(inputPath))
            return Result.Failure<PivEncodingResult, PipelineError>(new InputError("Supply a source image path.", nameof(inputPath)));
        try
        {
            var bytes = await File.ReadAllBytesAsync(inputPath, cancellationToken).ConfigureAwait(false);
            return await EncodeAsync(bytes, fileSizeTarget, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Result.Failure<PivEncodingResult, PipelineError>(new InputError($"Read the source image: {exception.Message}", inputPath));
        }
    }

    /// <summary>Releases the owned ONNX services after outstanding processing completes.</summary>
    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
            _gate.Wait();
            try { _services.Dispose(); }
            finally { _gate.Release(); }
        }
    }
}

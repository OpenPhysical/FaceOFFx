using FaceOFFx.Core.Domain.Common;
using FaceOFFx.Core.Domain.Detection;

namespace FaceOFFx.Core.Domain.Transformations;

/// <summary>Optional source-space anatomical measurements, with their recorded provenance.</summary>
public sealed record AnatomicalGeometry(
    Point2D? LeftEarAttachment = null,
    Point2D? RightEarAttachment = null,
    Point2D? Crown = null,
    string? MeasurementSource = null);

/// <summary>A source and output anatomical dimension, including its measurement basis.</summary>
public sealed record AnatomicalDimensionEvidence(
    float SourcePixels,
    float OutputPixels,
    GeometryEvidenceStatus Status,
    string Basis);

/// <summary>Geometry evidence for a PIV candidate and the remaining acquisition and anatomical checks.</summary>
public sealed record PivGeometryEvidence(
    float SourceJawWidth,
    float OutputJawWidth,
    AnatomicalDimensionEvidence HeadWidthCC,
    AnatomicalDimensionEvidence HeadHeightDD,
    float SourceRollDegrees,
    float EyeLineFromTopRatio,
    float SourceToOutputScale,
    bool OriginalPixelSupportVerified,
    IReadOnlyList<string> VerificationRequirements);

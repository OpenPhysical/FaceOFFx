namespace FaceOFFx.Core.Domain.Detection;

/// <summary>Fixed spatial definitions for the face-centered innermost JPEG 2000 region.</summary>
public enum PivFaceRegion
{
    /// <summary>All 68 detected features, brows, and jaw with a fixed localization margin.</summary>
    LandmarkFace,
    /// <summary>The landmark region plus fixed estimated forehead and ear envelopes.</summary>
    ExtendedFace
}

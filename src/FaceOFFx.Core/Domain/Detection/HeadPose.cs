namespace FaceOFFx.Core.Domain.Detection;

/// <summary>
/// Represents the three-dimensional orientation of a human head using Euler angles.
/// </summary>
/// <param name="Yaw">
/// The left/right rotation angle in degrees. Positive values indicate the head turning right,
/// negative values indicate turning left. Range is typically -90 to +90 degrees.
/// </param>
/// <param name="Pitch">
/// The up/down rotation angle in degrees. Positive values indicate the head tilting up,
/// negative values indicate tilting down. Range is typically -90 to +90 degrees.
/// </param>
/// <param name="Roll">
/// The tilt rotation angle in degrees. Positive values indicate the head tilting to the right shoulder,
/// negative values indicate tilting to the left shoulder. Range is typically -180 to +180 degrees.
/// </param>
/// <remarks>
/// <para>
/// Head pose estimation is crucial for many facial analysis applications including:
/// - Face recognition (frontal faces perform better)
/// - Attention monitoring and gaze estimation
/// - PIV/biometric image compliance checking
/// - Driver monitoring systems
/// </para>
/// <para>
/// The coordinate system follows the convention where the person is facing the camera:
/// - Yaw: Rotation around the vertical Y-axis
/// - Pitch: Rotation around the horizontal X-axis
/// - Roll: Rotation around the Z-axis (depth)
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Check if a detected face is suitable for recognition
/// var headPose = new HeadPose(yaw: 5f, pitch: -10f, roll: 2f);
///
/// if (headPose.IsFrontal(maxDeviationDegrees: 20f))
/// {
///     Console.WriteLine("Face is frontal enough for recognition");
/// }
/// else
/// {
///     Console.WriteLine($"Face is turned: Yaw={headPose.Yaw}°, Pitch={headPose.Pitch}°");
/// }
///
/// // Check for specific pose conditions
/// bool isLookingDown = headPose.Pitch &lt; -30f;
/// bool isProfileView = Math.Abs(headPose.Yaw) &gt; 60f;
/// </code>
/// </example>
public sealed record HeadPose(float Yaw, float Pitch, float Roll)
{
    /// <summary>
    /// Gets a head pose representing a neutral, forward-facing orientation.
    /// </summary>
    /// <value>A HeadPose with all angles set to 0 degrees.</value>
    /// <remarks>
    /// Use this as a reference point or default value when no head pose information
    /// is available, or to represent an ideal frontal face position.
    /// </remarks>
    public static HeadPose Neutral => new(0, 0, 0);

    /// <summary>
    /// Determines whether the head pose is approximately frontal based on deviation thresholds.
    /// </summary>
    /// <param name="maxDeviationDegrees">
    /// The maximum allowed deviation in degrees for each angle to still be considered frontal.
    /// Default is 15 degrees.
    /// </param>
    /// <returns>
    /// <c>true</c> if all three rotation angles are within the specified deviation from neutral;
    /// otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method is useful for filtering faces for applications that require frontal views,
    /// such as face recognition and PIV image capture review. Supply angles from a validated estimator.
    /// </para>
    /// <para>
    /// Common threshold values:
    /// - 10-15°: Strict frontal requirement (biometric applications)
    /// - 20-25°: Moderate frontal requirement (general recognition)
    /// - 30-45°: Relaxed requirement (face detection only)
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var pose = new HeadPose(yaw: 12f, pitch: 8f, roll: 3f);
    ///
    /// // Strict check for biometric capture
    /// bool isStrictlyFrontal = pose.IsFrontal(10f);  // returns false
    ///
    /// // Relaxed check for detection
    /// bool isGenerallyFrontal = pose.IsFrontal(15f); // returns true
    /// </code>
    /// </example>
    public bool IsFrontal(float maxDeviationDegrees = 15f) =>
        Math.Abs(Yaw) <= maxDeviationDegrees
        && Math.Abs(Pitch) <= maxDeviationDegrees
        && Math.Abs(Roll) <= maxDeviationDegrees;
}

using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Three-Stage Spatial State Filter for ARCore Image Tracking stabilization.
/// Decouples the 3D Text Quad from raw 60Hz ARCore transform hierarchy by routing
/// tracking updates through:
///   1. Gating Pipeline (TrackingState validation & grace period hold)
///   2. Deadzone Comparator (Hysteresis thresholding to eliminate stationary micro-jitter)
///   3. Adaptive Exponential Moving Average (EMA) Smoother with frame-rate independent decay
/// </summary>
public class ARSpatialPoseFilter : MonoBehaviour
{
    [Header("Stage 1: Gating & Grace Period")]
    [Tooltip("Time in seconds to hold the last valid pose during brief camera occlusion or tracking drops")]
    [SerializeField] private float trackingLossGracePeriod = 0.45f;

    [Header("Stage 2: Deadzone Thresholds (Zero-Jitter Lock)")]
    [Tooltip("Movement below this distance (meters) is treated as sensor noise (avoids boundary chatter)")]
    [SerializeField] private float positionDeadzoneMeters = 0.0045f; // 4.5 mm

    [Tooltip("Rotation below this angle (degrees) is treated as sensor noise (avoids normal flutter)")]
    [SerializeField] private float rotationDeadzoneDegrees = 1.25f;   // 1.25°

    [Header("Stage 3: Calibrated Damped EMA Smoothing")]
    [Tooltip("Position smoothing speed when holding steady (rock-solid hold)")]
    [SerializeField] private float minSmoothSpeed = 6f;

    [Tooltip("Position smoothing speed when user moves the phone / book (smooth tracking)")]
    [SerializeField] private float maxSmoothSpeed = 16f;

    [Tooltip("Rotation smoothing speed when stationary")]
    [SerializeField] private float minRotSmoothSpeed = 7f;

    [Tooltip("Rotation smoothing speed when tilting phone / book")]
    [SerializeField] private float maxRotSmoothSpeed = 18f;

    [Tooltip("Distance delta threshold (meters) where max position smoothing speed is reached")]
    [SerializeField] private float fastMoveThreshold = 0.10f; // 10 cm

    [Tooltip("Angular delta threshold (degrees) where max rotation smoothing speed is reached")]
    [SerializeField] private float fastRotThreshold = 8.0f; // 8 degrees

    [Header("Surface Alignment Offset")]
    [Tooltip("Ultra-thin normal offset above paper surface to eliminate z-fighting without parallax float (meters)")]
    [SerializeField] private float normalOffsetMeters = 0.00035f; // 0.35 mm

    private ARTrackedImage currentTarget;
    private Vector3 lockedTargetPosition;
    private Quaternion lockedTargetRotation;
    private float lastValidTrackingTime;
    private bool isInitialized = false;

    /// <summary>
    /// Sets a new ARTrackedImage target. Snaps pose immediately on first acquisition.
    /// </summary>
    public void SetTarget(ARTrackedImage target)
    {
        if (target == null) return;

        bool isNewTarget = (currentTarget != target);
        currentTarget = target;

        if (isNewTarget)
        {
            isInitialized = false;
        }

        if (target.trackingState == TrackingState.Tracking)
        {
            lastValidTrackingTime = Time.time;
            ComputeRawTargetPose(out Vector3 rawPos, out Quaternion rawRot);

            lockedTargetPosition = rawPos;
            lockedTargetRotation = rawRot;

            // First-Frame Hard Snap: instantly snap position and rotation without gliding from (0,0,0) or stale pose
            if (!isInitialized)
            {
                transform.position = lockedTargetPosition;
                transform.rotation = lockedTargetRotation;
                isInitialized = true;
            }
        }
    }

    /// <summary>
    /// Clears the tracking target and resets filter state.
    /// </summary>
    public void ClearTarget()
    {
        currentTarget = null;
        isInitialized = false;
    }

    private void LateUpdate()
    {
        if (currentTarget == null) return;

        // ─── STAGE 1: Gating Pipeline ──────────────────────────────────────────
        bool isCurrentlyTracking = (currentTarget.trackingState == TrackingState.Tracking);

        if (isCurrentlyTracking)
        {
            lastValidTrackingTime = Time.time;
        }
        else
        {
            // If tracking is lost and grace period expired, hold position but do not update
            if (Time.time - lastValidTrackingTime > trackingLossGracePeriod)
            {
                return;
            }
        }

        ComputeRawTargetPose(out Vector3 rawPos, out Quaternion rawRot);

        // First-frame initialization guard
        if (!isInitialized)
        {
            lockedTargetPosition = rawPos;
            lockedTargetRotation = rawRot;
            transform.position = rawPos;
            transform.rotation = rawRot;
            isInitialized = true;
            return;
        }

        // ─── STAGE 2: Deadzone Comparator (Hysteresis) ────────────────────────
        float posDelta = Vector3.Distance(rawPos, lockedTargetPosition);
        float rotDelta = Quaternion.Angle(rawRot, lockedTargetRotation);

        // Only update locked target pose if sensor movement exceeds the physical noise deadzone
        if (posDelta >= positionDeadzoneMeters)
        {
            lockedTargetPosition = rawPos;
        }

        if (rotDelta >= rotationDeadzoneDegrees)
        {
            lockedTargetRotation = rawRot;
        }

        // ─── STAGE 3: Well-Damped Adaptive EMA Smoother ───────────────────────
        // 3a. Position Smoothing (Clamped monotonic ratio prevents flutter)
        float distanceToTarget = Vector3.Distance(transform.position, lockedTargetPosition);
        float posRatio = Mathf.Clamp01(distanceToTarget / fastMoveThreshold);
        float currentPosLerpSpeed = Mathf.Lerp(minSmoothSpeed, maxSmoothSpeed, posRatio);
        float alphaPos = 1.0f - Mathf.Exp(-currentPosLerpSpeed * Time.deltaTime);

        // 3b. Rotation Smoothing (Clamped monotonic ratio avoids over-correction oscillation)
        float angleToTarget = Quaternion.Angle(transform.rotation, lockedTargetRotation);
        float rotRatio = Mathf.Clamp01(angleToTarget / fastRotThreshold);
        float currentRotLerpSpeed = Mathf.Lerp(minRotSmoothSpeed, maxRotSmoothSpeed, rotRatio);
        float alphaRot = 1.0f - Mathf.Exp(-currentRotLerpSpeed * Time.deltaTime);

        // Apply frame-rate independent damped exponential smoothing
        transform.position = Vector3.Lerp(transform.position, lockedTargetPosition, alphaPos);
        transform.rotation = Quaternion.Slerp(transform.rotation, lockedTargetRotation, alphaRot);
    }

    private void ComputeRawTargetPose(out Vector3 rawPos, out Quaternion rawRot)
    {
        // ARTrackedImage local Y is the surface normal in AR Foundation (paper facing outward)
        // Offset slightly along surface normal (flush 0.35mm offset to prevent z-fighting)
        rawPos = currentTarget.transform.position + (currentTarget.transform.up * normalOffsetMeters);
        rawRot = currentTarget.transform.rotation * Quaternion.Euler(90f, 0f, 0f);
    }
}

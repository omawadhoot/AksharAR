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
    [Tooltip("Movement below this distance (meters) is treated as sensor noise")]
    [SerializeField] private float positionDeadzoneMeters = 0.0025f; // 2.5 mm

    [Tooltip("Rotation below this angle (degrees) is treated as sensor noise")]
    [SerializeField] private float rotationDeadzoneDegrees = 0.60f;   // 0.60°

    [Header("Stage 3: Adaptive EMA Smoothing")]
    [Tooltip("Smoothing speed when moving slowly / holding still (silky smooth)")]
    [SerializeField] private float minSmoothSpeed = 12f;

    [Tooltip("Smoothing speed when user moves the phone / book quickly (snappy, zero lag)")]
    [SerializeField] private float maxSmoothSpeed = 30f;

    [Tooltip("Velocity threshold (m/s) where max smoothing speed is fully engaged")]
    [SerializeField] private float fastMoveThreshold = 0.08f; // 8 cm/s

    [Header("Surface Alignment Offset")]
    [Tooltip("Slight normal offset above paper surface to prevent z-fighting (meters)")]
    [SerializeField] private float normalOffsetMeters = 0.001f; // 1 mm

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

            // First-Frame Snap: instantly snap position and rotation without gliding from (0,0,0)
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

        // ─── STAGE 2: Deadzone Comparator ─────────────────────────────────────
        float posDelta = Vector3.Distance(rawPos, lockedTargetPosition);
        float rotDelta = Quaternion.Angle(rawRot, lockedTargetRotation);

        // Only accept new target pose if movement exceeds the physical noise deadzone
        if (posDelta >= positionDeadzoneMeters)
        {
            lockedTargetPosition = rawPos;
        }

        if (rotDelta >= rotationDeadzoneDegrees)
        {
            lockedTargetRotation = rawRot;
        }

        // ─── STAGE 3: Frame-Rate Independent Adaptive EMA Smoother ────────────
        float distanceToTarget = Vector3.Distance(transform.position, lockedTargetPosition);
        float speedFactor = Mathf.Clamp01(distanceToTarget / fastMoveThreshold);
        float currentLerpSpeed = Mathf.Lerp(minSmoothSpeed, maxSmoothSpeed, speedFactor);

        // Frame-rate independent exponential decay smoothing: alpha = 1 - e^(-lambda * dt)
        float alpha = 1.0f - Mathf.Exp(-currentLerpSpeed * Time.deltaTime);

        transform.position = Vector3.Lerp(transform.position, lockedTargetPosition, alpha);
        transform.rotation = Quaternion.Slerp(transform.rotation, lockedTargetRotation, alpha);
    }

    private void ComputeRawTargetPose(out Vector3 rawPos, out Quaternion rawRot)
    {
        // ARTrackedImage local Y is the surface normal in AR Foundation (paper facing outward)
        // Offset slightly along surface normal (+Z offset in quad space)
        rawPos = currentTarget.transform.position + (currentTarget.transform.up * normalOffsetMeters);
        rawRot = currentTarget.transform.rotation * Quaternion.Euler(90f, 0f, 0f);
    }
}

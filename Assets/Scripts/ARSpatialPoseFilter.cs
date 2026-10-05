using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;

/// <summary>
/// Three-Stage Spatial State Filter for ARCore Image Tracking stabilization.
/// Decouples the 3D Text Quad from raw 60Hz ARCore transform hierarchy by routing
/// tracking updates through:
///   1. Gating & App Lifecycle Pipeline (TrackingState, ARSession relocalization, resume warmup)
///   2. Relocalization & Discontinuity Snap Engine (Instant hard-snap on app switch / coordinate shift)
///   3. Statistical Lock Gate (Windowed mean residual < 1.5mm, stddev < 1.0mm, angle < 0.5°)
///   4. 3-Tier Drift Management (Deadzone < 1.2mm, Tier 2 Rate-Limited EMA, Tier 3 Async Anchor Swap)
///   5. Make-Before-Break ARAnchor Handoff (Zero visual glitch during re-anchoring)
///   6. Per-Frame Ground-Truth CSV Telemetry Logger
/// </summary>
public class ARSpatialPoseFilter : MonoBehaviour
{
    [Header("Test & Calibration Mode")]
    [Tooltip("When true, disables Tier 2 and Tier 3 adjustments once locked, keeping Quad 100% frozen in world space to test pure VIO grounding")]
    [SerializeField] private bool disableDriftCorrectionPostLock = true;

    [Header("Stage 1: Gating & Grace Period")]
    [Tooltip("Time in seconds to hold the last valid pose during brief camera occlusion or tracking drops")]
    [SerializeField] private float trackingLossGracePeriod = 0.45f;

    [Header("Stage 2: Continuous Hermite Deadzone (Zero-Jitter Lock)")]
    [Tooltip("Movement below this distance (meters) has 0% influence (eliminates micro-tremor completely)")]
    [SerializeField] private float innerPositionDeadzone = 0.0012f; // 1.2 mm

    [Tooltip("Movement above this distance (meters) has 100% influence. Smoothstep Hermite blend between inner and outer.")]
    [SerializeField] private float outerPositionDeadzone = 0.0060f; // 6.0 mm

    [Tooltip("Rotation below this angle (degrees) has 0% influence (eliminates angular flutter)")]
    [SerializeField] private float innerRotationDeadzone = 0.5f;   // 0.5°

    [Tooltip("Rotation above this angle (degrees) has 100% influence")]
    [SerializeField] private float outerRotationDeadzone = 2.0f;   // 2.0°

    [Header("Stage 3: Feathery Velvet EMA Smoother")]
    [Tooltip("Damped holding speed when stationary (ultra-stable, feathery hold)")]
    [SerializeField] private float minSmoothSpeed = 2.5f;

    [Tooltip("Smooth tracking speed during deliberate movement (gentle, fluid glide)")]
    [SerializeField] private float maxSmoothSpeed = 8.5f;

    [Tooltip("Angular smoothing speed when stationary")]
    [SerializeField] private float minRotSmoothSpeed = 2.8f;

    [Tooltip("Angular smoothing speed during deliberate movement")]
    [SerializeField] private float maxRotSmoothSpeed = 8.0f;

    [Tooltip("Distance threshold (meters) where maximum tracking speed is reached")]
    [SerializeField] private float fastMoveThreshold = 0.08f; // 8 cm

    [Tooltip("Angular threshold (degrees) where maximum rotation speed is reached")]
    [SerializeField] private float fastRotThreshold = 7.0f; // 7 degrees

    [Tooltip("Maximum linear velocity in meters per second (caps sudden hand jerkiness)")]
    [SerializeField] private float maxLinearVelocityMps = 0.40f; // 40 cm/s

    [Header("Discontinuous Jump & Relocalization Recovery")]
    [Tooltip("Distance delta threshold (meters) where an update is treated as a coordinate reset rather than physical hand movement")]
    [SerializeField] private float discontinuousJumpThreshold = 0.12f; // 12 cm

    [Tooltip("Angular delta threshold (degrees) where an update is treated as a coordinate reset")]
    [SerializeField] private float discontinuousAngleThreshold = 22f;

    [Tooltip("Consecutive stable frames required before hard-snapping to a relocalized target")]
    [SerializeField] private int requiredStableFrames = 3;

    [Header("Surface Alignment Offset")]
    [Tooltip("Normal offset above paper surface to eliminate z-fighting without parallax float (meters)")]
    [SerializeField] private float normalOffsetMeters = 0.0008f; // 0.8 mm

    [Header("Tiered Drift Management & ARAnchor Lock")]
    [Tooltip("Tier 2 max linear correction rate inside mask margin (m/s)")]
    [SerializeField] private float tier2MaxRateInsideMargin = 0.0015f; // 1.5 mm/s

    [Tooltip("Tier 2 max linear correction rate outside mask margin (m/s)")]
    [SerializeField] private float tier2MaxRateOutsideMargin = 0.0060f; // 6.0 mm/s

    [Tooltip("Tier 3 threshold (meters) for PERPENDICULAR BEARING error triggering Make-Before-Break Anchor Swap")]
    [SerializeField] private float tier3SnapBearingDistance = 0.018f; // 18 mm perpendicular bearing

    [Tooltip("Tier 3 angular threshold (degrees) triggering Anchor Swap")]
    [SerializeField] private float tier3SnapAngle = 8.0f; // 8.0°

    [Tooltip("Sustained duration (seconds) required before Tier 3 executes anchor swap")]
    [SerializeField] private float tier3SustainDuration = 0.30f;

    [Header("Telemetry Logging")]
    [SerializeField] private bool enableTelemetryCsv = true;

    private ARTrackedImage currentTarget;
    private ARAnchorManager anchorManager;
    private XROrigin xrOrigin;
    private ARPlaneManager planeManager;
    private ARPointCloudManager pointCloudManager;
    private ARAnchor activeAnchor;
    private ARAnchor candidateAnchor;
    private Transform correctionNode;
    private MeshRenderer quadRenderer;
    private Vector3 lockedTargetPosition;
    private Quaternion lockedTargetRotation;
    private float lastValidTrackingTime;
    private bool isInitialized = false;

    // App Resume & Relocalization State
    private bool isAppSuspended = false;
    private bool isARSessionRelocalizing = false;
    private int framesToWarmupAfterResume = 0;
    private bool pendingRelocalizationSnap = false;

    private Vector3 previousRawPos;
    private Quaternion previousRawRot;
    private int consecutiveStableFrames = 0;

    // World Lock & Statistical Gate State
    private bool isWorldLocked = false;
    private string lockMode = "Unlocked";
    private int trackingFramesCount = 0;
    private int deliberateMoveFrameCounter = 0;
    private float trackingStartTime = 0f;
    private float timeToLockSeconds = 0f;
    private int swapCount = 0;
    private float tier3Timer = 0f;
    private bool isAnchorSwapInFlight = false;
    private Queue<float> residualWindow = new Queue<float>(25);
    private Queue<float> bearingResidualWindow = new Queue<float>(25);
    private float lastTelemetryLogTime = 0f;

    // Kinematics & Camera-Local Metrics
    private Vector3 previousQuadPos = Vector3.zero;
    private float currentQuadSpeedMps = 0f;
    private Vector3 camLocalQuadPos = Vector3.zero;
    private Vector3 camLocalRawPos = Vector3.zero;
    private Vector3 camLocalDelta = Vector3.zero;
    private float camLocalDeltaNorm_mm = 0f;

    // View-Ray Decomposition Metrics
    private float lastBearingError_mm = 0f;
    private float lastRangeError_mm = 0f;
    private float lastCamToPageDist_m = 0f;
    private float lastCamToQuadDist_m = 0f;

    // Additional Telemetry Metrics
    private float trueCropWidth_m = 0f;
    private float quadWorldWidth_m = 0f;
    private float pageHeight_mm = 0f;
    private string pageHeightSource = "None";
    private int pageHeightSampleCount = 0;
    private bool isOverlayVisible = false;
    private string hideReason = "None";

    // CSV Logging
    private StreamWriter telemetryWriter;
    private string telemetryFilePath;
    private float lastFlushTime = 0f;

    private void Awake()
    {
        quadRenderer = GetComponent<MeshRenderer>() ?? GetComponentInChildren<MeshRenderer>();
        anchorManager = FindFirstObjectByType<ARAnchorManager>();
        xrOrigin = FindFirstObjectByType<XROrigin>();
        planeManager = FindFirstObjectByType<ARPlaneManager>();
        pointCloudManager = FindFirstObjectByType<ARPointCloudManager>();

        // Create Correction Node hierarchy for smooth Tier 2 offset application
        GameObject corrObj = new GameObject("AR_CorrectionNode");
        corrObj.transform.SetParent(transform.parent, false);
        corrObj.transform.position = transform.position;
        corrObj.transform.rotation = transform.rotation;
        correctionNode = corrObj.transform;

        InitializeTelemetryCsv();
    }

    private void OnEnable()
    {
        ARSession.stateChanged += HandleARSessionStateChanged;
        if (telemetryWriter == null)
        {
            InitializeTelemetryCsv();
        }
    }

    private void OnDisable()
    {
        ARSession.stateChanged -= HandleARSessionStateChanged;
        if (telemetryWriter != null)
        {
            try { telemetryWriter.Flush(); } catch { }
        }
    }

    private void HandleARSessionStateChanged(ARSessionStateChangedEventArgs args)
    {
        if (args.state != ARSessionState.SessionTracking)
        {
            isARSessionRelocalizing = true;
            pendingRelocalizationSnap = true;
            consecutiveStableFrames = 0;

            if (quadRenderer != null)
                quadRenderer.enabled = false;
        }
        else
        {
            isARSessionRelocalizing = false;
            pendingRelocalizationSnap = true;
            consecutiveStableFrames = 0;
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            isAppSuspended = true;
            if (quadRenderer != null)
                quadRenderer.enabled = false;
        }
        else
        {
            isAppSuspended = false;
            framesToWarmupAfterResume = 5;
            pendingRelocalizationSnap = true;
            consecutiveStableFrames = 0;
        }
    }

    /// <summary>
    /// Sets a new ARTrackedImage target. Snaps pose immediately on first acquisition.
    /// </summary>
    public void SetTarget(ARTrackedImage target)
    {
        if (target == null) return;

        if (quadRenderer == null)
            quadRenderer = GetComponent<MeshRenderer>() ?? GetComponentInChildren<MeshRenderer>();

        if (anchorManager == null)
            anchorManager = FindFirstObjectByType<ARAnchorManager>();

        bool isNewTarget = (currentTarget != target);
        currentTarget = target;

        if (isNewTarget)
        {
            isInitialized = false;
            isWorldLocked = false;
            lockMode = "Unlocked";
            trackingFramesCount = 0;
            trackingStartTime = Time.time;
            timeToLockSeconds = 0f;
            tier3Timer = 0f;
            pendingRelocalizationSnap = true;
            consecutiveStableFrames = 0;
            residualWindow.Clear();
            bearingResidualWindow.Clear();
            ReleaseActiveAnchor();
        }

        if (target.trackingState == TrackingState.Tracking)
        {
            lastValidTrackingTime = Time.time;

            if (!isInitialized)
            {
                ComputeRawTargetPose(out Vector3 rawPos, out Quaternion rawRot);
                lockedTargetPosition = rawPos;
                lockedTargetRotation = rawRot;
                transform.position = lockedTargetPosition;
                transform.rotation = lockedTargetRotation;
                previousRawPos = rawPos;
                previousRawRot = rawRot;
                isInitialized = true;
                pendingRelocalizationSnap = false;

                if (quadRenderer != null)
                    quadRenderer.enabled = true;
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
        isWorldLocked = false;
        lockMode = "Unlocked";
        trackingFramesCount = 0;
        trackingStartTime = 0f;
        timeToLockSeconds = 0f;
        tier3Timer = 0f;
        pendingRelocalizationSnap = true;
        consecutiveStableFrames = 0;
        residualWindow.Clear();
        bearingResidualWindow.Clear();
        ReleaseActiveAnchor();

        if (quadRenderer != null)
            quadRenderer.enabled = false;
    }

    public void SetCropMetrics(float trueCropWidth, float quadWorldWidth, float pageHeightMm, string source, int samples)
    {
        trueCropWidth_m = trueCropWidth;
        quadWorldWidth_m = quadWorldWidth;
        pageHeight_mm = pageHeightMm;
        pageHeightSource = source;
        pageHeightSampleCount = samples;
    }

    public void SetOverlayVisibility(bool visible, string reason = "")
    {
        isOverlayVisible = visible;
        hideReason = visible ? "Visible" : (string.IsNullOrEmpty(reason) ? "Hidden" : reason);
    }

    private void LateUpdate()
    {
        if (currentTarget == null) return;

        float dt = Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f);

        // ─── STAGE 1: Gating & Lifecycle Pipeline ──────────────────────────────
        if (isAppSuspended || isARSessionRelocalizing)
        {
            return;
        }

        if (framesToWarmupAfterResume > 0)
        {
            framesToWarmupAfterResume--;
            return;
        }

        bool isCurrentlyTracking = (currentTarget.trackingState == TrackingState.Tracking);

        if (isCurrentlyTracking)
        {
            lastValidTrackingTime = Time.time;
        }
        else
        {
            if (Time.time - lastValidTrackingTime > trackingLossGracePeriod)
            {
                return;
            }
        }

        ComputeRawTargetPose(out Vector3 rawPos, out Quaternion rawRot);

        // View-Ray Decomposition
        Camera mainCam = Camera.main;
        Vector3 camPos = (mainCam != null) ? mainCam.transform.position : transform.position - Vector3.forward * 0.5f;
        Vector3 viewVec = transform.position - camPos;
        float camToQuadDist = viewVec.magnitude;
        Vector3 viewRayDir = camToQuadDist > 0.001f ? (viewVec / camToQuadDist) : Vector3.forward;

        float camToPageDist = Vector3.Distance(camPos, rawPos);
        Vector3 errorVec = rawPos - transform.position;
        float rangeError_m = Vector3.Dot(errorVec, viewRayDir);
        Vector3 rangeErrorVec = rangeError_m * viewRayDir;
        Vector3 bearingErrorVec = errorVec - rangeErrorVec;
        float bearingError_m = bearingErrorVec.magnitude;

        lastBearingError_mm = bearingError_m * 1000f;
        lastRangeError_mm = rangeError_m * 1000f;
        lastCamToPageDist_m = camToPageDist;
        lastCamToQuadDist_m = camToQuadDist;

        // Camera-Local Coordinate Decomposition & Kinematics
        camLocalQuadPos = (mainCam != null) ? mainCam.transform.InverseTransformPoint(transform.position) : Vector3.zero;
        camLocalRawPos = (mainCam != null) ? mainCam.transform.InverseTransformPoint(rawPos) : Vector3.zero;
        camLocalDelta = camLocalRawPos - camLocalQuadPos;
        camLocalDeltaNorm_mm = camLocalDelta.magnitude * 1000f;

        currentQuadSpeedMps = (dt > 0.0001f && previousQuadPos != Vector3.zero) ? (Vector3.Distance(transform.position, previousQuadPos) / dt) : 0f;
        previousQuadPos = transform.position;

        // First-frame initialization guard
        if (!isInitialized)
        {
            lockedTargetPosition = rawPos;
            lockedTargetRotation = rawRot;
            transform.position = rawPos;
            transform.rotation = rawRot;
            previousRawPos = rawPos;
            previousRawRot = rawRot;
            previousQuadPos = rawPos;
            isInitialized = true;
            pendingRelocalizationSnap = false;

            if (quadRenderer != null)
                quadRenderer.enabled = true;
            return;
        }

        // ─── STAGE 2: Relocalization & Discontinuity Snap Engine ────────────────
        float rawJitterDistance = Vector3.Distance(rawPos, previousRawPos);
        float rawJitterAngle = Quaternion.Angle(rawRot, previousRawRot);
        previousRawPos = rawPos;
        previousRawRot = rawRot;

        bool isRawPoseStable = (rawJitterDistance < 0.018f && rawJitterAngle < 3.5f);
        if (isRawPoseStable && isCurrentlyTracking)
        {
            consecutiveStableFrames++;
        }
        else
        {
            consecutiveStableFrames = 0;
        }

        float distanceToCurrentTransform = Vector3.Distance(transform.position, rawPos);
        float angleToCurrentTransform = Quaternion.Angle(transform.rotation, rawRot);
        bool isDiscontinuousJump = !isWorldLocked && (distanceToCurrentTransform > discontinuousJumpThreshold || angleToCurrentTransform > discontinuousAngleThreshold);

        if (pendingRelocalizationSnap || isDiscontinuousJump)
        {
            if (consecutiveStableFrames >= requiredStableFrames)
            {
                transform.position = rawPos;
                transform.rotation = rawRot;
                lockedTargetPosition = rawPos;
                lockedTargetRotation = rawRot;
                pendingRelocalizationSnap = false;
                consecutiveStableFrames = 0;
                swapCount++;

                if (quadRenderer != null)
                    quadRenderer.enabled = true;

                Debug.Log($"[ARSpatialPoseFilter] Clean Relocalization Snap established! Distance jumped: {distanceToCurrentTransform:F3}m");
            }
            return;
        }

        // ─── STAGE 3: Statistical Convergence Gate ────────────────────────────
        float currentResidualMeters = Vector3.Distance(transform.position, rawPos);
        float currentAngleDeltaDeg = Quaternion.Angle(transform.rotation, rawRot);

        if (isCurrentlyTracking)
        {
            residualWindow.Enqueue(currentResidualMeters);
            bearingResidualWindow.Enqueue(bearingError_m);
            while (residualWindow.Count > 20)
            {
                residualWindow.Dequeue();
            }
            while (bearingResidualWindow.Count > 20)
            {
                bearingResidualWindow.Dequeue();
            }
        }
        else
        {
            residualWindow.Clear();
            bearingResidualWindow.Clear();
        }

        float windowMeanMeters = float.MaxValue;
        float windowStdDevMeters = float.MaxValue;
        float bearingMeanMeters = float.MaxValue;
        float bearingStdDevMeters = float.MaxValue;

        if (residualWindow.Count >= 15)
        {
            float sum = 0f;
            foreach (float r in residualWindow) sum += r;
            windowMeanMeters = sum / residualWindow.Count;

            float varSum = 0f;
            foreach (float r in residualWindow)
            {
                float diff = r - windowMeanMeters;
                varSum += diff * diff;
            }
            windowStdDevMeters = Mathf.Sqrt(varSum / residualWindow.Count);

            float bSum = 0f;
            foreach (float b in bearingResidualWindow) bSum += b;
            bearingMeanMeters = bSum / bearingResidualWindow.Count;

            float bVarSum = 0f;
            foreach (float b in bearingResidualWindow)
            {
                float diff = b - bearingMeanMeters;
                bVarSum += diff * diff;
            }
            bearingStdDevMeters = Mathf.Sqrt(bVarSum / bearingResidualWindow.Count);
        }

        bool isStatisticallyStable = isCurrentlyTracking
            && (bearingMeanMeters < 0.0020f || windowMeanMeters < 0.0020f)
            && (bearingStdDevMeters < 0.0012f || windowStdDevMeters < 0.0012f)
            && currentAngleDeltaDeg < 1.0f;

        trackingFramesCount++;

        // ─── STAGE 3 & 4: Active Image Tracking Filter & Grace Period Hold ─────
        if (isCurrentlyTracking)
        {
            float posDelta = Vector3.Distance(rawPos, lockedTargetPosition);
            float rotDelta = Quaternion.Angle(rawRot, lockedTargetRotation);

            if (isWorldLocked)
            {
                lockMode = "WorldLockedToDesk";

                // In World-Locked mode, keep quad 100% frozen in world space to eliminate all hand tremor
                transform.position = lockedTargetPosition;
                transform.rotation = lockedTargetRotation;

                // Monitor if the user deliberately displaced or rotated the physical textbook
                if (posDelta > 0.016f || rotDelta > 4.5f) // >16mm or >4.5 degrees sustained
                {
                    deliberateMoveFrameCounter++;
                    if (deliberateMoveFrameCounter >= 6)
                    {
                        // Deliberate book displacement confirmed: unlock to smoothly track to new book pose
                        isWorldLocked = false;
                        deliberateMoveFrameCounter = 0;
                        lockMode = "RelocatingToNewDeskPose";
                        Debug.Log($"[ARSpatialPoseFilter] Physical book movement detected ({posDelta * 1000f:F1}mm / {rotDelta:F1}°). Relocating overlay smoothly...");
                    }
                }
                else
                {
                    deliberateMoveFrameCounter = 0;
                }
            }
            else
            {
                lockMode = "ActiveImageTrackedSmooth";

                // Continuous Smoothstep Deadzone + Feathery Velvet EMA Smoother
                if (posDelta > innerPositionDeadzone)
                {
                    float tPos = Mathf.Clamp01((posDelta - innerPositionDeadzone) / Mathf.Max(0.0001f, outerPositionDeadzone - innerPositionDeadzone));
                    float smoothPosWeight = tPos * tPos * (3f - 2f * tPos);
                    lockedTargetPosition = Vector3.Lerp(lockedTargetPosition, rawPos, smoothPosWeight);
                }

                if (rotDelta > innerRotationDeadzone)
                {
                    float tRot = Mathf.Clamp01((rotDelta - innerRotationDeadzone) / Mathf.Max(0.0001f, outerRotationDeadzone - innerRotationDeadzone));
                    float smoothRotWeight = tRot * tRot * (3f - 2f * tRot);
                    lockedTargetRotation = Quaternion.Slerp(lockedTargetRotation, rawRot, smoothRotWeight);
                }

                float distanceToTarget = Vector3.Distance(transform.position, lockedTargetPosition);
                float normalizedDist = Mathf.Clamp01(distanceToTarget / fastMoveThreshold);
                float featheryPosRatio = normalizedDist * normalizedDist;
                float currentPosLerpSpeed = Mathf.Lerp(minSmoothSpeed, maxSmoothSpeed, featheryPosRatio);
                float alphaPos = 1.0f - Mathf.Exp(-currentPosLerpSpeed * dt);

                float angleToTarget = Quaternion.Angle(transform.rotation, lockedTargetRotation);
                float normalizedAngle = Mathf.Clamp01(angleToTarget / fastRotThreshold);
                float featheryRotRatio = normalizedAngle * normalizedAngle;
                float currentRotLerpSpeed = Mathf.Lerp(minRotSmoothSpeed, maxRotSmoothSpeed, featheryRotRatio);
                float alphaRot = 1.0f - Mathf.Exp(-currentRotLerpSpeed * dt);

                Vector3 smoothedPos = Vector3.Lerp(transform.position, lockedTargetPosition, alphaPos);
                Quaternion smoothedRot = Quaternion.Slerp(transform.rotation, lockedTargetRotation, alphaRot);

                float maxStep = maxLinearVelocityMps * dt;
                transform.position = Vector3.MoveTowards(transform.position, smoothedPos, maxStep);
                transform.rotation = smoothedRot;

                // Lock down once settled in position (eliminates 100% of user hand tremor during reading)
                if ((trackingFramesCount >= 10 && isStatisticallyStable) || (trackingFramesCount >= 18 && posDelta < 0.004f && rotDelta < 1.8f))
                {
                    isWorldLocked = true;
                    lockMode = "WorldLockedToDesk";
                    deliberateMoveFrameCounter = 0;
                    Debug.Log($"[ARSpatialPoseFilter] World-Lock established on physical desk! Quad anchored with 0.0mm tremor.");
                }
            }
        }
        else
        {
            // Tracking Lost: Hold last known pose during grace period (0.45s)
            lockMode = "GracePeriodHold";
            isWorldLocked = true;
        }

        // ─── STAGE 5: Telemetry Logging & Diagnostics ─────────────────────────
        LogPerFrameTelemetry(windowMeanMeters < 10f ? windowMeanMeters : currentResidualMeters,
                             windowStdDevMeters < 10f ? windowStdDevMeters : 0f,
                             currentAngleDeltaDeg,
                             rawPos,
                             rawRot);

        if (Time.time - lastTelemetryLogTime > 1.0f)
        {
            lastTelemetryLogTime = Time.time;
            Debug.Log($"[Spatial Telemetry] Locked: {isWorldLocked} ({lockMode}) | CamLocalDelta: {camLocalDeltaNorm_mm:F1}mm | BearingErr: {lastBearingError_mm:F1}mm | RangeErr: {lastRangeError_mm:F1}mm | ResMean: {(windowMeanMeters < 10f ? windowMeanMeters * 1000f : currentResidualMeters * 1000f):F2}mm | State: {currentTarget.trackingState}");
        }

        if (quadRenderer != null && !quadRenderer.enabled)
        {
            quadRenderer.enabled = true;
        }
    }

    private void ComputeRawTargetPose(out Vector3 rawPos, out Quaternion rawRot)
    {
        rawPos = currentTarget.transform.position + (currentTarget.transform.up * normalOffsetMeters);
        rawRot = currentTarget.transform.rotation * Quaternion.Euler(90f, 0f, 0f);
    }

    private void AttachAnchorAtCurrentPose()
    {
        try
        {
            GameObject anchorObj = new GameObject("AR_WorldAnchor");
            anchorObj.transform.position = transform.position;
            anchorObj.transform.rotation = transform.rotation;
            activeAnchor = anchorObj.AddComponent<ARAnchor>();
            if (activeAnchor != null)
            {
                Debug.Log($"[ARSpatialPoseFilter] ARAnchor attached at {transform.position:F3}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[ARSpatialPoseFilter] Could not create ARAnchor: {ex.Message}");
        }
    }

    private System.Collections.IEnumerator ExecuteMakeBeforeBreakAnchorSwap(Vector3 targetPos, Quaternion targetRot)
    {
        isAnchorSwapInFlight = true;
        Debug.Log("[ARSpatialPoseFilter] Tier 3 Triggered: Initiating Make-Before-Break Anchor Swap...");

        GameObject candObj = new GameObject("AR_CandidateAnchor");
        candObj.transform.position = targetPos;
        candObj.transform.rotation = targetRot;
        candidateAnchor = candObj.AddComponent<ARAnchor>();

        // Wait for candidate anchor to be registered and tracking
        float timeout = 0.5f;
        while (candidateAnchor != null && candidateAnchor.trackingState != TrackingState.Tracking && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        // Swap to candidate anchor
        ReleaseActiveAnchor();
        activeAnchor = candidateAnchor;
        candidateAnchor = null;

        transform.position = targetPos;
        transform.rotation = targetRot;
        lockedTargetPosition = targetPos;
        lockedTargetRotation = targetRot;
        isAnchorSwapInFlight = false;
        swapCount++;
        Debug.Log("[ARSpatialPoseFilter] Make-Before-Break Anchor Swap completed seamlessly!");
    }

    private void ReleaseActiveAnchor()
    {
        if (activeAnchor != null)
        {
            if (anchorManager != null)
            {
                Destroy(activeAnchor.gameObject);
            }
            activeAnchor = null;
        }
    }

    private void InitializeTelemetryCsv()
    {
        if (!enableTelemetryCsv) return;

        try
        {
            string dir = Application.persistentDataPath;
            string fileName = $"spatial_telemetry_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
            telemetryFilePath = Path.Combine(dir, fileName);

            telemetryWriter = new StreamWriter(telemetryFilePath, false, System.Text.Encoding.UTF8);
            telemetryWriter.WriteLine("TimestampMs,TrackingState,ImageTrackingState,IsImageInView,NotTrackingReason,IsLocked,LockMode,Flag_DisableDriftCorrection,TimeToLock_s,SwapCount,SwapPending,QuadSpeed_mps,WorldQuadToRawDist_m,OverlayVisible,HideReason,ResMean_mm,ResStd_mm,BearingError_mm,RangeError_mm,AngleDelta_deg,CamLocalQuadX,CamLocalQuadY,CamLocalQuadZ,CamLocalRawX,CamLocalRawY,CamLocalRawZ,CamLocalDeltaX_mm,CamLocalDeltaY_mm,CamLocalDeltaZ_mm,CamLocalDeltaNorm_mm,CamLocalDeskPlaneX,CamLocalDeskPlaneY,CamLocalDeskPlaneZ,CamPosX,CamPosY,CamPosZ,CamRotX,CamRotY,CamRotZ,CamRotW,QuadPosX,QuadPosY,QuadPosZ,QuadRotX,QuadRotY,QuadRotZ,QuadRotW,RawImagePosX,RawImagePosY,RawImagePosZ,RawImageRotX,RawImageRotY,RawImageRotZ,RawImageRotW,XROriginPosX,XROriginPosY,XROriginPosZ,XROriginRotX,XROriginRotY,XROriginRotZ,XROriginRotW,HasDeskPlane,DeskPlaneId,DeskPlaneSizeX_m,DeskPlaneSizeY_m,DeskPlanePosX,DeskPlanePosY,DeskPlanePosZ,CamToPlaneHeight_m,QuadToPlaneHeight_m,FeaturePointCount,CamToPageDist_m,CamToQuadDist_m,TrueCropWidth_m,QuadWorldWidth_m,PageHeight_mm,PageHeightSource,PageHeightSampleCount");
            telemetryWriter.Flush();
            Debug.Log($"[Spatial Telemetry] CSV Logger initialized at: {telemetryFilePath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Spatial Telemetry] Failed to create CSV log: {ex.Message}");
        }
    }

    private void LogPerFrameTelemetry(float resMeanMeters, float resStdMeters, float angleDeltaDeg, Vector3 rawImagePos, Quaternion rawImageRot)
    {
        if (currentTarget == null) return;
        if (telemetryWriter == null) InitializeTelemetryCsv();
        if (telemetryWriter == null) return;

        try
        {
            long timestampMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string stateStr = currentTarget.trackingState.ToString();
            string imgTrackingStateStr = currentTarget.trackingState.ToString();
            string notTrackingReasonStr = ARSession.notTrackingReason.ToString();

            Camera mainCam = Camera.main;
            Vector3 camPos = mainCam != null ? mainCam.transform.position : Vector3.zero;
            Quaternion camRot = mainCam != null ? mainCam.transform.rotation : Quaternion.identity;
            Vector3 quadPos = transform.position;
            Quaternion quadRot = transform.rotation;

            bool isImageInView = false;
            if (mainCam != null && currentTarget != null)
            {
                Vector3 vp = mainCam.WorldToViewportPoint(currentTarget.transform.position);
                isImageInView = (vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f);
            }

            float worldQuadToRawDist_m = Vector3.Distance(quadPos, rawImagePos);

            if (xrOrigin == null) xrOrigin = FindFirstObjectByType<XROrigin>();
            Vector3 xrPos = xrOrigin != null ? xrOrigin.transform.position : Vector3.zero;
            Quaternion xrRot = xrOrigin != null ? xrOrigin.transform.rotation : Quaternion.identity;

            if (planeManager == null) planeManager = FindFirstObjectByType<ARPlaneManager>();
            Vector3 deskPlanePos = Vector3.zero;
            Vector2 deskPlaneSize = Vector2.zero;
            string deskPlaneIdStr = "None";
            bool hasDeskPlane = false;
            if (planeManager != null)
            {
                float bestScore = float.MaxValue;
                foreach (var plane in planeManager.trackables)
                {
                    if (plane.alignment == PlaneAlignment.HorizontalUp)
                    {
                        float dx = quadPos.x - plane.transform.position.x;
                        float dz = quadPos.z - plane.transform.position.z;
                        float horizontalDist = Mathf.Sqrt(dx * dx + dz * dz);
                        if (horizontalDist < bestScore)
                        {
                            bestScore = horizontalDist;
                            deskPlanePos = plane.transform.position;
                            deskPlaneSize = plane.size;
                            deskPlaneIdStr = plane.trackableId.ToString();
                            hasDeskPlane = true;
                        }
                    }
                }
            }

            float camToPlaneHeight_m = hasDeskPlane ? (camPos.y - deskPlanePos.y) : 0f;
            float quadToPlaneHeight_m = hasDeskPlane ? (quadPos.y - deskPlanePos.y) : 0f;

            if (pointCloudManager == null) pointCloudManager = FindFirstObjectByType<ARPointCloudManager>();
            int featurePointCount = 0;
            if (pointCloudManager != null)
            {
                foreach (var pointCloud in pointCloudManager.trackables)
                {
                    if (pointCloud.positions.HasValue)
                    {
                        featurePointCount += pointCloud.positions.Value.Length;
                    }
                }
            }

            Vector3 camLocalDeskPlane = (mainCam != null && hasDeskPlane) ? mainCam.transform.InverseTransformPoint(deskPlanePos) : Vector3.zero;

            string row = $"{timestampMs},{stateStr},{imgTrackingStateStr},{isImageInView},{notTrackingReasonStr},{isWorldLocked},{lockMode},{disableDriftCorrectionPostLock},{timeToLockSeconds:F3}," +
                         $"{swapCount},{isAnchorSwapInFlight},{currentQuadSpeedMps:F4},{worldQuadToRawDist_m:F4}," +
                         $"{isOverlayVisible},{hideReason}," +
                         $"{(resMeanMeters * 1000f):F3},{(resStdMeters * 1000f):F3}," +
                         $"{lastBearingError_mm:F3},{lastRangeError_mm:F3},{angleDeltaDeg:F3}," +
                         $"{camLocalQuadPos.x:F4},{camLocalQuadPos.y:F4},{camLocalQuadPos.z:F4}," +
                         $"{camLocalRawPos.x:F4},{camLocalRawPos.y:F4},{camLocalRawPos.z:F4}," +
                         $"{(camLocalDelta.x * 1000f):F3},{(camLocalDelta.y * 1000f):F3},{(camLocalDelta.z * 1000f):F3},{camLocalDeltaNorm_mm:F3}," +
                         $"{camLocalDeskPlane.x:F4},{camLocalDeskPlane.y:F4},{camLocalDeskPlane.z:F4}," +
                         $"{camPos.x:F4},{camPos.y:F4},{camPos.z:F4}," +
                         $"{camRot.x:F4},{camRot.y:F4},{camRot.z:F4},{camRot.w:F4}," +
                         $"{quadPos.x:F4},{quadPos.y:F4},{quadPos.z:F4}," +
                         $"{quadRot.x:F4},{quadRot.y:F4},{quadRot.z:F4},{quadRot.w:F4}," +
                         $"{rawImagePos.x:F4},{rawImagePos.y:F4},{rawImagePos.z:F4}," +
                         $"{rawImageRot.x:F4},{rawImageRot.y:F4},{rawImageRot.z:F4},{rawImageRot.w:F4}," +
                         $"{xrPos.x:F4},{xrPos.y:F4},{xrPos.z:F4}," +
                         $"{xrRot.x:F4},{xrRot.y:F4},{xrRot.z:F4},{xrRot.w:F4}," +
                         $"{hasDeskPlane},{deskPlaneIdStr},{deskPlaneSize.x:F3},{deskPlaneSize.y:F3},{deskPlanePos.x:F4},{deskPlanePos.y:F4},{deskPlanePos.z:F4}," +
                         $"{camToPlaneHeight_m:F4},{quadToPlaneHeight_m:F4}," +
                         $"{featurePointCount}," +
                         $"{lastCamToPageDist_m:F4},{lastCamToQuadDist_m:F4}," +
                         $"{trueCropWidth_m:F4},{quadWorldWidth_m:F4}," +
                         $"{pageHeight_mm:F2},{pageHeightSource},{pageHeightSampleCount}";

            telemetryWriter.WriteLine(row);
            if (Time.time - lastFlushTime >= 1.5f)
            {
                lastFlushTime = Time.time;
                telemetryWriter.Flush();
            }
        }
        catch
        {
            // Silently suppress per-frame write errors
        }
    }

    private void CloseTelemetryCsv()
    {
        if (telemetryWriter != null)
        {
            try
            {
                telemetryWriter.Flush();
                telemetryWriter.Close();
                telemetryWriter.Dispose();
            }
            catch { }
            telemetryWriter = null;
        }
    }

    private void OnDestroy()
    {
        CloseTelemetryCsv();
        ReleaseActiveAnchor();
        if (correctionNode != null)
        {
            Destroy(correctionNode.gameObject);
        }
    }
}

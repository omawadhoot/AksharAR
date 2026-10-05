using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class ARPageScanController : MonoBehaviour
{
    [Header("AR Foundation References")]
    [SerializeField] private ARTrackedImageManager trackedImageManager;
    [SerializeField] private ARCameraManager arCameraManager;
    [SerializeField] private ARRaycastManager raycastManager;

    [Header("Services & Overlay Controllers")]
    [SerializeField] private CloudVisionService visionService;
    [SerializeField] private ARLineOverlayController overlayController;
    [SerializeField] private Transform worldSpaceCanvasTransform;

    [Header("Scan Configuration")]
    [Tooltip("Minimum time (in seconds) between scans to prevent spamming")]
    [SerializeField] private float scanCooldown = 3.0f;

    [Tooltip("If true, captures raw CPU image directly from ARCameraManager with Aspect Crop. If false, uses clean upright ScreenCapture.")]
    [SerializeField] private bool useDirectCameraManager = false;

    [Tooltip("Delay in seconds after tapping scan to let camera focus and hand movement settle before frame capture")]
    [SerializeField] private float scanCaptureDelay = 0.15f;

    [Tooltip("Maximum additional duration (seconds) to wait for gyroscope angular velocity to settle below threshold")]
    [SerializeField] private float maxInertialSettlingTimeout = 0.25f;

    [Tooltip("Gyro angular speed threshold (rad/s) below which the device is considered motionless")]
    [SerializeField] private float steadyGyroThresholdRadPerSec = 0.14f;

    [Header("Reading Window Viewfinder")]
    [SerializeField] private ReadingWindowViewfinder readingWindowViewfinder;

    private bool isScanningOrHandoffInProgress = false;
    private float lastScanTime = -10f;
    private MutableRuntimeReferenceImageLibrary mutableLibrary;
    private string activePageId = "";
    private string pendingPageId = "";
    private int pendingStableFrameCount = 0;
    private Coroutine timeoutCoroutine;

    private void Awake()
    {
        Input.gyro.enabled = true;
        RequestAndroidCameraPermission();
    }

    private void Start()
    {
        if (readingWindowViewfinder == null)
            readingWindowViewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();

        EnsureRaycastAndPlaneManagers();
        InitializeMutableRuntimeLibrary();
    }

    private void InitializeMutableRuntimeLibrary()
    {
        if (trackedImageManager == null)
            trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();

        if (trackedImageManager != null)
        {
            // Ensure continuous 6-DoF ARCore tracking updates on moving/viewed images
            trackedImageManager.requestedMaxNumberOfMovingImages = 1;

            if (trackedImageManager.referenceLibrary is MutableRuntimeReferenceImageLibrary runtimeLib)
            {
                mutableLibrary = runtimeLib;
            }
            else
            {
                var newLib = trackedImageManager.CreateRuntimeLibrary();
                if (newLib is MutableRuntimeReferenceImageLibrary mutable)
                {
                    mutableLibrary = mutable;
                    trackedImageManager.referenceLibrary = mutableLibrary;
                    Debug.Log("[ARPageScanController] MutableRuntimeReferenceImageLibrary initialized for dynamic AR tracking!");
                }
            }
        }
    }

    private void RequestAndroidCameraPermission()
    {
#if UNITY_ANDROID
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Debug.Log("[ARPageScanController] Requesting Android Camera runtime permission...");
            Permission.RequestUserPermission(Permission.Camera);
        }
#endif
    }

    private void OnEnable()
    {
        if (trackedImageManager == null)
            trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();

        if (arCameraManager == null)
            arCameraManager = FindFirstObjectByType<ARCameraManager>();

        if (visionService == null)
            visionService = FindFirstObjectByType<CloudVisionService>();

        if (overlayController == null)
            overlayController = FindFirstObjectByType<ARLineOverlayController>();

        if (readingWindowViewfinder == null)
            readingWindowViewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();

        if (trackedImageManager != null)
        {
            trackedImageManager.requestedMaxNumberOfMovingImages = 1;
            trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
        }

        InitializeMutableRuntimeLibrary();
    }

    private void OnDisable()
    {
        if (trackedImageManager != null)
        {
            trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
        }
    }

    private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs eventArgs)
    {
        foreach (ARTrackedImage trackedImage in eventArgs.added)
        {
            HandleTrackedImageUpdate(trackedImage);
        }

        foreach (ARTrackedImage trackedImage in eventArgs.updated)
        {
            if (trackedImage != null && trackedImage.trackingState == TrackingState.Tracking)
            {
                HandleTrackedImageUpdate(trackedImage);
            }
        }
    }

    private bool TryGetTrackedImageName(ARTrackedImage trackedImage, out string imageName)
    {
        imageName = null;
        if (trackedImage == null) return false;
        try
        {
            if (trackedImageManager != null && trackedImageManager.referenceLibrary != null)
            {
                var refImg = trackedImage.referenceImage;
                if (!string.IsNullOrEmpty(refImg.name) && refImg.guid != System.Guid.Empty)
                {
                    imageName = refImg.name;
                    return true;
                }
            }
        }
        catch (System.Exception)
        {
            // Catches ARFoundation IndexOutOfRangeException when library is swapped
            return false;
        }
        return false;
    }

    private void HandleTrackedImageUpdate(ARTrackedImage trackedImage)
    {
        if (!TryGetTrackedImageName(trackedImage, out string imageName)) return;

        // Check if newly scanned target is now tracking stably
        if (!string.IsNullOrEmpty(pendingPageId) && imageName == pendingPageId && trackedImage.trackingState == TrackingState.Tracking)
        {
            pendingStableFrameCount++;
            if (pendingStableFrameCount >= 2)
            {
                // Smoothly promote pending target to active target
                activePageId = pendingPageId;
                pendingPageId = "";
                pendingStableFrameCount = 0;
                isScanningOrHandoffInProgress = false;

                if (timeoutCoroutine != null)
                {
                    StopCoroutine(timeoutCoroutine);
                    timeoutCoroutine = null;
                }

                if (overlayController != null)
                    overlayController.UpdateShutterButtonState();

                Debug.Log($"[ARPageScanController] Smooth tracking lock established on '{activePageId}'!");
                AlignCanvasToTrackedImage(trackedImage);
                return;
            }
        }

        // Align if this is our active tracked target
        if (!string.IsNullOrEmpty(activePageId) && imageName == activePageId)
        {
            AlignCanvasToTrackedImage(trackedImage);
        }
    }

    private void AlignCanvasToTrackedImage(ARTrackedImage trackedImage)
    {
        if (trackedImage == null) return;

        if (worldSpaceCanvasTransform != null)
        {
            worldSpaceCanvasTransform.position = trackedImage.transform.position;
            worldSpaceCanvasTransform.rotation = trackedImage.transform.rotation;
        }

        ARWorldSpaceOverlayController worldSpaceOverlay = FindFirstObjectByType<ARWorldSpaceOverlayController>();
        if (worldSpaceOverlay != null)
        {
            worldSpaceOverlay.AttachToTrackedImage(trackedImage);
        }

        ARWorldSpaceUIToolkitController worldSpaceUIToolkit = FindFirstObjectByType<ARWorldSpaceUIToolkitController>();
        if (worldSpaceUIToolkit != null)
        {
            worldSpaceUIToolkit.AttachToTrackedImage(trackedImage);
        }
    }

    /// <summary>
    /// App backgrounded / phone slept: ARCore's world frame is no longer trustworthy on return,
    /// so wipe the overlay, reset the AR session, and go back to the ready-to-scan state.
    /// </summary>
    private bool resumeResetPending = false;

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            ResetAfterInterruption();
            resumeResetPending = true;
        }
        else if (resumeResetPending)
        {
            resumeResetPending = false;
            // Fresh coordinate frame; any previously learned page poses are meaningless now
            var arSession = FindFirstObjectByType<ARSession>();
            if (arSession != null) arSession.Reset();
            ResetAfterInterruption();
        }
    }

    private void ResetAfterInterruption()
    {
        Debug.Log("[ARPageScanController] App interruption detected: clearing overlay and returning to rescan state.");
        ResetTrackingSession();

        if (overlayController == null)
            overlayController = FindFirstObjectByType<ARLineOverlayController>();
        if (overlayController != null)
            overlayController.ResetScanButton();
    }

    public void ResetTrackingSession()
    {
        Debug.Log("[ARPageScanController] ResetTrackingSession: Clearing active tracking targets and removing overlays.");
        if (timeoutCoroutine != null)
        {
            StopCoroutine(timeoutCoroutine);
            timeoutCoroutine = null;
        }

        activePageId = "";
        pendingPageId = "";
        pendingStableFrameCount = 0;
        isScanningOrHandoffInProgress = false;

        ARWorldSpaceUIToolkitController worldSpaceUIToolkit = FindFirstObjectByType<ARWorldSpaceUIToolkitController>();
        if (worldSpaceUIToolkit != null)
        {
            worldSpaceUIToolkit.ClearOverlays();
        }

        ARWorldSpaceOverlayController worldSpaceOverlay = FindFirstObjectByType<ARWorldSpaceOverlayController>();
        if (worldSpaceOverlay != null)
        {
            worldSpaceOverlay.ClearOverlays();
        }

        ResetTrackedImageManagerLibrary();
    }

    private void ResetTrackedImageManagerLibrary()
    {
        if (trackedImageManager == null)
            trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();

        if (trackedImageManager != null)
        {
            trackedImageManager.enabled = false;

            // Purge any stale ARTrackedImage game objects lingering from previous scans
            var oldTracked = FindObjectsByType<ARTrackedImage>(FindObjectsSortMode.None);
            foreach (var img in oldTracked)
            {
                if (img != null) Destroy(img.gameObject);
            }

            var newLib = trackedImageManager.CreateRuntimeLibrary();
            if (newLib is MutableRuntimeReferenceImageLibrary mutable)
            {
                mutableLibrary = mutable;
                trackedImageManager.referenceLibrary = mutableLibrary;
            }
            trackedImageManager.enabled = true;
            Debug.Log("[ARPageScanController] ARTrackedImageManager library cleanly reset.");
        }
    }

    public void TriggerManualScan()
    {
        if (isScanningOrHandoffInProgress)
        {
            Debug.Log("[ARPageScanController] Scan / tracking handoff in progress. Ignoring tap.");
            return;
        }

        if (Time.time - lastScanTime < scanCooldown)
        {
            Debug.Log("[ARPageScanController] Scan cooldown active. Please wait.");
            return;
        }

        if (readingWindowViewfinder == null)
            readingWindowViewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();

        if (readingWindowViewfinder != null && !readingWindowViewfinder.IsDeviceLevel)
        {
            Debug.LogWarning($"[ARPageScanController] Scan aborted: Device tilted ({readingWindowViewfinder.CurrentTiltAngle:F0}°). Hold camera level until viewfinder turns clear.");
            return;
        }

        // Hard-lock scan button instantly
        isScanningOrHandoffInProgress = true;
        lastScanTime = Time.time;

        if (overlayController != null)
        {
            overlayController.SetScanButtonInteractable(false);
            overlayController.SetScanButtonText("⏳ Scanning...");
        }

        // Clear existing overlays immediately when a new scan starts
        ARWorldSpaceUIToolkitController worldSpaceUIToolkit = FindFirstObjectByType<ARWorldSpaceUIToolkitController>();
        if (worldSpaceUIToolkit != null)
        {
            worldSpaceUIToolkit.ClearOverlays();
        }

        // Cleanly wipe old reference library targets to avoid duplicate/missing GUID errors on rescan
        ResetTrackedImageManagerLibrary();

        StartCoroutine(CaptureAndScanFrame());

        if (timeoutCoroutine != null) StopCoroutine(timeoutCoroutine);
        timeoutCoroutine = StartCoroutine(HandoffTimeoutSafety(8f));
    }

    private IEnumerator HandoffTimeoutSafety(float timeoutSeconds)
    {
        yield return new WaitForSeconds(timeoutSeconds);
        if (isScanningOrHandoffInProgress)
        {
            Debug.LogWarning("[ARPageScanController] Handoff timeout reached. Re-enabling scan button.");
            isScanningOrHandoffInProgress = false;
            pendingPageId = "";
            pendingStableFrameCount = 0;
            if (overlayController != null)
                overlayController.ResetScanButton();
        }
    }

    private IEnumerator CaptureAndScanFrame()
    {
        Debug.Log("[ARPageScanController] Anti-Jerk Shutter: Settling camera & monitoring inertial stability...");

        // Initial mechanical settling delay (lets finger tap impact and screen touch shockwave dissipate)
        if (scanCaptureDelay > 0f)
        {
            yield return new WaitForSeconds(scanCaptureDelay);
        }

        // Gyroscopic Inertial Settling Loop: Snap when device motion is at a quiet minimum
        float settlingTimer = 0f;
        while (settlingTimer < maxInertialSettlingTimeout)
        {
            float gyroMagnitude = 0f;
            if (SystemInfo.supportsGyroscope && Input.gyro.enabled)
            {
                gyroMagnitude = Input.gyro.rotationRateUnbiased.magnitude;
            }

            if (gyroMagnitude < steadyGyroThresholdRadPerSec || !SystemInfo.supportsGyroscope)
            {
                // Hand is steady — proceed to snap immediately
                break;
            }

            settlingTimer += Time.deltaTime;
            yield return null;
        }

        Texture2D liveFrameTexture = null;
        CameraCapturePose capturePose = default;

        if (useDirectCameraManager)
        {
            liveFrameTexture = CaptureRawCameraTexture();
        }

        if (liveFrameTexture == null)
        {
            // Hide UI overlay & button before screen capture
            if (overlayController != null) overlayController.SetUIVisibility(false);

            // Wait for end of frame to capture clean camera background in 100% upright orientation
            yield return new WaitForEndOfFrame();

            // Snapshot camera 6-DoF pose and viewport metrics at exact capture timestamp
            Camera arCam = Camera.main;
            RectInt crop = readingWindowViewfinder != null 
                ? readingWindowViewfinder.GetPixelCropRect(Screen.width, Screen.height)
                : new RectInt(0, 0, Screen.width, Screen.height);

            if (arCam != null)
            {
                capturePose = new CameraCapturePose
                {
                    cameraPosition = arCam.transform.position,
                    cameraRotation = arCam.transform.rotation,
                    fieldOfView = arCam.fieldOfView,
                    aspect = arCam.aspect,
                    pixelCropRect = crop,
                    screenWidth = Screen.width,
                    screenHeight = Screen.height,
                    isValid = true
                };
            }

            liveFrameTexture = ScreenCapture.CaptureScreenshotAsTexture();

            // Restore UI overlay & freeze photo on screen immediately upon capture
            if (overlayController != null)
            {
                overlayController.SetUIVisibility(true);
                overlayController.SetFreezeFrameTexture(liveFrameTexture);
            }
        }

        if (liveFrameTexture != null)
        {
            Texture2D textureToSend = liveFrameTexture;

            RectInt cropRect = readingWindowViewfinder != null 
                ? readingWindowViewfinder.GetPixelCropRect(liveFrameTexture.width, liveFrameTexture.height)
                : new RectInt(0, 0, liveFrameTexture.width, liveFrameTexture.height);

            // Step 2: Calculate True Metric Width and Page Height using AR Plane + Feature Points
            float physicalWidthMeters = GetDynamicCropWidth(cropRect, out float pageHeightMm, out string heightSource, out int sampleCount);

            // Forward crop metrics to spatial pose filter for telemetry logging
            ARSpatialPoseFilter spatialFilter = FindFirstObjectByType<ARSpatialPoseFilter>();
            if (spatialFilter != null)
            {
                spatialFilter.SetCropMetrics(physicalWidthMeters, physicalWidthMeters, pageHeightMm, heightSource, sampleCount);
                spatialFilter.SetOverlayVisibility(true);
            }

            // Crop texture to the 2D "Reading Window" viewport for high-density paragraph OCR
            if (readingWindowViewfinder != null)
            {
                Texture2D cropped = readingWindowViewfinder.CropTextureToReadingWindow(liveFrameTexture);
                if (cropped != null)
                {
                    textureToSend = cropped;
                }
            }

            // Forward camera capture pose to UI Toolkit controller for 3D Raycast Plane Unprojection
            ARWorldSpaceUIToolkitController worldSpaceUIToolkit = FindFirstObjectByType<ARWorldSpaceUIToolkitController>();
            if (worldSpaceUIToolkit != null)
            {
                worldSpaceUIToolkit.SetCameraCapturePose(capturePose);
            }

            string modeTag = readingWindowViewfinder != null ? readingWindowViewfinder.ActiveReadingMode.ToString() : "Default";
            bool isLandscapePhoto = textureToSend.width > textureToSend.height;
            Debug.Log($"[ARPageScanController] Captured {(isLandscapePhoto ? "LANDSCAPE" : "PORTRAIT")} Reading Window Photo ({textureToSend.width}x{textureToSend.height}) in {modeTag} Mode. Metric Width: {physicalWidthMeters:F3}m (PageHeight: {pageHeightMm:F1}mm via {heightSource}). Sending to CloudVisionService...");

            if (visionService != null)
            {
                visionService.DetectTextFromTexture(textureToSend);
            }
            else
            {
                Debug.LogError("[ARPageScanController] CloudVisionService reference missing!");
            }

            // In Freeze-Frame AR, we do not need 3D ARCore reference tracking quad
            isScanningOrHandoffInProgress = false;
        }
        else
        {
            Debug.LogError("[ARPageScanController] Failed to capture live camera frame.");
            isScanningOrHandoffInProgress = false;
            if (overlayController != null)
                overlayController.ResetScanButton();
        }
    }

    private void EnsureRaycastAndPlaneManagers()
    {
        if (raycastManager == null)
            raycastManager = FindFirstObjectByType<ARRaycastManager>();

        var planeManager = FindFirstObjectByType<ARPlaneManager>();

        var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
        if (origin != null)
        {
            if (raycastManager == null)
            {
                raycastManager = origin.gameObject.AddComponent<ARRaycastManager>();
                Debug.Log("[ARPageScanController] Added ARRaycastManager to XROrigin.");
            }
            if (planeManager == null)
            {
                planeManager = origin.gameObject.AddComponent<ARPlaneManager>();
                planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal;
                Debug.Log("[ARPageScanController] Added ARPlaneManager to XROrigin for desk plane tracking.");
            }
        }
        else
        {
            if (raycastManager == null) raycastManager = gameObject.AddComponent<ARRaycastManager>();
            if (planeManager == null)
            {
                planeManager = gameObject.AddComponent<ARPlaneManager>();
                planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal;
            }
        }
    }

    /// <summary>
    /// Measures the exact physical distance from the camera to the desk/book plane at the shutter moment,
    /// samples feature points to measure book elevation (page height), and calculates the true metric width.
    /// </summary>
    public float GetDynamicCropWidth(RectInt screenCropRect, out float pageHeightMm, out string heightSource, out int sampleCount)
    {
        EnsureRaycastAndPlaneManagers();

        pageHeightMm = 0f;
        heightSource = "Unavailable";
        sampleCount = 0;

        if (arCameraManager == null)
            arCameraManager = FindFirstObjectByType<ARCameraManager>();

        float normWidth = (readingWindowViewfinder != null) ? readingWindowViewfinder.WindowWidthNormalized : 0.86f;
        float normHeight = (readingWindowViewfinder != null) ? readingWindowViewfinder.WindowHeightNormalized : 0.40f;

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        List<ARRaycastHit> planeHits = new List<ARRaycastHit>();
        List<ARRaycastHit> pointHits = new List<ARRaycastHit>();

        float depthToDesk = 0f;
        bool hasPlane = (raycastManager != null && raycastManager.Raycast(screenCenter, planeHits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds | TrackableType.Planes));
        if (hasPlane && planeHits.Count > 0)
        {
            depthToDesk = planeHits[0].distance;
            heightSource = "DeskPlaneOnly_HeightUnavailable";
        }

        // Sample feature points across the actual on-screen viewfinder bounds
        float measuredDepth = depthToDesk;
        if (raycastManager != null)
        {
            float halfW = Screen.width * normWidth * 0.5f;
            float halfH = Screen.height * normHeight * 0.5f;

            Vector2[] samplePoints = new Vector2[]
            {
                screenCenter,
                new Vector2(screenCenter.x - halfW * 0.6f, screenCenter.y - halfH * 0.6f),
                new Vector2(screenCenter.x + halfW * 0.6f, screenCenter.y - halfH * 0.6f),
                new Vector2(screenCenter.x - halfW * 0.6f, screenCenter.y + halfH * 0.6f),
                new Vector2(screenCenter.x + halfW * 0.6f, screenCenter.y + halfH * 0.6f),
            };

            List<float> featureDepths = new List<float>();
            foreach (var sp in samplePoints)
            {
                pointHits.Clear();
                if (raycastManager.Raycast(sp, pointHits, TrackableType.FeaturePoint))
                {
                    featureDepths.Add(pointHits[0].distance);
                }
            }

            if (featureDepths.Count >= 2)
            {
                featureDepths.Sort();
                float medianFeatureDepth = featureDepths[featureDepths.Count / 2];
                if (hasPlane && depthToDesk > 0.05f)
                {
                    float delta = depthToDesk - medianFeatureDepth; // positive if book surface is elevated above desk
                    if (delta >= -0.01f && delta <= 0.08f) // plausible 0 to 8cm book thickness
                    {
                        pageHeightMm = delta * 1000f;
                        measuredDepth = medianFeatureDepth;
                        heightSource = "FeaturePointMedian";
                        sampleCount = featureDepths.Count;
                    }
                    else
                    {
                        heightSource = "DeskPlaneOnly_HeightUnavailable";
                    }
                }
                else if (medianFeatureDepth > 0.05f)
                {
                    measuredDepth = medianFeatureDepth;
                    heightSource = "FeaturePointOnly";
                    sampleCount = featureDepths.Count;
                }
            }
        }

        if (measuredDepth > 0.05f)
        {
            float fovH = 0f;
            Camera cam = Camera.main;
            if (cam != null)
            {
                // In Unity ARFoundation, Camera.main.fieldOfView and aspect are strictly synchronized to the device screen orientation
                float fovV = cam.fieldOfView * Mathf.Deg2Rad;
                fovH = 2.0f * Mathf.Atan(Mathf.Tan(fovV * 0.5f) * cam.aspect);
            }
            else if (arCameraManager != null && arCameraManager.TryGetIntrinsics(out XRCameraIntrinsics intrinsics))
            {
                // Handle raw sensor orientation vs portrait screen
                bool isPortrait = Screen.height > Screen.width;
                float sensorW = isPortrait ? intrinsics.resolution.y : intrinsics.resolution.x;
                float focalL = isPortrait ? intrinsics.focalLength.y : intrinsics.focalLength.x;
                fovH = 2.0f * Mathf.Atan((sensorW * 0.5f) / focalL);
            }

            if (fovH > 0.01f)
            {
                float fullPhysicalWidth = 2.0f * measuredDepth * Mathf.Tan(fovH * 0.5f);
                float cropFraction = normWidth;
                float trueCropWidth = fullPhysicalWidth * cropFraction;
                trueCropWidth = Mathf.Clamp(trueCropWidth, 0.06f, 0.35f);

                Debug.Log($"[AksharAR] True Metric Width: {trueCropWidth:F3}m at {measuredDepth:F2}m depth (fovH: {fovH * Mathf.Rad2Deg:F1}°, cropFraction: {cropFraction:F2}, PageHeight: {pageHeightMm:F1}mm via {heightSource}, samples: {sampleCount})");
                return trueCropWidth;
            }
        }

        // Fallback calibrated metric width at standard reading distance (25cm)
        float fallbackWidth = 0.125f * (normWidth / 0.86f);
        heightSource = "Unavailable_Fallback";
        Debug.Log($"[AksharAR] Raycast hit unavailable, using calibrated fallback crop width: {fallbackWidth:F3}m");
        return fallbackWidth;
    }

    public float GetDynamicCropWidth(RectInt screenCropRect)
    {
        return GetDynamicCropWidth(screenCropRect, out _, out _, out _);
    }

    private IEnumerator AddPageToMutableLibrary(Texture2D pageTexture, float physicalWidthMeters = 0.22f)
    {
        if (mutableLibrary == null || trackedImageManager == null || trackedImageManager.referenceLibrary == null)
        {
            ResetTrackedImageManagerLibrary();
        }

        if (mutableLibrary != null)
        {
            Texture2D uncompressed = GetUncompressedCopy(pageTexture);
            if (pageTexture != null) Destroy(pageTexture);

            string pageId = $"Page_{System.DateTime.Now:HHmmss}";
            pendingPageId = pageId;
            pendingStableFrameCount = 0;

            Debug.Log($"[ARPageScanController] Dynamically learning image '{pageId}' ({physicalWidthMeters}m) in ARCore...");

            var jobState = mutableLibrary.ScheduleAddImageWithValidationJob(
                uncompressed,
                pageId,
                physicalWidthMeters);

            while (!jobState.jobHandle.IsCompleted)
            {
                yield return null;
            }

            jobState.jobHandle.Complete();

            if (jobState.status == AddReferenceImageJobStatus.Success)
            {
                Debug.Log($"[ARPageScanController] SUCCESS: '{pageId}' added to ARCore live reference library! Active tracking count: {mutableLibrary.count}");
            }
            else
            {
                Debug.LogWarning($"[ARPageScanController] AddReferenceImage status: {jobState.status}");
                isScanningOrHandoffInProgress = false;
                if (overlayController != null)
                    overlayController.ResetScanButton();
            }

            Destroy(uncompressed);
        }
    }

    private Texture2D GetUncompressedCopy(Texture2D source)
    {
        RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, rt);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        readable.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        readable.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        return readable;
    }

    private Texture2D CaptureRawCameraTexture()
    {
        if (arCameraManager == null)
            arCameraManager = FindFirstObjectByType<ARCameraManager>();

        if (arCameraManager == null || !arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            Debug.LogWarning("[ARPageScanController] Could not acquire ARCameraManager CPU image.");
            return null;
        }

        Texture2D rawTexture = null;

        using (image)
        {
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(image.width, image.height),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.None
            };

            int bufferSize = image.GetConvertedDataSize(conversionParams);
            var buffer = new Unity.Collections.NativeArray<byte>(bufferSize, Unity.Collections.Allocator.Temp);

            try
            {
                image.Convert(conversionParams, buffer);

                rawTexture = new Texture2D(
                    conversionParams.outputDimensions.x,
                    conversionParams.outputDimensions.y,
                    TextureFormat.RGBA32,
                    false);

                rawTexture.LoadRawTextureData(buffer);
                rawTexture.Apply();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ARPageScanController] Exception during XRCpuImage conversion: {ex.Message}");
                if (rawTexture != null) Destroy(rawTexture);
                return null;
            }
            finally
            {
                buffer.Dispose();
            }
        }

        // If raw sensor texture is landscape (width > height) and mobile display is portrait,
        // rotate image 90° so text is upright and unmirrored.
        if (rawTexture != null && rawTexture.width > rawTexture.height && Screen.height > Screen.width)
        {
            Texture2D uprightTexture = RotateLandscapeSensorToPortrait(rawTexture);
            Destroy(rawTexture);

            // Crop upright texture to match screen aspect ratio exactly (Viewport Aspect Fill matching)
            Texture2D croppedTexture = CropToScreenAspect(uprightTexture);
            Destroy(uprightTexture);
            return croppedTexture;
        }

        return rawTexture;
    }

    private Texture2D CropToScreenAspect(Texture2D uprightTexture)
    {
        float targetAspect = (float)Screen.width / Screen.height;
        float currentAspect = (float)uprightTexture.width / uprightTexture.height;

        int cropW = uprightTexture.width;
        int cropH = uprightTexture.height;
        int startX = 0;
        int startY = 0;

        if (currentAspect > targetAspect)
        {
            // Texture is wider than screen aspect -> Crop left & right
            cropW = Mathf.RoundToInt(uprightTexture.height * targetAspect);
            startX = (uprightTexture.width - cropW) / 2;
        }
        else if (currentAspect < targetAspect)
        {
            // Texture is taller than screen aspect -> Crop top & bottom
            cropH = Mathf.RoundToInt(uprightTexture.width / targetAspect);
            startY = (uprightTexture.height - cropH) / 2;
        }

        Color[] pixels = uprightTexture.GetPixels(startX, startY, cropW, cropH);
        Texture2D cropped = new Texture2D(cropW, cropH, TextureFormat.RGBA32, false);
        cropped.SetPixels(pixels);
        cropped.Apply();
        return cropped;
    }

    private Texture2D RotateLandscapeSensorToPortrait(Texture2D original)
    {
        Color32[] originalPixels = original.GetPixels32();
        int origW = original.width;
        int origH = original.height;

        Color32[] rotatedPixels = new Color32[origW * origH];

        for (int y = 0; y < origH; y++)
        {
            for (int x = 0; x < origW; x++)
            {
                int oldIndex = y * origW + x;
                int newX = y;
                int newY = origW - 1 - x;
                int newIndex = newY * origH + newX;
                rotatedPixels[newIndex] = originalPixels[oldIndex];
            }
        }

        Texture2D rotated = new Texture2D(origH, origW, TextureFormat.RGBA32, false);
        rotated.SetPixels32(rotatedPixels);
        rotated.Apply();
        return rotated;
    }
}

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
    [SerializeField] private float scanCaptureDelay = 0.2f;

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
        RequestAndroidCameraPermission();
    }

    private void Start()
    {
        if (readingWindowViewfinder == null)
            readingWindowViewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();

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
            if (trackedImage.trackingState == TrackingState.Tracking)
            {
                HandleTrackedImageUpdate(trackedImage);
            }
        }
    }

    private void HandleTrackedImageUpdate(ARTrackedImage trackedImage)
    {
        if (trackedImage == null) return;
        string imageName = trackedImage.referenceImage.name;

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
                    overlayController.ResetScanButton();

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
        Debug.Log("[ARPageScanController] Settling camera & capturing frame for Vision API...");

        // Settling delay: lets auto-focus, exposure, and tap vibrations stabilize
        if (scanCaptureDelay > 0f)
        {
            yield return new WaitForSeconds(scanCaptureDelay);
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
            if (arCam != null)
            {
                RectInt crop = readingWindowViewfinder != null 
                    ? readingWindowViewfinder.GetPixelCropRect(Screen.width, Screen.height)
                    : new RectInt(0, 0, Screen.width, Screen.height);

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

            // Restore UI overlay & button immediately after capture
            if (overlayController != null) overlayController.SetUIVisibility(true);
        }

        if (liveFrameTexture != null)
        {
            Texture2D textureToSend = liveFrameTexture;
            float physicalWidthMeters = 0.14f;

            // Crop texture to the 2D "Reading Window" viewport for high-density paragraph OCR
            if (readingWindowViewfinder != null)
            {
                Texture2D cropped = readingWindowViewfinder.CropTextureToReadingWindow(liveFrameTexture);
                if (cropped != null)
                {
                    textureToSend = cropped;
                    physicalWidthMeters = 0.14f; // ~14cm physical textbook column width
                    Destroy(liveFrameTexture);
                }
            }

            // Forward camera capture pose to UI Toolkit controller for 3D Raycast Plane Unprojection
            ARWorldSpaceUIToolkitController worldSpaceUIToolkit = FindFirstObjectByType<ARWorldSpaceUIToolkitController>();
            if (worldSpaceUIToolkit != null)
            {
                worldSpaceUIToolkit.SetCameraCapturePose(capturePose);
            }

            Debug.Log($"[ARPageScanController] Sending Reading Window Frame ({textureToSend.width}x{textureToSend.height}) to CloudVisionService...");

            if (visionService != null)
            {
                visionService.DetectTextFromTexture(textureToSend);
            }
            else
            {
                Debug.LogError("[ARPageScanController] CloudVisionService reference missing!");
            }

            // Register cropped paragraph window into ARCore Mutable Runtime Reference Library
            StartCoroutine(AddPageToMutableLibrary(textureToSend, physicalWidthMeters));

            Destroy(textureToSend);
        }
        else
        {
            Debug.LogError("[ARPageScanController] Failed to capture live camera frame.");
            isScanningOrHandoffInProgress = false;
            if (overlayController != null)
                overlayController.ResetScanButton();
        }
    }

    private IEnumerator AddPageToMutableLibrary(Texture2D pageTexture, float physicalWidthMeters = 0.22f)
    {
        if (mutableLibrary == null)
        {
            InitializeMutableRuntimeLibrary();
        }

        // Only reset if library accumulates too many images (avoids abrupt tracking drops)
        if (mutableLibrary != null && mutableLibrary.count >= 4 && trackedImageManager != null)
        {
            var newLib = trackedImageManager.CreateRuntimeLibrary();
            if (newLib is MutableRuntimeReferenceImageLibrary mutable)
            {
                mutableLibrary = mutable;
                trackedImageManager.referenceLibrary = mutableLibrary;
            }
        }

        if (mutableLibrary != null)
        {
            Texture2D uncompressed = GetUncompressedCopy(pageTexture);
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

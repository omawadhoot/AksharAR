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

    private bool isScanning = false;
    private float lastScanTime = -10f;
    private MutableRuntimeReferenceImageLibrary mutableLibrary;

    private void Awake()
    {
        RequestAndroidCameraPermission();
    }

    private void Start()
    {
        InitializeMutableRuntimeLibrary();
    }

    private void InitializeMutableRuntimeLibrary()
    {
        if (trackedImageManager == null)
            trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>();

        if (trackedImageManager != null)
        {
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

        if (trackedImageManager != null)
        {
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
            AlignCanvasToTrackedImage(trackedImage);
        }

        foreach (ARTrackedImage trackedImage in eventArgs.updated)
        {
            if (trackedImage.trackingState == TrackingState.Tracking)
            {
                AlignCanvasToTrackedImage(trackedImage);
            }
        }
    }

    private void AlignCanvasToTrackedImage(ARTrackedImage trackedImage)
    {
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
        if (isScanning) return;
        if (Time.time - lastScanTime < scanCooldown)
        {
            Debug.Log("[ARPageScanController] Scan cooldown active. Please wait.");
            return;
        }

        StartCoroutine(CaptureAndScanFrame());
    }

    private IEnumerator CaptureAndScanFrame()
    {
        isScanning = true;
        lastScanTime = Time.time;
        Debug.Log("[ARPageScanController] Capturing camera frame for Vision API & Dynamic AR Tracking...");

        Texture2D liveFrameTexture = null;

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

            liveFrameTexture = ScreenCapture.CaptureScreenshotAsTexture();

            // Restore UI overlay & button immediately after capture
            if (overlayController != null) overlayController.SetUIVisibility(true);
        }

        if (liveFrameTexture != null)
        {
            Debug.Log($"[ARPageScanController] Upright Frame Captured ({liveFrameTexture.width}x{liveFrameTexture.height}). Sending to CloudVisionService...");

            if (visionService != null)
            {
                visionService.DetectTextFromTexture(liveFrameTexture);
            }
            else
            {
                Debug.LogError("[ARPageScanController] CloudVisionService reference missing!");
            }

            // Register captured page into ARTrackedImageManager's Mutable Runtime Reference Library
            StartCoroutine(AddPageToMutableLibrary(liveFrameTexture));

            Destroy(liveFrameTexture);
        }
        else
        {
            Debug.LogError("[ARPageScanController] Failed to capture live camera frame.");
        }

        isScanning = false;
    }

    private IEnumerator AddPageToMutableLibrary(Texture2D pageTexture)
    {
        if (mutableLibrary == null)
        {
            InitializeMutableRuntimeLibrary();
        }

        if (mutableLibrary != null)
        {
            Texture2D uncompressed = GetUncompressedCopy(pageTexture);
            string pageId = $"Page_{System.DateTime.Now:HHmmss}";
            float physicalWidthMeters = 0.22f; // ~22cm textbook page width

            Debug.Log($"[ARPageScanController] Dynamically learning image '{pageId}' in ARCore...");

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

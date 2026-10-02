using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Active textbook framing mode.
/// Poem: compact, centered lines for verses and stanzas (Portrait).
/// Chapter: wide, multi-line prose framing for dense chapter paragraphs (Landscape).
/// </summary>
public enum ReadingMode
{
    Poem,
    Chapter
}

/// <summary>
/// Manages the 2D Reading Window Viewfinder entirely via UI Toolkit.
/// Controls the letterbox masks, frosted glass interior, corner brackets,
/// top horizon level pill, and interactive touch drag handles (Bottom, Right, Corner).
/// Crops camera images for high-resolution OCR and targeted AR tracking.
/// </summary>
public class ReadingWindowViewfinder : MonoBehaviour
{
    [Header("UI Toolkit Document")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Reading Modes & Presets")]
    [Tooltip("Currently selected reading mode")]
    [SerializeField] private ReadingMode activeReadingMode = ReadingMode.Poem;

    [Tooltip("Default dimensions in Poem Mode (short lines / stanzas)")]
    [SerializeField] private Vector2 poemModeDimensions = new Vector2(0.86f, 0.38f);
    [SerializeField] private float poemModeCenterY = 0.54f;

    [Tooltip("Default dimensions in Chapter Mode (dense chapter paragraphs)")]
    [SerializeField] private Vector2 chapterModeDimensions = new Vector2(0.80f, 0.60f);
    [SerializeField] private float chapterModeCenterY = 0.52f;

    [Header("Viewfinder Dimensions (Normalized Screen 0.0 - 1.0)")]
    [Range(0.28f, 0.98f)]
    [SerializeField] private float windowWidthNormalized = 0.86f;

    [Range(0.12f, 0.75f)]
    [SerializeField] private float windowHeightNormalized = 0.38f;

    [Range(0.2f, 0.8f)]
    [SerializeField] private float windowCenterYNormalized = 0.54f;

    [Header("Resizing Bounds (Normalized 0.0 - 1.0)")]
    [SerializeField] private float minWidthNormalized = 0.28f;
    [SerializeField] private float maxWidthNormalized = 0.96f;
    [SerializeField] private float minHeightNormalized = 0.12f;
    [SerializeField] private float maxHeightNormalized = 0.60f;
    [SerializeField] private float minBottomDistanceNormalized = 0.18f;

    [Header("Active Level Gatekeeper")]
    [Range(3f, 15f)]
    [SerializeField] private float maxLevelTiltAngle = 7.0f;
    [SerializeField] private bool enableSoftGatekeeping = true;

    // Public properties for external queries (used by ARPageScanController)
    public float WindowWidthNormalized => windowWidthNormalized;
    public float WindowHeightNormalized => windowHeightNormalized;
    public bool IsDeviceLevel { get; private set; } = true;
    public float CurrentTiltAngle { get; private set; } = 0f;
    public bool IsLandscapeMode => Screen.width > Screen.height;
    public ReadingMode ActiveReadingMode => activeReadingMode;

    // UI Toolkit Visual Elements
    private VisualElement rootElement;
    private VisualElement viewfinderRoot;
    private VisualElement readingBox;
    private VisualElement readingTint;
    private VisualElement topMask;
    private VisualElement bottomMask;
    private VisualElement leftMask;
    private VisualElement rightMask;

    private VisualElement levelPill;
    private Label levelPillText;

    private VisualElement bottomHandle;
    private VisualElement rightHandle;
    private VisualElement cornerHandle;

    private float initialTopEdge = 0.73f;
    private bool isDraggingBottom = false;
    private bool isDraggingRight = false;
    private bool isDraggingCorner = false;

    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;

    private void Awake()
    {
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;

        ApplyOrientationForMode(activeReadingMode);
        ApplyModePreset(activeReadingMode);
    }

    private void Start()
    {
        InitializeUIToolkit();
    }

    private void Update()
    {
        CheckScreenSizeChange();
        UpdateDeviceTiltAndReticleColor();
        HandleTwoFingerPinch();
    }

    private void InitializeUIToolkit()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument == null)
        {
            var docObj = GameObject.Find("ARLineOverlayDocument");
            if (docObj != null)
                uiDocument = docObj.GetComponent<UIDocument>();
        }

        if (uiDocument == null)
            uiDocument = FindFirstObjectByType<UIDocument>();

        if (uiDocument == null)
        {
            Debug.LogError("[ReadingWindowViewfinder] UIDocument not found!");
            return;
        }

        rootElement = uiDocument.rootVisualElement;
        if (rootElement == null) return;

        viewfinderRoot = rootElement.Q<VisualElement>("ViewfinderRoot");
        readingBox = rootElement.Q<VisualElement>("ReadingBox");
        readingTint = rootElement.Q<VisualElement>("ReadingTint");
        topMask = rootElement.Q<VisualElement>("TopMask");
        bottomMask = rootElement.Q<VisualElement>("BottomMask");
        leftMask = rootElement.Q<VisualElement>("LeftMask");
        rightMask = rootElement.Q<VisualElement>("RightMask");

        levelPill = rootElement.Q<VisualElement>("LevelPill");
        levelPillText = rootElement.Q<Label>("LevelPillText");

        bottomHandle = rootElement.Q<VisualElement>("BottomHandle");
        rightHandle = rootElement.Q<VisualElement>("RightHandle");
        cornerHandle = rootElement.Q<VisualElement>("CornerHandle");

        BindHandleCallbacks();
        UpdateLayout();
        Debug.Log("[ReadingWindowViewfinder] UI Toolkit Viewfinder initialized successfully.");
    }

    private void BindHandleCallbacks()
    {
        // ── Bottom Drag Handle (Vertical resize) ──
        if (bottomHandle != null)
        {
            bottomHandle.RegisterCallback<PointerDownEvent>(evt =>
            {
                initialTopEdge = windowCenterYNormalized + (windowHeightNormalized / 2f);
                isDraggingBottom = true;
                bottomHandle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            bottomHandle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (isDraggingBottom && bottomHandle.HasPointerCapture(evt.pointerId))
                {
                    float panelHeight = GetPanelHeight();
                    float pointerYNorm = 1f - (evt.position.y / panelHeight);
                    ResizeVertical(pointerYNorm);
                    UpdateLayout();
                    evt.StopPropagation();
                }
            });
            bottomHandle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (bottomHandle.HasPointerCapture(evt.pointerId))
                    bottomHandle.ReleasePointer(evt.pointerId);
                isDraggingBottom = false;
                evt.StopPropagation();
            });
            bottomHandle.RegisterCallback<PointerCaptureOutEvent>(evt => isDraggingBottom = false);
        }

        // ── Right Drag Handle (Horizontal resize) ──
        if (rightHandle != null)
        {
            rightHandle.RegisterCallback<PointerDownEvent>(evt =>
            {
                isDraggingRight = true;
                rightHandle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            rightHandle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (isDraggingRight && rightHandle.HasPointerCapture(evt.pointerId))
                {
                    float panelWidth = GetPanelWidth();
                    float pointerXNorm = evt.position.x / panelWidth;
                    ResizeHorizontal(pointerXNorm);
                    UpdateLayout();
                    evt.StopPropagation();
                }
            });
            rightHandle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (rightHandle.HasPointerCapture(evt.pointerId))
                    rightHandle.ReleasePointer(evt.pointerId);
                isDraggingRight = false;
                evt.StopPropagation();
            });
            rightHandle.RegisterCallback<PointerCaptureOutEvent>(evt => isDraggingRight = false);
        }

        // ── Corner BR Drag Handle (2-axis resize) ──
        if (cornerHandle != null)
        {
            cornerHandle.RegisterCallback<PointerDownEvent>(evt =>
            {
                initialTopEdge = windowCenterYNormalized + (windowHeightNormalized / 2f);
                isDraggingCorner = true;
                cornerHandle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            cornerHandle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (isDraggingCorner && cornerHandle.HasPointerCapture(evt.pointerId))
                {
                    float panelWidth = GetPanelWidth();
                    float panelHeight = GetPanelHeight();
                    float pointerXNorm = evt.position.x / panelWidth;
                    float pointerYNorm = 1f - (evt.position.y / panelHeight);

                    ResizeHorizontal(pointerXNorm);
                    ResizeVertical(pointerYNorm);
                    UpdateLayout();
                    evt.StopPropagation();
                }
            });
            cornerHandle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (cornerHandle.HasPointerCapture(evt.pointerId))
                    cornerHandle.ReleasePointer(evt.pointerId);
                isDraggingCorner = false;
                evt.StopPropagation();
            });
            cornerHandle.RegisterCallback<PointerCaptureOutEvent>(evt => isDraggingCorner = false);
        }
    }

    private float GetPanelWidth()
    {
        if (viewfinderRoot != null && viewfinderRoot.layout.width > 10f)
            return viewfinderRoot.layout.width;
        return Screen.width > 0 ? Screen.width : 1080f;
    }

    private float GetPanelHeight()
    {
        if (viewfinderRoot != null && viewfinderRoot.layout.height > 10f)
            return viewfinderRoot.layout.height;
        return Screen.height > 0 ? Screen.height : 1920f;
    }

    private void ResizeVertical(float pointerYNorm)
    {
        float minBottom = Mathf.Max(minBottomDistanceNormalized, initialTopEdge - maxHeightNormalized);
        float maxBottom = initialTopEdge - minHeightNormalized;
        float newBottomNorm = Mathf.Clamp(pointerYNorm, minBottom, maxBottom);

        float newHeight = initialTopEdge - newBottomNorm;
        windowHeightNormalized = newHeight;
        windowCenterYNormalized = initialTopEdge - (newHeight / 2f);
    }

    private void ResizeHorizontal(float pointerXNorm)
    {
        // Symmetrical expansion from horizontal center (0.5)
        float halfWidth = Mathf.Clamp(Mathf.Abs(pointerXNorm - 0.5f), minWidthNormalized * 0.5f, maxWidthNormalized * 0.5f);
        windowWidthNormalized = halfWidth * 2f;
    }

    private void CheckScreenSizeChange()
    {
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight)
        {
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            UpdateLayout();
        }
    }

    public void SetReadingMode(ReadingMode mode)
    {
        activeReadingMode = mode;
        ApplyOrientationForMode(activeReadingMode);
        ApplyModePreset(activeReadingMode);
        UpdateDeviceTiltAndReticleColor();

        ARLineOverlayController overlayCtrl = FindFirstObjectByType<ARLineOverlayController>();
        if (overlayCtrl != null)
        {
            overlayCtrl.SyncModeCarousel(mode);
        }

        Debug.Log($"[ReadingWindowViewfinder] UI switched Reading Mode to {activeReadingMode} | Orientation: {Screen.orientation}");
    }

    private void ApplyOrientationForMode(ReadingMode mode)
    {
        if (mode == ReadingMode.Chapter)
        {
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
        }
        else
        {
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToPortrait = true;
            Screen.orientation = ScreenOrientation.Portrait;
        }
    }

    public void ApplyModePreset(ReadingMode mode)
    {
        if (mode == ReadingMode.Chapter)
        {
            windowWidthNormalized = chapterModeDimensions.x;
            windowHeightNormalized = chapterModeDimensions.y;
            windowCenterYNormalized = chapterModeCenterY;
            maxHeightNormalized = 0.72f;
            minBottomDistanceNormalized = 0.14f;
        }
        else
        {
            windowWidthNormalized = poemModeDimensions.x;
            windowHeightNormalized = poemModeDimensions.y;
            windowCenterYNormalized = poemModeCenterY;
            maxHeightNormalized = 0.60f;
            minBottomDistanceNormalized = 0.18f;
        }

        UpdateLayout();
    }

    private void HandleTwoFingerPinch()
    {
        if (Input.touchCount == 2)
        {
            Touch touch0 = Input.GetTouch(0);
            Touch touch1 = Input.GetTouch(1);

            Vector2 prevPos0 = touch0.position - touch0.deltaPosition;
            Vector2 prevPos1 = touch1.position - touch1.deltaPosition;

            float prevDistanceX = Mathf.Abs(prevPos0.x - prevPos1.x);
            float currentDistanceX = Mathf.Abs(touch0.position.x - touch1.position.x);

            float prevDistanceY = Mathf.Abs(prevPos0.y - prevPos1.y);
            float currentDistanceY = Mathf.Abs(touch0.position.y - touch1.position.y);

            float deltaX = (currentDistanceX - prevDistanceX) / (float)Screen.width;
            float deltaY = (currentDistanceY - prevDistanceY) / (float)Screen.height;

            bool changed = false;
            if (Mathf.Abs(deltaX) > 0.0015f)
            {
                windowWidthNormalized = Mathf.Clamp(windowWidthNormalized + deltaX * 1.5f, minWidthNormalized, maxWidthNormalized);
                changed = true;
            }

            if (Mathf.Abs(deltaY) > 0.0015f)
            {
                windowHeightNormalized = Mathf.Clamp(windowHeightNormalized + deltaY * 1.5f, minHeightNormalized, maxHeightNormalized);
                changed = true;
            }

            if (changed)
            {
                UpdateLayout();
            }
        }
    }

    private void UpdateDeviceTiltAndReticleColor()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            CurrentTiltAngle = Vector3.Angle(cam.transform.forward, Vector3.down);
            IsDeviceLevel = CurrentTiltAngle <= maxLevelTiltAngle;
        }
        else
        {
            IsDeviceLevel = true;
            CurrentTiltAngle = 0f;
        }

        if (enableSoftGatekeeping)
        {
            if (levelPill != null && levelPillText != null)
            {
                levelPill.EnableInClassList("level-pill--level", IsDeviceLevel);
                levelPill.EnableInClassList("level-pill--tilted", !IsDeviceLevel);
                levelPillText.text = IsDeviceLevel ? "✨ LEVEL" : $"📐 TILT {CurrentTiltAngle:F0}°";
            }

            if (readingBox != null)
                readingBox.EnableInClassList("reading-box--level", IsDeviceLevel);

            if (readingTint != null)
                readingTint.EnableInClassList("reading-tint--clear", IsDeviceLevel);
        }
    }

    private void UpdateLayout()
    {
        float leftPercent = ((1f - windowWidthNormalized) / 2f) * 100f;
        float rightPercent = leftPercent;
        float bottomPercent = (windowCenterYNormalized - (windowHeightNormalized / 2f)) * 100f;
        float topPercent = (1f - (windowCenterYNormalized + (windowHeightNormalized / 2f))) * 100f;

        if (readingBox != null)
        {
            readingBox.style.left = Length.Percent(leftPercent);
            readingBox.style.top = Length.Percent(topPercent);
            readingBox.style.width = Length.Percent(windowWidthNormalized * 100f);
            readingBox.style.height = Length.Percent(windowHeightNormalized * 100f);
        }

        if (topMask != null)
            topMask.style.height = Length.Percent(topPercent);

        if (bottomMask != null)
            bottomMask.style.height = Length.Percent(bottomPercent);

        if (leftMask != null)
        {
            leftMask.style.top = Length.Percent(topPercent);
            leftMask.style.bottom = Length.Percent(bottomPercent);
            leftMask.style.width = Length.Percent(leftPercent);
        }

        if (rightMask != null)
        {
            rightMask.style.top = Length.Percent(topPercent);
            rightMask.style.bottom = Length.Percent(bottomPercent);
            rightMask.style.width = Length.Percent(rightPercent);
        }
    }

    /// <summary>
    /// Returns the normalized UV Rect [0..1] of the reading window relative to the full screen.
    /// </summary>
    public Rect GetNormalizedCropRect()
    {
        float left = (1f - windowWidthNormalized) / 2f;
        float bottom = windowCenterYNormalized - (windowHeightNormalized / 2f);
        return new Rect(left, bottom, windowWidthNormalized, windowHeightNormalized);
    }

    /// <summary>
    /// Returns the exact pixel RectInt corresponding to the reading window on an input texture.
    /// </summary>
    public RectInt GetPixelCropRect(int textureWidth, int textureHeight)
    {
        Rect norm = GetNormalizedCropRect();
        int x = Mathf.Clamp(Mathf.RoundToInt(norm.x * textureWidth), 0, textureWidth - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(norm.y * textureHeight), 0, textureHeight - 1);
        int w = Mathf.Clamp(Mathf.RoundToInt(norm.width * textureWidth), 1, textureWidth - x);
        int h = Mathf.Clamp(Mathf.RoundToInt(norm.height * textureHeight), 1, textureHeight - y);

        return new RectInt(x, y, w, h);
    }

    /// <summary>
    /// Crops a full-screen Texture2D down to the reading window boundaries.
    /// </summary>
    public Texture2D CropTextureToReadingWindow(Texture2D source)
    {
        if (source == null) return null;

        RectInt cropRect = GetPixelCropRect(source.width, source.height);
        Color[] pixels = source.GetPixels(cropRect.x, cropRect.y, cropRect.width, cropRect.height);
        Texture2D cropped = new Texture2D(cropRect.width, cropRect.height, TextureFormat.RGBA32, false);
        cropped.SetPixels(pixels);
        cropped.Apply();

        return cropped;
    }

    public void SetVisibility(bool visible)
    {
        if (viewfinderRoot != null)
        {
            viewfinderRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}

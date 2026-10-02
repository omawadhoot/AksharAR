using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Type of drag handle for interactive viewfinder resizing.
/// </summary>
public enum DragHandleType
{
    Bottom,   // Vertical height adjustment (top-anchored)
    Right,    // Horizontal width adjustment (symmetric)
    CornerBR  // Simultaneous 2-axis width and height adjustment
}

/// <summary>
/// Active textbook framing mode.
/// Poem: compact, centered lines for verses and stanzas.
/// Chapter: wide, multi-line prose framing for dense chapter paragraphs.
/// </summary>
public enum ReadingMode
{
    Poem,
    Chapter
}

/// <summary>
/// Renders a stylized 2D "Reading Window" viewfinder overlay on the screen HUD.
/// Guides the user to frame a textbook paragraph and crops the camera image 
/// for high-resolution OCR and targeted AR tracking.
/// Features touch-interactive bottom, right, and corner drag handles to resize 
/// both horizontally (up to 96% screen width) and vertically, plus pinch-to-resize.
/// Includes an Active Level Gatekeeper that shifts the reticle to Emerald Green when level.
/// Features a child-friendly UI Mode Selector to toggle between Poem Mode and Chapter Mode.
/// </summary>
public class ReadingWindowViewfinder : MonoBehaviour
{
    [Header("Reading Modes & Presets (Managed via UI)")]
    [Tooltip("Currently selected reading mode")]
    [SerializeField] private ReadingMode activeReadingMode = ReadingMode.Poem;

    [Tooltip("Default dimensions in Poem Mode (short lines / stanzas)")]
    [SerializeField] private Vector2 poemModeDimensions = new Vector2(0.86f, 0.38f);
    [SerializeField] private float poemModeCenterY = 0.54f;

    [Tooltip("Default dimensions in Chapter Mode (dense chapter paragraphs)")]
    [SerializeField] private Vector2 chapterModeDimensions = new Vector2(0.80f, 0.60f);
    [SerializeField] private float chapterModeCenterY = 0.52f;

    [Header("Viewfinder Dimensions (Normalized Screen 0.0 - 1.0)")]
    [Tooltip("Normalized horizontal size (0.0 to 1.0) of the reading box")]
    [Range(0.28f, 0.98f)]
    [SerializeField] private float windowWidthNormalized = 0.86f;

    [Tooltip("Normalized vertical size (0.0 to 1.0) of the reading box")]
    [Range(0.12f, 0.75f)]
    [SerializeField] private float windowHeightNormalized = 0.38f;

    [Tooltip("Normalized vertical center position (0.0 = bottom, 1.0 = top)")]
    [Range(0.2f, 0.8f)]
    [SerializeField] private float windowCenterYNormalized = 0.54f;

    [Header("Horizontal Resizing Bounds (Normalized 0.0 - 1.0)")]
    [Tooltip("Minimum normalized width of the reading box")]
    [SerializeField] private float minWidthNormalized = 0.28f;

    [Tooltip("Maximum normalized width of the reading box (almost full screen width)")]
    [SerializeField] private float maxWidthNormalized = 0.96f;

    [Header("Vertical Resizing Bounds (Normalized 0.0 - 1.0)")]
    [Tooltip("Minimum normalized height of the reading box (~2 lines)")]
    [SerializeField] private float minHeightNormalized = 0.12f;

    [Tooltip("Maximum normalized height of the reading box (~10-12 lines)")]
    [SerializeField] private float maxHeightNormalized = 0.60f;

    [Tooltip("Minimum distance from screen bottom (keeps box safely above scan button)")]
    [SerializeField] private float minBottomDistanceNormalized = 0.18f;

    [Header("Active Level Gatekeeper & Frosted Glass Effect")]
    [Tooltip("Maximum tilt angle away from tabletop perpendicular (degrees) considered 'level'")]
    [Range(3f, 15f)]
    [SerializeField] private float maxLevelTiltAngle = 7.0f;

    [Tooltip("Reticle color when phone is held level (Emerald Green)")]
    [SerializeField] private Color levelReticleColor = new Color(0.0f, 0.90f, 0.46f, 0.95f); // #00E676

    [Tooltip("Reticle color when phone is tilted (Soft Sky Blue)")]
    [SerializeField] private Color unlevelReticleColor = new Color(0.20f, 0.60f, 1.0f, 0.90f); // #3399FF

    [Tooltip("Crystal clear window tint when level (100% transparent)")]
    [SerializeField] private Color levelWindowTintColor = new Color(1.0f, 1.0f, 1.0f, 0.0f); // Crystal Clear

    [Tooltip("Translucent frosted glass tint when tilted (Soft milky frosted veil)")]
    [SerializeField] private Color unlevelWindowTintColor = new Color(1.0f, 1.0f, 1.0f, 0.35f); // Frosted Glass

    [Tooltip("Enables dynamic color shifting and level guidance prompt")]
    [SerializeField] private bool enableSoftGatekeeping = true;

    [Header("Visual Styling")]
    [SerializeField] private Color cornerReticleColor = new Color(0.12f, 0.53f, 0.96f, 0.95f); // Neon Blue default
    [SerializeField] private Color handleHighlightColor = new Color(0.40f, 0.85f, 1.0f, 1.0f); // Bright Cyan
    [SerializeField] private Color maskShadeColor = new Color(0f, 0f, 0f, 0.40f); // Darkened vignette
    [SerializeField] private float cornerThickness = 4f;
    [SerializeField] private float cornerLength = 32f;

    [Header("References")]
    [SerializeField] private RectTransform windowBoxTransform;
    [SerializeField] private GameObject visualRoot;

    // Public properties for external queries
    public float WindowWidthNormalized => windowWidthNormalized;
    public float WindowHeightNormalized => windowHeightNormalized;
    public bool IsDeviceLevel { get; private set; } = true;
    public float CurrentTiltAngle { get; private set; } = 0f;
    public bool IsLandscapeMode => Screen.width > Screen.height;
    public ReadingMode ActiveReadingMode => activeReadingMode;

    // Internal references for real-time layout updates
    private RectTransform topMaskRect;
    private RectTransform bottomMaskRect;
    private RectTransform leftMaskRect;
    private RectTransform rightMaskRect;
    private RectTransform hintRect;
    private Text hintTextComponent;

    // Mode Selector UI Elements
    private RectTransform modeSelectorRootRect;
    private Image poemModeBtnBg;
    private Text poemModeBtnText;
    private Image chapterModeBtnBg;
    private Text chapterModeBtnText;

    // Drag Handles
    private RectTransform bottomDragHandleRect;
    private Image bottomDragHandlePillImage;
    private Transform bottomDragHandlePillTransform;

    private RectTransform rightDragHandleRect;
    private Image rightDragHandlePillImage;
    private Transform rightDragHandlePillTransform;

    private RectTransform cornerDragHandleRect;
    private Image cornerDragHandleDotImage;

    // Window interior tint
    private Image windowInteriorTintImage;
    private Color currentDynamicTintColor;

    private readonly List<Image> reticleBracketImages = new List<Image>();
    private Color currentDynamicColor;

    private float initialTopEdge = 0.73f;
    private bool isDraggingBottom = false;
    private bool isDraggingRight = false;
    private bool isDraggingCorner = false;

    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;

    private void Awake()
    {
        currentDynamicColor = cornerReticleColor;
        currentDynamicTintColor = unlevelWindowTintColor;
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        ApplyModePreset(activeReadingMode);
        BuildViewfinderUIIfNeeded();
    }

    private void Update()
    {
        CheckScreenSizeChange();
        UpdateDeviceTiltAndReticleColor();
        HandleTwoFingerPinch();
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

    /// <summary>
    /// Explicitly switches between Poem Mode and Chapter Mode via the UI.
    /// Adjusts viewfinder framing dimensions and updates HUD visuals.
    /// </summary>
    public void SetReadingMode(ReadingMode mode)
    {
        activeReadingMode = mode;
        ApplyModePreset(activeReadingMode);
        UpdateModeSelectorVisuals();
        UpdateDeviceTiltAndReticleColor();
        Debug.Log($"[ReadingWindowViewfinder] UI switched Reading Mode to {activeReadingMode}");
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

        if (visualRoot != null)
        {
            UpdateLayout();
        }
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

        Color targetColor = cornerReticleColor;
        Color targetTintColor = Color.clear;

        if (enableSoftGatekeeping)
        {
            targetColor = IsDeviceLevel ? levelReticleColor : unlevelReticleColor;
            targetTintColor = IsDeviceLevel ? levelWindowTintColor : unlevelWindowTintColor;

            if (hintTextComponent != null)
            {
                if (IsDeviceLevel)
                {
                    string modeTag = (activeReadingMode == ReadingMode.Chapter) ? "📖 Chapter Mode" : "📜 Poem Mode";
                    hintTextComponent.text = $"✨ {modeTag} • Crystal Clear • Tap to scan!";
                    hintTextComponent.color = new Color(0.85f, 1f, 0.90f, 0.98f);
                }
                else
                {
                    hintTextComponent.text = $"🔎 Tilt {CurrentTiltAngle:F0}° • Hold flat to clear glass";
                    hintTextComponent.color = new Color(1f, 1f, 1f, 0.95f);
                }
            }
        }

        currentDynamicColor = Color.Lerp(currentDynamicColor, targetColor, Time.deltaTime * 7f);
        currentDynamicTintColor = Color.Lerp(currentDynamicTintColor, targetTintColor, Time.deltaTime * 7f);

        for (int i = 0; i < reticleBracketImages.Count; i++)
        {
            if (reticleBracketImages[i] != null)
                reticleBracketImages[i].color = currentDynamicColor;
        }

        if (windowInteriorTintImage != null)
            windowInteriorTintImage.color = currentDynamicTintColor;

        if (bottomDragHandlePillImage != null && !isDraggingBottom)
            bottomDragHandlePillImage.color = currentDynamicColor;

        if (rightDragHandlePillImage != null && !isDraggingRight)
            rightDragHandlePillImage.color = currentDynamicColor;

        if (cornerDragHandleDotImage != null && !isDraggingCorner)
            cornerDragHandleDotImage.color = currentDynamicColor;
    }

    private void BuildViewfinderUIIfNeeded()
    {
        if (visualRoot != null && windowBoxTransform != null) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        visualRoot = new GameObject("ReadingWindow_VisualRoot");
        visualRoot.transform.SetParent(transform, false);

        RectTransform rootRect = visualRoot.AddComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.sizeDelta = Vector2.zero;
        rootRect.anchoredPosition = Vector2.zero;

        // ── 1. Semi-transparent Letterbox Masks ──
        topMaskRect = CreateMaskPanel(rootRect, "TopMask", maskShadeColor);
        bottomMaskRect = CreateMaskPanel(rootRect, "BottomMask", maskShadeColor);
        leftMaskRect = CreateMaskPanel(rootRect, "LeftMask", maskShadeColor);
        rightMaskRect = CreateMaskPanel(rootRect, "RightMask", maskShadeColor);

        // ── 2. Reading Window Center Box & Corners ──
        GameObject boxObj = new GameObject("ReadingWindow_CenterBox");
        boxObj.transform.SetParent(rootRect, false);
        windowBoxTransform = boxObj.AddComponent<RectTransform>();

        // ── Interior Level Window Tint (subtle red wash when tilted, subtle green wash when level) ──
        GameObject tintObj = new GameObject("ReadingWindow_InteriorTint");
        tintObj.transform.SetParent(windowBoxTransform, false);
        RectTransform tintRect = tintObj.AddComponent<RectTransform>();
        tintRect.anchorMin = Vector2.zero;
        tintRect.anchorMax = Vector2.one;
        tintRect.sizeDelta = Vector2.zero;
        tintRect.anchoredPosition = Vector2.zero;

        windowInteriorTintImage = tintObj.AddComponent<Image>();
        windowInteriorTintImage.color = unlevelWindowTintColor;
        windowInteriorTintImage.raycastTarget = false;

        // Add 4 Corner Brackets
        reticleBracketImages.Clear();
        CreateCornerReticle(windowBoxTransform, "TL", new Vector2(0f, 1f), new Vector2(cornerLength, cornerThickness), new Vector2(cornerThickness, cornerLength));
        CreateCornerReticle(windowBoxTransform, "TR", new Vector2(1f, 1f), new Vector2(cornerLength, cornerThickness), new Vector2(cornerThickness, cornerLength));
        CreateCornerReticle(windowBoxTransform, "BL", new Vector2(0f, 0f), new Vector2(cornerLength, cornerThickness), new Vector2(cornerThickness, cornerLength));
        CreateCornerReticle(windowBoxTransform, "BR", new Vector2(1f, 0f), new Vector2(cornerLength, cornerThickness), new Vector2(cornerThickness, cornerLength));

        // ── 3. Helper Prompt Text ──
        GameObject hintObj = new GameObject("ReadingWindow_HintText");
        hintObj.transform.SetParent(rootRect, false);
        hintRect = hintObj.AddComponent<RectTransform>();
        hintRect.sizeDelta = new Vector2(0f, 36f);
        hintRect.anchoredPosition = new Vector2(0f, 22f);

        hintTextComponent = hintObj.AddComponent<Text>();
        hintTextComponent.text = "📖 Align paragraph inside box";
        hintTextComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (hintTextComponent.font == null) hintTextComponent.font = Font.CreateDynamicFontFromOSFont("Arial", 16);
        hintTextComponent.fontSize = 17;
        hintTextComponent.alignment = TextAnchor.MiddleCenter;
        hintTextComponent.color = new Color(1f, 1f, 1f, 0.95f);
        hintTextComponent.raycastTarget = false;

        Outline outline = hintObj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
        outline.effectDistance = new Vector2(1f, -1f);

        // ── 4. Interactive Drag Handles ──
        BuildBottomDragHandle(rootRect);
        BuildRightDragHandle(rootRect);
        BuildCornerDragHandle(rootRect);

        // ── 5. Child-Friendly Mode Selector UI (Poem vs Chapter) ──
        BuildModeSelectorUI(rootRect);

        // Initial Layout
        UpdateLayout();
    }

    private void BuildModeSelectorUI(RectTransform parent)
    {
        GameObject selectorObj = new GameObject("ReadingWindow_ModeSelector");
        selectorObj.transform.SetParent(parent, false);
        modeSelectorRootRect = selectorObj.AddComponent<RectTransform>();
        modeSelectorRootRect.anchorMin = new Vector2(0.5f, 1f);
        modeSelectorRootRect.anchorMax = new Vector2(0.5f, 1f);
        modeSelectorRootRect.pivot = new Vector2(0.5f, 1f);
        modeSelectorRootRect.anchoredPosition = new Vector2(0f, -22f);
        modeSelectorRootRect.sizeDelta = new Vector2(340f, 44f);

        Image containerBg = selectorObj.AddComponent<Image>();
        containerBg.color = new Color(0.05f, 0.08f, 0.16f, 0.88f);
        containerBg.raycastTarget = false;

        Outline outline = selectorObj.AddComponent<Outline>();
        outline.effectColor = new Color(0.25f, 0.55f, 0.95f, 0.50f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        // ── Poem Mode Button (Left) ──
        GameObject poemBtnObj = new GameObject("Btn_PoemMode");
        poemBtnObj.transform.SetParent(selectorObj.transform, false);
        RectTransform poemBtnRect = poemBtnObj.AddComponent<RectTransform>();
        poemBtnRect.anchorMin = new Vector2(0f, 0.5f);
        poemBtnRect.anchorMax = new Vector2(0f, 0.5f);
        poemBtnRect.pivot = new Vector2(0f, 0.5f);
        poemBtnRect.anchoredPosition = new Vector2(6f, 0f);
        poemBtnRect.sizeDelta = new Vector2(160f, 34f);

        poemModeBtnBg = poemBtnObj.AddComponent<Image>();
        poemModeBtnBg.raycastTarget = true;

        Button poemBtn = poemBtnObj.AddComponent<Button>();
        poemBtn.targetGraphic = poemModeBtnBg;
        poemBtn.onClick.AddListener(() => SetReadingMode(ReadingMode.Poem));

        GameObject poemTextObj = new GameObject("Text");
        poemTextObj.transform.SetParent(poemBtnObj.transform, false);
        RectTransform poemTextRect = poemTextObj.AddComponent<RectTransform>();
        poemTextRect.anchorMin = Vector2.zero;
        poemTextRect.anchorMax = Vector2.one;
        poemTextRect.sizeDelta = Vector2.zero;
        poemTextRect.anchoredPosition = Vector2.zero;

        poemModeBtnText = poemTextObj.AddComponent<Text>();
        poemModeBtnText.text = "📜 Poem Mode";
        poemModeBtnText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (poemModeBtnText.font == null) poemModeBtnText.font = Font.CreateDynamicFontFromOSFont("Arial", 13);
        poemModeBtnText.fontSize = 13;
        poemModeBtnText.alignment = TextAnchor.MiddleCenter;
        poemModeBtnText.raycastTarget = false;

        // ── Chapter Mode Button (Right) ──
        GameObject chapterBtnObj = new GameObject("Btn_ChapterMode");
        chapterBtnObj.transform.SetParent(selectorObj.transform, false);
        RectTransform chapterBtnRect = chapterBtnObj.AddComponent<RectTransform>();
        chapterBtnRect.anchorMin = new Vector2(1f, 0.5f);
        chapterBtnRect.anchorMax = new Vector2(1f, 0.5f);
        chapterBtnRect.pivot = new Vector2(1f, 0.5f);
        chapterBtnRect.anchoredPosition = new Vector2(-6f, 0f);
        chapterBtnRect.sizeDelta = new Vector2(160f, 34f);

        chapterModeBtnBg = chapterBtnObj.AddComponent<Image>();
        chapterModeBtnBg.raycastTarget = true;

        Button chapterBtn = chapterBtnObj.AddComponent<Button>();
        chapterBtn.targetGraphic = chapterModeBtnBg;
        chapterBtn.onClick.AddListener(() => SetReadingMode(ReadingMode.Chapter));

        GameObject chapterTextObj = new GameObject("Text");
        chapterTextObj.transform.SetParent(chapterBtnObj.transform, false);
        RectTransform chapterTextRect = chapterTextObj.AddComponent<RectTransform>();
        chapterTextRect.anchorMin = Vector2.zero;
        chapterTextRect.anchorMax = Vector2.one;
        chapterTextRect.sizeDelta = Vector2.zero;
        chapterTextRect.anchoredPosition = Vector2.zero;

        chapterModeBtnText = chapterTextObj.AddComponent<Text>();
        chapterModeBtnText.text = "📖 Chapter Mode";
        chapterModeBtnText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (chapterModeBtnText.font == null) chapterModeBtnText.font = Font.CreateDynamicFontFromOSFont("Arial", 13);
        chapterModeBtnText.fontSize = 13;
        chapterModeBtnText.alignment = TextAnchor.MiddleCenter;
        chapterModeBtnText.raycastTarget = false;

        UpdateModeSelectorVisuals();
    }

    private void UpdateModeSelectorVisuals()
    {
        Color activeBg = new Color(0.10f, 0.52f, 0.98f, 0.95f);
        Color inactiveBg = new Color(1f, 1f, 1f, 0.08f);
        Color activeText = Color.white;
        Color inactiveText = new Color(0.80f, 0.85f, 0.95f, 0.65f);

        if (poemModeBtnBg != null)
            poemModeBtnBg.color = (activeReadingMode == ReadingMode.Poem) ? activeBg : inactiveBg;

        if (poemModeBtnText != null)
        {
            poemModeBtnText.color = (activeReadingMode == ReadingMode.Poem) ? activeText : inactiveText;
            poemModeBtnText.fontStyle = (activeReadingMode == ReadingMode.Poem) ? FontStyle.Bold : FontStyle.Normal;
        }

        if (chapterModeBtnBg != null)
            chapterModeBtnBg.color = (activeReadingMode == ReadingMode.Chapter) ? activeBg : inactiveBg;

        if (chapterModeBtnText != null)
        {
            chapterModeBtnText.color = (activeReadingMode == ReadingMode.Chapter) ? activeText : inactiveText;
            chapterModeBtnText.fontStyle = (activeReadingMode == ReadingMode.Chapter) ? FontStyle.Bold : FontStyle.Normal;
        }
    }

    private void BuildBottomDragHandle(RectTransform parent)
    {
        GameObject handleObj = new GameObject("ReadingWindow_BottomHandle");
        handleObj.transform.SetParent(parent, false);
        bottomDragHandleRect = handleObj.AddComponent<RectTransform>();
        bottomDragHandleRect.sizeDelta = new Vector2(180f, 54f); // Touch hit-box

        Image hitImage = handleObj.AddComponent<Image>();
        hitImage.color = new Color(0f, 0f, 0f, 0.001f);
        hitImage.raycastTarget = true;

        GameObject pillObj = new GameObject("Handle_Pill");
        pillObj.transform.SetParent(handleObj.transform, false);
        RectTransform pillRect = pillObj.AddComponent<RectTransform>();
        pillRect.anchorMin = new Vector2(0.5f, 0.5f);
        pillRect.anchorMax = new Vector2(0.5f, 0.5f);
        pillRect.anchoredPosition = new Vector2(0f, 6f);
        pillRect.sizeDelta = new Vector2(64f, 6f);

        bottomDragHandlePillImage = pillObj.AddComponent<Image>();
        bottomDragHandlePillImage.color = cornerReticleColor;
        bottomDragHandlePillImage.raycastTarget = false;
        bottomDragHandlePillTransform = pillObj.transform;

        GameObject labelObj = new GameObject("Handle_Label");
        labelObj.transform.SetParent(handleObj.transform, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = new Vector2(0f, -8f);
        labelRect.sizeDelta = new Vector2(140f, 20f);

        Text labelText = labelObj.AddComponent<Text>();
        labelText.text = "═  ↕  ═";
        labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (labelText.font == null) labelText.font = Font.CreateDynamicFontFromOSFont("Arial", 12);
        labelText.fontSize = 12;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.color = new Color(1f, 1f, 1f, 0.70f);
        labelText.raycastTarget = false;

        ViewfinderDragHandle dragHandle = handleObj.AddComponent<ViewfinderDragHandle>();
        dragHandle.Initialize(this, DragHandleType.Bottom);
    }

    private void BuildRightDragHandle(RectTransform parent)
    {
        GameObject handleObj = new GameObject("ReadingWindow_RightHandle");
        handleObj.transform.SetParent(parent, false);
        rightDragHandleRect = handleObj.AddComponent<RectTransform>();
        rightDragHandleRect.sizeDelta = new Vector2(54f, 160f); // Vertical touch hit-box

        Image hitImage = handleObj.AddComponent<Image>();
        hitImage.color = new Color(0f, 0f, 0f, 0.001f);
        hitImage.raycastTarget = true;

        GameObject pillObj = new GameObject("RightHandle_Pill");
        pillObj.transform.SetParent(handleObj.transform, false);
        RectTransform pillRect = pillObj.AddComponent<RectTransform>();
        pillRect.anchorMin = new Vector2(0.5f, 0.5f);
        pillRect.anchorMax = new Vector2(0.5f, 0.5f);
        pillRect.anchoredPosition = new Vector2(-6f, 0f);
        pillRect.sizeDelta = new Vector2(6f, 64f);

        rightDragHandlePillImage = pillObj.AddComponent<Image>();
        rightDragHandlePillImage.color = cornerReticleColor;
        rightDragHandlePillImage.raycastTarget = false;
        rightDragHandlePillTransform = pillObj.transform;

        GameObject labelObj = new GameObject("RightHandle_Label");
        labelObj.transform.SetParent(handleObj.transform, false);
        RectTransform labelRect = labelObj.AddComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = new Vector2(10f, 0f);
        labelRect.sizeDelta = new Vector2(24f, 60f);

        Text labelText = labelObj.AddComponent<Text>();
        labelText.text = "║\n↔\n║";
        labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (labelText.font == null) labelText.font = Font.CreateDynamicFontFromOSFont("Arial", 11);
        labelText.fontSize = 11;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.color = new Color(1f, 1f, 1f, 0.70f);
        labelText.raycastTarget = false;

        ViewfinderDragHandle dragHandle = handleObj.AddComponent<ViewfinderDragHandle>();
        dragHandle.Initialize(this, DragHandleType.Right);
    }

    private void BuildCornerDragHandle(RectTransform parent)
    {
        GameObject handleObj = new GameObject("ReadingWindow_CornerBRHandle");
        handleObj.transform.SetParent(parent, false);
        cornerDragHandleRect = handleObj.AddComponent<RectTransform>();
        cornerDragHandleRect.sizeDelta = new Vector2(60f, 60f); // Corner touch zone

        Image hitImage = handleObj.AddComponent<Image>();
        hitImage.color = new Color(0f, 0f, 0f, 0.001f);
        hitImage.raycastTarget = true;

        GameObject dotObj = new GameObject("Corner_Dot");
        dotObj.transform.SetParent(handleObj.transform, false);
        RectTransform dotRect = dotObj.AddComponent<RectTransform>();
        dotRect.anchorMin = new Vector2(0.5f, 0.5f);
        dotRect.anchorMax = new Vector2(0.5f, 0.5f);
        dotRect.anchoredPosition = new Vector2(-4f, 4f);
        dotRect.sizeDelta = new Vector2(10f, 10f);

        cornerDragHandleDotImage = dotObj.AddComponent<Image>();
        cornerDragHandleDotImage.color = cornerReticleColor;
        cornerDragHandleDotImage.raycastTarget = false;

        ViewfinderDragHandle dragHandle = handleObj.AddComponent<ViewfinderDragHandle>();
        dragHandle.Initialize(this, DragHandleType.CornerBR);
    }

    public void OnHandlePointerDown(PointerEventData eventData, DragHandleType type)
    {
        initialTopEdge = windowCenterYNormalized + (windowHeightNormalized / 2f);

        switch (type)
        {
            case DragHandleType.Bottom:
                isDraggingBottom = true;
                if (bottomDragHandlePillImage != null) bottomDragHandlePillImage.color = handleHighlightColor;
                if (bottomDragHandlePillTransform != null) bottomDragHandlePillTransform.localScale = new Vector3(1.15f, 1.25f, 1f);
                break;
            case DragHandleType.Right:
                isDraggingRight = true;
                if (rightDragHandlePillImage != null) rightDragHandlePillImage.color = handleHighlightColor;
                if (rightDragHandlePillTransform != null) rightDragHandlePillTransform.localScale = new Vector3(1.25f, 1.15f, 1f);
                break;
            case DragHandleType.CornerBR:
                isDraggingCorner = true;
                if (cornerDragHandleDotImage != null) cornerDragHandleDotImage.color = handleHighlightColor;
                break;
        }
    }

    public void OnHandleDrag(PointerEventData eventData, DragHandleType type)
    {
        switch (type)
        {
            case DragHandleType.Bottom:
                ResizeVertical(eventData.position.y);
                break;

            case DragHandleType.Right:
                ResizeHorizontal(eventData.position.x);
                break;

            case DragHandleType.CornerBR:
                ResizeVertical(eventData.position.y);
                ResizeHorizontal(eventData.position.x);
                break;
        }

        UpdateLayout();
    }

    private void ResizeVertical(float pointerScreenY)
    {
        float pointerYNorm = Mathf.Clamp01(pointerScreenY / (float)Screen.height);
        float minBottom = Mathf.Max(minBottomDistanceNormalized, initialTopEdge - maxHeightNormalized);
        float maxBottom = initialTopEdge - minHeightNormalized;
        float newBottomNorm = Mathf.Clamp(pointerYNorm, minBottom, maxBottom);

        float newHeight = initialTopEdge - newBottomNorm;
        windowHeightNormalized = newHeight;
        windowCenterYNormalized = initialTopEdge - (newHeight / 2f);
    }

    private void ResizeHorizontal(float pointerScreenX)
    {
        float pointerXNorm = Mathf.Clamp01(pointerScreenX / (float)Screen.width);
        // Symmetrical expansion from horizontal center (0.5)
        float halfWidth = Mathf.Clamp(Mathf.Abs(pointerXNorm - 0.5f), minWidthNormalized * 0.5f, maxWidthNormalized * 0.5f);
        windowWidthNormalized = halfWidth * 2f;
    }

    public void OnHandlePointerUp(PointerEventData eventData, DragHandleType type)
    {
        switch (type)
        {
            case DragHandleType.Bottom:
                isDraggingBottom = false;
                if (bottomDragHandlePillImage != null) bottomDragHandlePillImage.color = currentDynamicColor;
                if (bottomDragHandlePillTransform != null) bottomDragHandlePillTransform.localScale = Vector3.one;
                break;
            case DragHandleType.Right:
                isDraggingRight = false;
                if (rightDragHandlePillImage != null) rightDragHandlePillImage.color = currentDynamicColor;
                if (rightDragHandlePillTransform != null) rightDragHandlePillTransform.localScale = Vector3.one;
                break;
            case DragHandleType.CornerBR:
                isDraggingCorner = false;
                if (cornerDragHandleDotImage != null) cornerDragHandleDotImage.color = currentDynamicColor;
                break;
        }
    }

    private void UpdateLayout()
    {
        float leftNorm = (1f - windowWidthNormalized) / 2f;
        float rightNorm = leftNorm;
        float bottomNorm = windowCenterYNormalized - (windowHeightNormalized / 2f);
        float topNorm = 1f - (windowCenterYNormalized + (windowHeightNormalized / 2f));

        if (topMaskRect != null)
        {
            topMaskRect.anchorMin = new Vector2(0f, 1f - topNorm);
            topMaskRect.anchorMax = new Vector2(1f, 1f);
            topMaskRect.sizeDelta = Vector2.zero;
            topMaskRect.anchoredPosition = Vector2.zero;
        }

        if (bottomMaskRect != null)
        {
            bottomMaskRect.anchorMin = new Vector2(0f, 0f);
            bottomMaskRect.anchorMax = new Vector2(1f, bottomNorm);
            bottomMaskRect.sizeDelta = Vector2.zero;
            bottomMaskRect.anchoredPosition = Vector2.zero;
        }

        if (leftMaskRect != null)
        {
            leftMaskRect.anchorMin = new Vector2(0f, bottomNorm);
            leftMaskRect.anchorMax = new Vector2(leftNorm, 1f - topNorm);
            leftMaskRect.sizeDelta = Vector2.zero;
            leftMaskRect.anchoredPosition = Vector2.zero;
        }

        if (rightMaskRect != null)
        {
            rightMaskRect.anchorMin = new Vector2(1f - rightNorm, bottomNorm);
            rightMaskRect.anchorMax = new Vector2(1f, 1f - topNorm);
            rightMaskRect.sizeDelta = Vector2.zero;
            rightMaskRect.anchoredPosition = Vector2.zero;
        }

        if (windowBoxTransform != null)
        {
            windowBoxTransform.anchorMin = new Vector2(leftNorm, bottomNorm);
            windowBoxTransform.anchorMax = new Vector2(1f - rightNorm, 1f - topNorm);
            windowBoxTransform.sizeDelta = Vector2.zero;
            windowBoxTransform.anchoredPosition = Vector2.zero;
        }

        if (hintRect != null)
        {
            hintRect.anchorMin = new Vector2(0.1f, 1f - topNorm);
            hintRect.anchorMax = new Vector2(0.9f, 1f - topNorm);
        }

        if (bottomDragHandleRect != null)
        {
            bottomDragHandleRect.anchorMin = new Vector2(0.5f, bottomNorm);
            bottomDragHandleRect.anchorMax = new Vector2(0.5f, bottomNorm);
            bottomDragHandleRect.anchoredPosition = new Vector2(0f, 0f);
        }

        if (rightDragHandleRect != null)
        {
            rightDragHandleRect.anchorMin = new Vector2(1f - rightNorm, windowCenterYNormalized);
            rightDragHandleRect.anchorMax = new Vector2(1f - rightNorm, windowCenterYNormalized);
            rightDragHandleRect.anchoredPosition = new Vector2(0f, 0f);
        }

        if (cornerDragHandleRect != null)
        {
            cornerDragHandleRect.anchorMin = new Vector2(1f - rightNorm, bottomNorm);
            cornerDragHandleRect.anchorMax = new Vector2(1f - rightNorm, bottomNorm);
            cornerDragHandleRect.anchoredPosition = new Vector2(0f, 0f);
        }
    }

    private RectTransform CreateMaskPanel(RectTransform parent, string name, Color color)
    {
        GameObject panelObj = new GameObject(name);
        panelObj.transform.SetParent(parent, false);
        RectTransform rt = panelObj.AddComponent<RectTransform>();
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;

        Image img = panelObj.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    private void CreateCornerReticle(RectTransform parent, string name, Vector2 anchor, Vector2 horizSize, Vector2 vertSize)
    {
        GameObject cornerObj = new GameObject("Corner_" + name);
        cornerObj.transform.SetParent(parent, false);
        RectTransform cornerRect = cornerObj.AddComponent<RectTransform>();
        cornerRect.anchorMin = anchor;
        cornerRect.anchorMax = anchor;
        cornerRect.anchoredPosition = Vector2.zero;
        cornerRect.sizeDelta = Vector2.zero;

        Vector2 pivot = anchor; // (0,1) for TL, (1,1) for TR, etc.

        // Horizontal line
        GameObject hLine = new GameObject("H_Line");
        hLine.transform.SetParent(cornerRect, false);
        RectTransform hRt = hLine.AddComponent<RectTransform>();
        hRt.pivot = pivot;
        hRt.sizeDelta = horizSize;
        hRt.anchoredPosition = Vector2.zero;
        Image hImg = hLine.AddComponent<Image>();
        hImg.color = cornerReticleColor;
        hImg.raycastTarget = false;
        reticleBracketImages.Add(hImg);

        // Vertical line
        GameObject vLine = new GameObject("V_Line");
        vLine.transform.SetParent(cornerRect, false);
        RectTransform vRt = vLine.AddComponent<RectTransform>();
        vRt.pivot = pivot;
        vRt.sizeDelta = vertSize;
        vRt.anchoredPosition = Vector2.zero;
        Image vImg = vLine.AddComponent<Image>();
        vImg.color = cornerReticleColor;
        vImg.raycastTarget = false;
        reticleBracketImages.Add(vImg);
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
        if (visualRoot != null)
        {
            visualRoot.SetActive(visible);
        }
    }
}

/// <summary>
/// Helper drag receiver attached to the viewfinder handles.
/// Forwards pointer events to ReadingWindowViewfinder for fluid 60fps resizing.
/// </summary>
public class ViewfinderDragHandle : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private ReadingWindowViewfinder viewfinder;
    private DragHandleType handleType;

    public void Initialize(ReadingWindowViewfinder parent, DragHandleType type)
    {
        viewfinder = parent;
        handleType = type;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (viewfinder != null)
            viewfinder.OnHandlePointerDown(eventData, handleType);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (viewfinder != null)
            viewfinder.OnHandleDrag(eventData, handleType);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (viewfinder != null)
            viewfinder.OnHandlePointerUp(eventData, handleType);
    }
}

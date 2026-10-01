using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Renders a stylized 2D "Reading Window" viewfinder overlay on the screen HUD.
/// Guides the user to frame a textbook paragraph and crops the camera image 
/// for high-resolution OCR and targeted AR tracking.
/// Features a touch-interactive bottom drag handle to resize the reading box vertically.
/// </summary>
public class ReadingWindowViewfinder : MonoBehaviour
{
    [Header("Viewfinder Dimensions (Normalized Screen 0.0 - 1.0)")]
    [Tooltip("Normalized horizontal size (0.0 to 1.0) of the reading box")]
    [Range(0.4f, 0.95f)]
    [SerializeField] private float windowWidthNormalized = 0.86f;

    [Tooltip("Normalized vertical size (0.0 to 1.0) of the reading box")]
    [Range(0.12f, 0.65f)]
    [SerializeField] private float windowHeightNormalized = 0.38f;

    [Tooltip("Normalized vertical center position (0.0 = bottom, 1.0 = top)")]
    [Range(0.2f, 0.8f)]
    [SerializeField] private float windowCenterYNormalized = 0.54f;

    [Header("Vertical Resizing Bounds (Normalized 0.0 - 1.0)")]
    [Tooltip("Minimum normalized height of the reading box (~2 lines)")]
    [SerializeField] private float minHeightNormalized = 0.12f;

    [Tooltip("Maximum normalized height of the reading box (~10-12 lines)")]
    [SerializeField] private float maxHeightNormalized = 0.60f;

    [Tooltip("Minimum distance from screen bottom (keeps box safely above scan button)")]
    [SerializeField] private float minBottomDistanceNormalized = 0.18f;

    [Header("Visual Styling")]
    [SerializeField] private Color cornerReticleColor = new Color(0.12f, 0.53f, 0.96f, 0.95f); // Neon Blue
    [SerializeField] private Color handleHighlightColor = new Color(0.40f, 0.85f, 1.0f, 1.0f); // Bright Cyan
    [SerializeField] private Color maskShadeColor = new Color(0f, 0f, 0f, 0.40f); // Darkened vignette
    [SerializeField] private float cornerThickness = 4f;
    [SerializeField] private float cornerLength = 32f;

    [Header("References")]
    [SerializeField] private RectTransform windowBoxTransform;
    [SerializeField] private GameObject visualRoot;

    // Internal references for real-time layout updates
    private RectTransform topMaskRect;
    private RectTransform bottomMaskRect;
    private RectTransform leftMaskRect;
    private RectTransform rightMaskRect;
    private RectTransform hintRect;
    private RectTransform dragHandleRect;
    private Image dragHandlePillImage;
    private Transform dragHandlePillTransform;

    private float initialTopEdge = 0.73f;
    private bool isDraggingHandle = false;

    private void Awake()
    {
        BuildViewfinderUIIfNeeded();
    }

    private void BuildViewfinderUIIfNeeded()
    {
        if (visualRoot != null && windowBoxTransform != null) return;

        // Check if canvas exists on this object or parent
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

        // Add 4 Corner Brackets
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

        Text hintText = hintObj.AddComponent<Text>();
        hintText.text = "📖 Align paragraph inside box";
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (hintText.font == null) hintText.font = Font.CreateDynamicFontFromOSFont("Arial", 16);
        hintText.fontSize = 17;
        hintText.alignment = TextAnchor.MiddleCenter;
        hintText.color = new Color(1f, 1f, 1f, 0.95f);
        hintText.raycastTarget = false;

        Outline outline = hintObj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
        outline.effectDistance = new Vector2(1f, -1f);

        // ── 4. Interactive Bottom Drag Handle ──
        BuildBottomDragHandle(rootRect);

        // Initial Layout
        UpdateLayout();
    }

    private void BuildBottomDragHandle(RectTransform parent)
    {
        GameObject handleObj = new GameObject("ReadingWindow_BottomHandle");
        handleObj.transform.SetParent(parent, false);
        dragHandleRect = handleObj.AddComponent<RectTransform>();
        dragHandleRect.sizeDelta = new Vector2(180f, 54f); // Generous touch hit-box

        // Invisible Raycast Target to catch touch/mouse drags easily
        Image hitImage = handleObj.AddComponent<Image>();
        hitImage.color = new Color(0f, 0f, 0f, 0.001f);
        hitImage.raycastTarget = true;

        // Visual Pill Bar
        GameObject pillObj = new GameObject("Handle_Pill");
        pillObj.transform.SetParent(handleObj.transform, false);
        RectTransform pillRect = pillObj.AddComponent<RectTransform>();
        pillRect.anchorMin = new Vector2(0.5f, 0.5f);
        pillRect.anchorMax = new Vector2(0.5f, 0.5f);
        pillRect.anchoredPosition = new Vector2(0f, 6f);
        pillRect.sizeDelta = new Vector2(64f, 6f);

        dragHandlePillImage = pillObj.AddComponent<Image>();
        dragHandlePillImage.color = cornerReticleColor;
        dragHandlePillImage.raycastTarget = false;
        dragHandlePillTransform = pillObj.transform;

        // Visual Drag Prompt Label
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

        // Attach pointer drag handler
        ViewfinderDragHandle dragHandle = handleObj.AddComponent<ViewfinderDragHandle>();
        dragHandle.Initialize(this);
    }

    public void OnHandlePointerDown(PointerEventData eventData)
    {
        isDraggingHandle = true;
        initialTopEdge = windowCenterYNormalized + (windowHeightNormalized / 2f);

        if (dragHandlePillImage != null)
            dragHandlePillImage.color = handleHighlightColor;

        if (dragHandlePillTransform != null)
            dragHandlePillTransform.localScale = new Vector3(1.15f, 1.25f, 1f);
    }

    public void OnHandleDrag(PointerEventData eventData)
    {
        if (!isDraggingHandle) return;

        float pointerYNorm = Mathf.Clamp01(eventData.position.y / (float)Screen.height);

        float minBottom = Mathf.Max(minBottomDistanceNormalized, initialTopEdge - maxHeightNormalized);
        float maxBottom = initialTopEdge - minHeightNormalized;
        float newBottomNorm = Mathf.Clamp(pointerYNorm, minBottom, maxBottom);

        float newHeight = initialTopEdge - newBottomNorm;
        windowHeightNormalized = newHeight;
        windowCenterYNormalized = initialTopEdge - (newHeight / 2f);

        UpdateLayout();
    }

    public void OnHandlePointerUp(PointerEventData eventData)
    {
        isDraggingHandle = false;

        if (dragHandlePillImage != null)
            dragHandlePillImage.color = cornerReticleColor;

        if (dragHandlePillTransform != null)
            dragHandlePillTransform.localScale = Vector3.one;
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

        if (dragHandleRect != null)
        {
            dragHandleRect.anchorMin = new Vector2(0.5f, bottomNorm);
            dragHandleRect.anchorMax = new Vector2(0.5f, bottomNorm);
            dragHandleRect.anchoredPosition = new Vector2(0f, 0f);
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
/// Helper drag receiver attached to the viewfinder bottom handle.
/// Forwards pointer events to ReadingWindowViewfinder for fluid 60fps vertical resizing.
/// </summary>
public class ViewfinderDragHandle : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private ReadingWindowViewfinder viewfinder;

    public void Initialize(ReadingWindowViewfinder parent)
    {
        viewfinder = parent;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (viewfinder != null)
            viewfinder.OnHandlePointerDown(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (viewfinder != null)
            viewfinder.OnHandleDrag(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (viewfinder != null)
            viewfinder.OnHandlePointerUp(eventData);
    }
}

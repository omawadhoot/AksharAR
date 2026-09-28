using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders a stylized 2D "Reading Window" viewfinder overlay on the screen HUD.
/// Guides the user to frame a single paragraph and crops the camera image 
/// for high-resolution OCR and targeted AR tracking.
/// </summary>
public class ReadingWindowViewfinder : MonoBehaviour
{
    [Header("Viewfinder Dimensions (Normalized Screen 0.0 - 1.0)")]
    [Tooltip("Normalized horizontal size (0.0 to 1.0) of the reading box")]
    [Range(0.4f, 0.95f)]
    [SerializeField] private float windowWidthNormalized = 0.86f;

    [Tooltip("Normalized vertical size (0.0 to 1.0) of the reading box")]
    [Range(0.2f, 0.7f)]
    [SerializeField] private float windowHeightNormalized = 0.38f;

    [Tooltip("Normalized vertical center position (0.0 = bottom, 1.0 = top)")]
    [Range(0.3f, 0.7f)]
    [SerializeField] private float windowCenterYNormalized = 0.54f;

    [Header("Visual Styling")]
    [SerializeField] private Color cornerReticleColor = new Color(0.12f, 0.53f, 0.96f, 0.95f); // Neon Blue
    [SerializeField] private Color maskShadeColor = new Color(0f, 0f, 0f, 0.40f); // Darkened vignette
    [SerializeField] private float cornerThickness = 4f;
    [SerializeField] private float cornerLength = 32f;

    [Header("References")]
    [SerializeField] private RectTransform windowBoxTransform;
    [SerializeField] private GameObject visualRoot;

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
        float leftNorm = (1f - windowWidthNormalized) / 2f;
        float rightNorm = leftNorm;
        float bottomNorm = windowCenterYNormalized - (windowHeightNormalized / 2f);
        float topNorm = 1f - (windowCenterYNormalized + (windowHeightNormalized / 2f));

        // Top Mask
        CreateMaskPanel(rootRect, "TopMask", new Vector2(0f, 1f - topNorm), new Vector2(1f, 1f), maskShadeColor);
        // Bottom Mask (above scan button)
        CreateMaskPanel(rootRect, "BottomMask", new Vector2(0f, 0f), new Vector2(1f, bottomNorm), maskShadeColor);
        // Left Mask
        CreateMaskPanel(rootRect, "LeftMask", new Vector2(0f, bottomNorm), new Vector2(leftNorm, 1f - topNorm), maskShadeColor);
        // Right Mask
        CreateMaskPanel(rootRect, "RightMask", new Vector2(1f - rightNorm, bottomNorm), new Vector2(1f, 1f - topNorm), maskShadeColor);

        // ── 2. Reading Window Center Box & Corners ──
        GameObject boxObj = new GameObject("ReadingWindow_CenterBox");
        boxObj.transform.SetParent(rootRect, false);
        windowBoxTransform = boxObj.AddComponent<RectTransform>();
        windowBoxTransform.anchorMin = new Vector2(leftNorm, bottomNorm);
        windowBoxTransform.anchorMax = new Vector2(1f - rightNorm, 1f - topNorm);
        windowBoxTransform.sizeDelta = Vector2.zero;
        windowBoxTransform.anchoredPosition = Vector2.zero;

        // Add 4 Corner Brackets
        CreateCornerReticle(windowBoxTransform, "TL", new Vector2(0f, 1f), new Vector2(cornerLength, cornerThickness), new Vector2(cornerThickness, cornerLength));
        CreateCornerReticle(windowBoxTransform, "TR", new Vector2(1f, 1f), new Vector2(cornerLength, cornerThickness), new Vector2(cornerThickness, cornerLength));
        CreateCornerReticle(windowBoxTransform, "BL", new Vector2(0f, 0f), new Vector2(cornerLength, cornerThickness), new Vector2(cornerThickness, cornerLength));
        CreateCornerReticle(windowBoxTransform, "BR", new Vector2(1f, 0f), new Vector2(cornerLength, cornerThickness), new Vector2(cornerThickness, cornerLength));

        // ── 3. Helper Prompt Text ──
        GameObject hintObj = new GameObject("ReadingWindow_HintText");
        hintObj.transform.SetParent(rootRect, false);
        RectTransform hintRect = hintObj.AddComponent<RectTransform>();
        hintRect.anchorMin = new Vector2(0.1f, 1f - topNorm);
        hintRect.anchorMax = new Vector2(0.9f, 1f - topNorm);
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

        // Add subtle shadow to text for readability
        Outline outline = hintObj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.75f);
        outline.effectDistance = new Vector2(1f, -1f);
    }

    private void CreateMaskPanel(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        GameObject panelObj = new GameObject(name);
        panelObj.transform.SetParent(parent, false);
        RectTransform rt = panelObj.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;

        Image img = panelObj.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
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

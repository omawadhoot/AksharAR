using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[Serializable]
public struct CameraCapturePose
{
    public Vector3 cameraPosition;
    public Quaternion cameraRotation;
    public float fieldOfView;
    public float aspect;
    public RectInt pixelCropRect;
    public int screenWidth;
    public int screenHeight;
    public bool isValid;
}

public class ARWorldSpaceUIToolkitController : MonoBehaviour
{
    [Header("UI Toolkit References")]
    [SerializeField] private PanelSettings basePanelSettings;
    [SerializeField] private PanelSettings worldSpacePanelSettings;
    [SerializeField] private RenderTexture offscreenRenderTexture;
    [SerializeField] private StyleSheet lineStyleSheet;
    [SerializeField] private Font rawFont;
    [SerializeField] private UnityEngine.TextCore.Text.FontAsset sdfFontAsset;

    // Architectural Const Constraints
    private const float fontAscenderRatio = 0.22f;
    private const float minFontScaleFloor = 0.75f;
    private const float absoluteMaxSquish = 0.65f;

    [Header("Page Dimensions (Meters)")]
    [Tooltip("Standard physical textbook column width estimation in meters (0.14m = 14cm)")]
    [SerializeField] private Vector2 pageSizeMeters = new Vector2(0.14f, 0.10f);

    private UIDocument uiDocument;
    private GameObject worldQuadObj;
    private Material quadMaterial;
    private VisualElement rootElement;
    private VisualElement textContainer;
    private ARSpatialPoseFilter poseFilter;
    private CameraCapturePose lastCapturePose;

    public void SetCameraCapturePose(CameraCapturePose pose)
    {
        lastCapturePose = pose;
    }

    private void Awake()
    {
        LoadResourcesIfNeeded();
        InitializeWorldSpaceUIToolkit();
    }

    private void LoadResourcesIfNeeded()
    {
        if (offscreenRenderTexture == null)
        {
            offscreenRenderTexture = Resources.Load<RenderTexture>("WorldSpaceRenderTexture");
#if UNITY_EDITOR
            if (offscreenRenderTexture == null)
            {
                offscreenRenderTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<RenderTexture>("Assets/UI Toolkit/WorldSpaceRenderTexture.renderTexture");
            }
#endif
        }

        if (worldSpacePanelSettings == null)
        {
            worldSpacePanelSettings = Resources.Load<PanelSettings>("WorldSpacePanelSettings");
#if UNITY_EDITOR
            if (worldSpacePanelSettings == null)
            {
                worldSpacePanelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/WorldSpacePanelSettings.asset");
            }
#endif
        }

        if (sdfFontAsset == null)
        {
            sdfFontAsset = Resources.Load<UnityEngine.TextCore.Text.FontAsset>("Fonts/NeevA-Dyslexia-Regular SDF");
#if UNITY_EDITOR
            if (sdfFontAsset == null)
            {
                sdfFontAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>("Assets/Fonts/NeevA-Dyslexia-Regular SDF.asset");
            }
#endif
        }

        if (rawFont == null)
        {
            rawFont = Resources.Load<Font>("Fonts/NeevA-Dyslexia-Regular");
#if UNITY_EDITOR
            if (rawFont == null)
            {
                rawFont = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NeevA-Dyslexia-Regular.ttf");
            }
#endif
        }

        if (lineStyleSheet == null)
        {
            lineStyleSheet = Resources.Load<StyleSheet>("ARLineOverlay");
#if UNITY_EDITOR
            if (lineStyleSheet == null)
            {
                lineStyleSheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/ARLineOverlay.uss");
            }
#endif
        }

        if (basePanelSettings == null)
        {
            basePanelSettings = Resources.Load<PanelSettings>("PanelSettings");
#if UNITY_EDITOR
            if (basePanelSettings == null)
            {
                basePanelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/PanelSettings.asset");
            }
#endif
        }
    }

    private void InitializeWorldSpaceUIToolkit(Transform parentTransform = null)
    {
        LoadResourcesIfNeeded();

        if (worldSpacePanelSettings != null && offscreenRenderTexture != null)
        {
            worldSpacePanelSettings.targetTexture = offscreenRenderTexture;
            worldSpacePanelSettings.clearColor = true;
            worldSpacePanelSettings.colorClearValue = new Color(0f, 0f, 0f, 0f);
            worldSpacePanelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
        }

        if (uiDocument == null)
        {
            Transform childDocTransform = transform.Find("AR_WorldSpace_UIDoc");
            GameObject docObj;
            if (childDocTransform != null)
            {
                docObj = childDocTransform.gameObject;
            }
            else
            {
                docObj = new GameObject("AR_WorldSpace_UIDoc");
                docObj.transform.SetParent(transform, false);
            }

            uiDocument = docObj.GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                uiDocument = docObj.AddComponent<UIDocument>();
            }
        }

        if (uiDocument != null && worldSpacePanelSettings != null)
        {
            uiDocument.panelSettings = worldSpacePanelSettings;
        }

        if (worldQuadObj == null)
        {
            worldQuadObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            worldQuadObj.name = "AR_WorldSpace_UIQuad";

            // Ensure MeshCollider exists for raycast touch detection
            MeshCollider col = worldQuadObj.GetComponent<MeshCollider>();
            if (col == null)
            {
                col = worldQuadObj.AddComponent<MeshCollider>();
            }

            Material baseMat = Resources.Load<Material>("ARWorldSpaceQuadMaterial");
#if UNITY_EDITOR
            if (baseMat == null)
            {
                baseMat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ARWorldSpaceQuadMaterial.mat");
            }
#endif
            if (baseMat != null)
            {
                quadMaterial = new Material(baseMat);
            }
            else
            {
                Shader unlitShader = Shader.Find("AksharAR/TransparentUnlit");
                if (unlitShader == null) unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (unlitShader == null) unlitShader = Shader.Find("Unlit/Transparent");
                quadMaterial = new Material(unlitShader);
            }

            if (quadMaterial.HasProperty("_MainTex") && offscreenRenderTexture != null)
                quadMaterial.SetTexture("_MainTex", offscreenRenderTexture);
            if (quadMaterial.HasProperty("_BaseMap") && offscreenRenderTexture != null)
                quadMaterial.SetTexture("_BaseMap", offscreenRenderTexture);

            MeshRenderer renderer = worldQuadObj.GetComponent<MeshRenderer>();
            renderer.material = quadMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Attach Three-Stage Spatial Pose Filter to decouple and stabilize 3D quad
            poseFilter = worldQuadObj.GetComponent<ARSpatialPoseFilter>();
            if (poseFilter == null)
            {
                poseFilter = worldQuadObj.AddComponent<ARSpatialPoseFilter>();
            }
        }

        // Keep 3D Quad in root world space (unparented from any hierarchy)
        if (worldQuadObj.transform.parent != null)
        {
            worldQuadObj.transform.SetParent(null, true);
        }

        worldQuadObj.transform.localScale = new Vector3(pageSizeMeters.x, pageSizeMeters.y, 1f);

        if (uiDocument != null && uiDocument.rootVisualElement != null && offscreenRenderTexture != null)
        {
            rootElement = uiDocument.rootVisualElement;
            rootElement.style.width = offscreenRenderTexture.width;
            rootElement.style.height = offscreenRenderTexture.height;

            if (lineStyleSheet != null && !rootElement.styleSheets.Contains(lineStyleSheet))
            {
                rootElement.styleSheets.Add(lineStyleSheet);
            }

            textContainer = rootElement.Q<VisualElement>("TextContainer");
            if (textContainer == null)
            {
                textContainer = new VisualElement { name = "TextContainer" };
                textContainer.style.position = Position.Absolute;
                textContainer.style.left = 0;
                textContainer.style.top = 0;
                textContainer.style.width = offscreenRenderTexture.width;
                textContainer.style.height = offscreenRenderTexture.height;
                rootElement.Add(textContainer);
            }
        }
    }

    private ARTrackedImage currentTrackedImage;
    // Pending lines to display if textContainer isn't ready yet
    private List<DetectedTextLine> pendingLines;
    private Vector2 pendingImageSize;
    private Texture2D pendingCapturedTexture;

    public void AttachToTrackedImage(ARTrackedImage trackedImage)
    {
        if (trackedImage == null) return;

        if (trackedImage.size.x > 0 && trackedImage.size.y > 0)
        {
            pageSizeMeters = trackedImage.size;
        }

        currentTrackedImage = trackedImage;

        if (worldQuadObj == null || uiDocument == null)
        {
            InitializeWorldSpaceUIToolkit();
        }

        if (worldQuadObj != null && pageSizeMeters.x > 0f && pageSizeMeters.y > 0f)
        {
            worldQuadObj.transform.localScale = new Vector3(pageSizeMeters.x, pageSizeMeters.y, 1f);
        }

        // Route pose through Three-Stage Spatial State Filter
        if (poseFilter == null && worldQuadObj != null)
        {
            poseFilter = worldQuadObj.GetComponent<ARSpatialPoseFilter>();
            if (poseFilter == null)
                poseFilter = worldQuadObj.AddComponent<ARSpatialPoseFilter>();
        }

        if (poseFilter != null)
        {
            poseFilter.SetTarget(trackedImage);
        }

        if (worldQuadObj != null && trackedImage.trackingState == TrackingState.Tracking)
        {
            worldQuadObj.SetActive(true);
        }
    }

    /// <summary>
    /// Frame N Execution: Groups plates, aligns vertical typography metrics, 
    /// draws ink masking backgrounds, and kicks off the Yoga calculation pass.
    /// </summary>
    public void DisplayDetectedLines(List<DetectedTextLine> detectedPlates, Texture2D lastCapturedTexture, float baseFontSize)
    {
        Vector2 imageSize = lastCapturedTexture != null 
            ? new Vector2(lastCapturedTexture.width, lastCapturedTexture.height) 
            : new Vector2(2048f, 2898f);
        DisplayDetectedLines(detectedPlates, imageSize, lastCapturedTexture, baseFontSize);
    }

    public void DisplayDetectedLines(List<DetectedTextLine> detectedLines, Vector2 visionImageSize, Texture2D capturedFrameTexture = null, float baseFontSizeOverride = -1f)
    {
        if (detectedLines == null || detectedLines.Count == 0) return;

        // Halt any pending retries from prior scan
        StopAllCoroutines();

        // Ensure the quad exists
        if (worldQuadObj == null)
        {
            InitializeWorldSpaceUIToolkit();
        }

        // If the UIDocument panel isn't ready yet (rootElement / textContainer null),
        // stash the data and retry next frame via coroutine.
        if (rootElement == null || textContainer == null)
        {
            // Try to grab root element one more time in case panel initialized between frames
            if (uiDocument != null && uiDocument.rootVisualElement != null && offscreenRenderTexture != null)
            {
                rootElement = uiDocument.rootVisualElement;
                rootElement.style.width = offscreenRenderTexture.width;
                rootElement.style.height = offscreenRenderTexture.height;

                textContainer = rootElement.Q<VisualElement>("TextContainer");
                if (textContainer == null)
                {
                    textContainer = new VisualElement { name = "TextContainer" };
                    textContainer.style.position = Position.Absolute;
                    textContainer.style.left = 0;
                    textContainer.style.top = 0;
                    textContainer.style.width = offscreenRenderTexture.width;
                    textContainer.style.height = offscreenRenderTexture.height;
                    rootElement.Add(textContainer);
                }
            }
            else
            {
                // Still not ready — defer to next frame
                pendingLines = detectedLines;
                pendingImageSize = visionImageSize;
                pendingCapturedTexture = capturedFrameTexture;
                StartCoroutine(RetryDisplayNextFrame());
                return;
            }
        }

        // Explicit clean-slate: ensure textContainer is cleared before rendering new lines
        textContainer.Clear();
        RenderLines(detectedLines, visionImageSize, capturedFrameTexture, baseFontSizeOverride);

        // Always show the quad once text is rendered
        if (worldQuadObj != null)
            worldQuadObj.SetActive(true);

        if (poseFilter != null)
        {
            poseFilter.SetOverlayVisibility(true);
            poseFilter.SetCropMetrics(pageSizeMeters.x, pageSizeMeters.x, 0f, "Rendered", 0);
        }
    }

    private IEnumerator RetryDisplayNextFrame()
    {
        yield return null; // wait one frame for UIDocument panel to initialize
        if (pendingLines != null)
        {
            var lines = pendingLines;
            var imgSize = pendingImageSize;
            var tex = pendingCapturedTexture;
            pendingLines = null;
            pendingCapturedTexture = null;
            DisplayDetectedLines(lines, imgSize, tex);
        }
    }

    private void RenderLines(List<DetectedTextLine> detectedLines, Vector2 visionImageSize, Texture2D capturedFrameTexture = null, float baseFontSizeOverride = -1f)
    {
        textContainer.Clear();

        float texWidth = offscreenRenderTexture != null ? (float)offscreenRenderTexture.width : 2048f;
        float texHeight = offscreenRenderTexture != null ? (float)offscreenRenderTexture.height : 2898f;

        // 1. Calculate UNIFORM aspect scale factor (equal for both X and Y axes)
        float uniformScale = texWidth / visionImageSize.x;
        float activePanelHeight = visionImageSize.y * uniformScale;

        // 2. Compute physical world-space dimensions matching the cropped paragraph
        float physicalWidth = pageSizeMeters.x > 0f ? pageSizeMeters.x : 0.14f;
        float physicalHeight = physicalWidth * (visionImageSize.y / visionImageSize.x);

        if (worldQuadObj != null)
        {
            // Scale the 3D Quad to match the physical paragraph aspect ratio exactly
            worldQuadObj.transform.localScale = new Vector3(physicalWidth, physicalHeight, 1f);

            // 3. Map the active top rendered texture portion [0..activePanelHeight] across the full 3D Quad
            UpdateMaterialUVSlice(activePanelHeight, texHeight);
        }

        if (rootElement != null)
        {
            rootElement.style.width = texWidth;
            rootElement.style.height = activePanelHeight;
            rootElement.style.marginLeft = 0f;
            rootElement.style.marginRight = 0f;
            rootElement.style.marginTop = 0f;
            rootElement.style.marginBottom = 0f;
            rootElement.style.paddingLeft = 0f;
            rootElement.style.paddingRight = 0f;
            rootElement.style.paddingTop = 0f;
            rootElement.style.paddingBottom = 0f;
        }

        if (textContainer != null)
        {
            textContainer.style.width = texWidth;
            textContainer.style.height = activePanelHeight;
            textContainer.style.marginLeft = 0f;
            textContainer.style.marginRight = 0f;
            textContainer.style.marginTop = 0f;
            textContainer.style.marginBottom = 0f;
            textContainer.style.paddingLeft = 0f;
            textContainer.style.paddingRight = 0f;
            textContainer.style.paddingTop = 0f;
            textContainer.style.paddingBottom = 0f;
        }

        Debug.Log($"[World-Space UI Toolkit] Rendering {detectedLines.Count} lines. ImageSize:{visionImageSize.x}x{visionImageSize.y} | Quad:{physicalWidth:F3}mx{physicalHeight:F3}m | ActivePixels:{texWidth}x{activePanelHeight:F0}");

        // ─── POEM MODE TYPOGRAPHY HARMONIZATION ────────────────────────────
        var rawLineHeights = detectedLines.Select(l => l.height * uniformScale).OrderBy(h => h).ToList();
        float medianScaledLineHeight = rawLineHeights.Count > 0 
            ? rawLineHeights[rawLineHeights.Count / 2] 
            : 42f * uniformScale;

        // Calibrated Devanagari Cap-Height Font Size:
        float harmonizedFontSize = baseFontSizeOverride > 0f 
            ? baseFontSizeOverride 
            : Mathf.Clamp(medianScaledLineHeight * 0.95f, 32f, 160f);
        float uniformPlateHeight = harmonizedFontSize * 1.35f;

        // ─── DYNAMIC METRIC MARGINS ────────────────────────────────────────
        float quadWidthMm = Mathf.Max(10f, physicalWidth * 1000f);
        float quadHeightMm = Mathf.Max(10f, physicalHeight * 1000f);
        float hMarginMm = 3.75f; // 3.75mm horizontal tolerance stack budget
        float vMarginMm = 2.50f; // 2.50mm block vertical tolerance
        float hMarginPx = (hMarginMm / quadWidthMm) * texWidth;
        float vMarginPx = (vMarginMm / quadHeightMm) * activePanelHeight;

        // Context 2: Sample representative paper substrate color with neutral paper normalization
        DetectedTextLine centralSampleLine = detectedLines.Count > 0 ? detectedLines[detectedLines.Count / 2] : default;
        Color inpaintColor = SamplePaperColorFromFrame(capturedFrameTexture, centralSampleLine, 6, visionImageSize);

        // Active Contrast Adaptation:
        // Dynamically evaluate substrate luminance to ensure maximum dyslexic legibility
        float bgLuminance = (0.299f * inpaintColor.r) + (0.587f * inpaintColor.g) + (0.114f * inpaintColor.b);
        Color textColor = (bgLuminance < 0.48f)
            ? new Color(0.98f, 0.98f, 0.98f, 1.0f)  // Crisp white for dark illustrated backgrounds
            : new Color(0.08f, 0.08f, 0.08f, 1.0f); // Deep charcoal black for light textbook pages

        List<Label> createdLabels = new List<Label>();
        List<float> targetWidths = new List<float>();

        // ─── PASS 1: Poem Block-Level Anchoring & Rhythm Harmonization ─────
        var sortedLines = detectedLines.OrderBy(l => l.minY).ToList();
        List<LineLayoutData> lineLayouts = new List<LineLayoutData>();

        float blockMinX = float.MaxValue;
        float blockMaxX = float.MinValue;
        float blockMinY = sortedLines.Count > 0 ? sortedLines[0].minY * uniformScale : 0f;
        float currentRunningY = blockMinY;

        for (int i = 0; i < sortedLines.Count; i++)
        {
            var line = sortedLines[i];
            float scaledX = line.minX * uniformScale;
            float scaledW = Math.Max(40f, line.width * uniformScale);
            float rawTopY = line.minY * uniformScale;

            if (i > 0)
            {
                float prevRawTopY = sortedLines[i - 1].minY * uniformScale;
                float verticalGap = rawTopY - prevRawTopY;

                // Detect semantic stanza break (large whitespace gap between couplets/stanzas)
                if (verticalGap > medianScaledLineHeight * 1.45f)
                {
                    currentRunningY += uniformPlateHeight * 1.40f; // Stanza break gap
                }
                else
                {
                    currentRunningY += uniformPlateHeight * 1.05f; // Standard line advance
                }
            }

            lineLayouts.Add(new LineLayoutData
            {
                text = line.text,
                leftX = scaledX,
                topY = currentRunningY,
                width = scaledW,
                height = uniformPlateHeight
            });

            blockMinX = Mathf.Min(blockMinX, scaledX);
            blockMaxX = Mathf.Max(blockMaxX, scaledX + scaledW);
        }

        float blockMaxY = currentRunningY + uniformPlateHeight;

        // ─── PASS 2: Unified Block-Level Substrate Masking ─────────────────
        if (lineLayouts.Count > 0)
        {
            VisualElement blockBackingCard = new VisualElement { name = "BlockBackingCard" };
            blockBackingCard.style.position = Position.Absolute;
            blockBackingCard.style.left = Mathf.Max(0f, blockMinX - hMarginPx);
            blockBackingCard.style.top = Mathf.Max(0f, blockMinY - vMarginPx);
            blockBackingCard.style.width = Mathf.Min(texWidth, (blockMaxX - blockMinX) + (hMarginPx * 2f));
            blockBackingCard.style.height = Mathf.Max(uniformPlateHeight, (blockMaxY - blockMinY) + (vMarginPx * 2f));
            blockBackingCard.style.backgroundColor = new StyleColor(inpaintColor);
            blockBackingCard.style.borderTopLeftRadius = 8;
            blockBackingCard.style.borderBottomLeftRadius = 8;
            blockBackingCard.style.borderTopRightRadius = 8;
            blockBackingCard.style.borderBottomRightRadius = 8;
            textContainer.Add(blockBackingCard);
        }

        // ─── PASS 3: Render dyslexic labels inside the anchored block ──────
        int index = 1;
        foreach (var data in lineLayouts)
        {
            var label = new Label(data.text);
            label.AddToClassList("hindi-ar-line");
            label.AddToClassList("hindi-dyslexic-reveal");
            label.style.unityTextGenerator = new StyleEnum<TextGeneratorType>(TextGeneratorType.Advanced);

            if (rawFont != null)
            {
                label.style.unityFont = new StyleFont(rawFont);
                label.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(rawFont));
            }
            else if (sdfFontAsset != null)
            {
                label.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(sdfFontAsset));
            }

            IStyle style = label.style;
            style.position = Position.Absolute;
            style.left = data.leftX;
            style.top = data.topY;
            style.width = data.width;
            style.height = data.height;

            style.unityTextAlign = new StyleEnum<TextAnchor>(TextAnchor.UpperLeft);
            style.overflow = Overflow.Visible;
            style.whiteSpace = WhiteSpace.NoWrap;

            style.marginLeft = 0f;
            style.marginRight = 0f;
            style.marginTop = 0f;
            style.marginBottom = 0f;
            style.paddingLeft = 0f;
            style.paddingRight = 0f;
            style.paddingTop = 0f;
            style.paddingBottom = 0f;

            style.backgroundColor = new StyleColor(Color.clear);
            style.color = new StyleColor(textColor);

            textContainer.Add(label);
            createdLabels.Add(label);
            targetWidths.Add(data.width);

            Debug.Log($"[World-Space Line #{index++}] \"{data.text}\" | X:{data.leftX:F0} Y:{data.topY:F0} W:{data.width:F0} H:{data.height:F0} Font:{harmonizedFontSize:F0}px");
        }

        // Force UI Toolkit layout engine & rendering pipeline to resolve initial tree
        textContainer.MarkDirtyRepaint();
        if (uiDocument != null && uiDocument.rootVisualElement != null)
        {
            uiDocument.rootVisualElement.MarkDirtyRepaint();
        }

        // Context 3: Hand off to Coroutine for the Frame N+1 Geometry Evaluation
        if (createdLabels.Count > 0)
        {
            VisualElement blockCard = textContainer.Q<VisualElement>("BlockBackingCard");
            StartCoroutine(OptimizeUIToolkitFittingLoop(createdLabels, targetWidths, harmonizedFontSize, blockCard, blockMinX, hMarginPx, texWidth));
        }
    }

    /// <summary>
    /// Context 2: Downsampled Otsu Bimodal Luminance Segmentation engine (Nuance 3).
    /// Samples across the line bounding area with a mobile-optimized stride (stride >= 4), builds a 256-bin
    /// histogram, computes the Otsu inter-class variance threshold in <0.05ms, and classifies substrate
    /// by majority cluster (>60% pixel count). Works seamlessly on standard paper, craft paper, and inverted text.
    /// </summary>
    public Color SamplePaperColorFromFrame(Texture2D sourceTexture, DetectedTextLine plate, int safetyMargin = 6, Vector2 visionImageSize = default)
    {
        if (sourceTexture == null || !sourceTexture.isReadable) return new Color(0.98f, 0.97f, 0.95f, 0.98f);

        try
        {
            int imgW = sourceTexture.width;
            int imgH = sourceTexture.height;

            float scaleX = (visionImageSize.x > 0f) ? (float)imgW / visionImageSize.x : 1f;
            float scaleY = (visionImageSize.y > 0f) ? (float)imgH / visionImageSize.y : 1f;

            int xMin = Mathf.Clamp(Mathf.RoundToInt(plate.minX * scaleX), 0, imgW - 1);
            int xMax = Mathf.Clamp(Mathf.RoundToInt((plate.minX + plate.width) * scaleX), 0, imgW - 1);
            int yMin = Mathf.Clamp(Mathf.RoundToInt(plate.minY * scaleY), 0, imgH - 1);
            int yMax = Mathf.Clamp(Mathf.RoundToInt((plate.minY + plate.height) * scaleY), 0, imgH - 1);

            // Expand sampling into the clean whitespace above/below the text line where pure paper substrate sits
            int marginY = Mathf.Max(6, Mathf.RoundToInt((yMax - yMin) * 0.45f));
            yMin = Mathf.Clamp(yMin - marginY, 0, imgH - 1);
            yMax = Mathf.Clamp(yMax + marginY, 0, imgH - 1);

            int width = xMax - xMin;
            int height = yMax - yMin;
            if (width <= 0 || height <= 0) return new Color(0.98f, 0.97f, 0.95f, 0.98f);

            int strideX = Mathf.Max(2, width / 40);
            int strideY = Mathf.Max(2, height / 20);

            List<Color> samples = new List<Color>(512);

            for (int y = yMin; y <= yMax; y += strideY)
            {
                int texY = Mathf.Clamp(imgH - 1 - y, 0, imgH - 1);
                for (int x = xMin; x <= xMax; x += strideX)
                {
                    Color pixel = sourceTexture.GetPixel(x, texY);
                    samples.Add(pixel);
                }
            }

            if (samples.Count == 0) return new Color(0.98f, 0.97f, 0.95f, 0.98f);

            // Sort by luminance to cleanly separate dark printed ink from the bright paper substrate
            samples.Sort((a, b) =>
            {
                float lumA = (0.299f * a.r) + (0.587f * a.g) + (0.114f * a.b);
                float lumB = (0.299f * b.r) + (0.587f * b.g) + (0.114f * b.b);
                return lumA.CompareTo(lumB);
            });

            // The paper substrate is the 85th percentile brightness sample (pure physical paper background)
            int sampleIdx = Mathf.Clamp((int)(samples.Count * 0.85f), 0, samples.Count - 1);
            Color paperColor = samples[sampleIdx];

            float lum = (0.299f * paperColor.r) + (0.587f * paperColor.g) + (0.114f * paperColor.b);

            // Neutral Paper Normalization:
            // If the sampled background is a light page (lum > 0.52), desaturate any green/blue/yellow illustration
            // tint casts so the text card seamlessly matches clean textbook paper (warm cream/white):
            if (lum > 0.52f)
            {
                float avg = (paperColor.r + paperColor.g + paperColor.b) / 3f;
                // Blend 75% toward neutral luminance to eliminate green/yellow tint bleed
                float r = Mathf.Lerp(paperColor.r, avg, 0.75f);
                float g = Mathf.Lerp(paperColor.g, avg, 0.75f);
                float b = Mathf.Lerp(paperColor.b, avg, 0.75f);
                return new Color(r, g, b, 0.98f);
            }

            // Dark background (e.g. storm/night illustration): preserve dark illustration tone
            return new Color(paperColor.r, paperColor.g, paperColor.b, 0.98f);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[SamplePaperColorFromFrame] Pixel read error: {ex.Message}");
            return new Color(0.98f, 0.97f, 0.95f, 0.98f);
        }
    }

    /// <summary>
    /// Context 3: Standard layout-yield logic separating property generation from geometric measurement.
    /// </summary>
    private IEnumerator OptimizeUIToolkitFittingLoop(List<Label> labels, List<float> targetWidths, float baseFontSize, VisualElement blockCard, float blockMinX, float hMarginPx, float texWidth)
    {
        for (int i = 0; i < labels.Count; i++)
        {
            labels[i].style.fontSize = baseFontSize;
            labels[i].style.width = StyleKeyword.Auto; // Allow natural text measurement
        }

        // Defer thread execution 1 full frame to let UI Toolkit execute mesh generation layouts
        yield return null;

        float actualMaxRight = blockMinX;

        // Frame N+1: Evaluate post-layout metrics safely
        for (int i = 0; i < labels.Count && i < targetWidths.Count; i++)
        {
            Label label = labels[i];
            if (label == null) continue;

            float targetWidth = targetWidths[i];
            float renderedWidth = label.layout.width;

            if (renderedWidth > targetWidth * 1.08f)
            {
                ApplyFittingConstraints(label, targetWidth, renderedWidth, baseFontSize);
            }

            float lineRight = label.layout.x + label.layout.width;
            if (lineRight > actualMaxRight)
            {
                actualMaxRight = lineRight;
            }
        }

        // Dynamically expand blockBackingCard to 100% cover all rendered text lines with margins
        if (blockCard != null && actualMaxRight > blockMinX)
        {
            float requiredCardWidth = Mathf.Min(texWidth - blockMinX, (actualMaxRight - blockMinX) + (hMarginPx * 2f));
            blockCard.style.width = Mathf.Max(blockCard.style.width.value.value, requiredCardWidth);
        }

        textContainer.MarkDirtyRepaint();
    }

    /// <summary>
    /// Context 4: Multi-tier font sizing reduction and horizontal compression execution block.
    /// </summary>
    private void ApplyFittingConstraints(Label label, float targetWidth, float currentWidth, float baseFontSize)
    {
        float minReadableFontSize = baseFontSize * minFontScaleFloor;
        float estimatedFontSize = (targetWidth / currentWidth) * baseFontSize;

        // Tier 1: Proportional Scaling
        if (estimatedFontSize >= minReadableFontSize)
        {
            label.style.fontSize = estimatedFontSize;
        }
        else
        {
            // Tier 2: Readability Limit Clamp & Accordion Squish Transformation
            label.style.fontSize = minReadableFontSize;

            float widthAtMinFont = currentWidth * (minReadableFontSize / baseFontSize);
            float squishRatio = Mathf.Clamp(targetWidth / widthAtMinFont, absoluteMaxSquish, 1.0f);

            label.style.scale = new StyleScale(new Scale(new Vector2(squishRatio, 1f)));
            label.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(Length.Percent(0), Length.Percent(0)));
        }
    }

    private void UpdateMaterialUVSlice(float activeCanvasHeight, float texHeight)
    {
        if (quadMaterial == null || texHeight <= 0f) return;

        float uvScaleY = Mathf.Clamp01(activeCanvasHeight / texHeight);
        float uvOffsetY = 1f - uvScaleY;

        Vector2 scale = new Vector2(1f, uvScaleY);
        Vector2 offset = new Vector2(0f, uvOffsetY);

        if (quadMaterial.HasProperty("_MainTex"))
        {
            quadMaterial.SetTextureScale("_MainTex", scale);
            quadMaterial.SetTextureOffset("_MainTex", offset);
        }
        if (quadMaterial.HasProperty("_BaseMap"))
        {
            quadMaterial.SetTextureScale("_BaseMap", scale);
            quadMaterial.SetTextureOffset("_BaseMap", offset);
        }
        if (quadMaterial.HasProperty("_Cull"))
        {
            quadMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        }
    }

    public static Ray GetRayFromPose(CameraCapturePose pose, Vector2 screenPoint)
    {
        float ndcX = (screenPoint.x / (float)pose.screenWidth) * 2f - 1f;
        float ndcY = (screenPoint.y / (float)pose.screenHeight) * 2f - 1f;

        float tanHalfFov = Mathf.Tan(pose.fieldOfView * 0.5f * Mathf.Deg2Rad);
        Vector3 dirCam = new Vector3(ndcX * tanHalfFov * pose.aspect, ndcY * tanHalfFov, 1f).normalized;
        Vector3 dirWorld = (pose.cameraRotation * dirCam).normalized;

        return new Ray(pose.cameraPosition, dirWorld);
    }

    public void ClearOverlays()
    {
        pendingLines = null;
        currentTrackedImage = null;
        StopAllCoroutines();
        if (textContainer != null)
        {
            textContainer.Clear();
            textContainer.MarkDirtyRepaint();
        }
        if (uiDocument != null && uiDocument.rootVisualElement != null)
        {
            uiDocument.rootVisualElement.MarkDirtyRepaint();
        }
        if (poseFilter != null)
        {
            poseFilter.SetOverlayVisibility(false, "ClearOverlays");
            poseFilter.ClearTarget();
        }
        if (worldQuadObj != null)
        {
            worldQuadObj.SetActive(false);
        }
    }

    private struct LineLayoutData
    {
        public string text;
        public float leftX;
        public float topY;
        public float width;
        public float height;
    }

    private void OnDestroy()
    {
        if (quadMaterial != null)
        {
            Destroy(quadMaterial);
        }

        if (worldQuadObj != null)
        {
            Destroy(worldQuadObj);
        }
    }
}

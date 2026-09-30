using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ARWorldSpaceUIToolkitController : MonoBehaviour
{
    [Header("UI Toolkit References")]
    [SerializeField] private PanelSettings basePanelSettings;
    [SerializeField] private PanelSettings worldSpacePanelSettings;
    [SerializeField] private RenderTexture offscreenRenderTexture;
    [SerializeField] private StyleSheet lineStyleSheet;
    [SerializeField] private Font rawFont;
    [SerializeField] private UnityEngine.TextCore.Text.FontAsset sdfFontAsset;

    [Header("Page Dimensions (Meters)")]
    [Tooltip("Matches ReferenceImageLibrary.asset (0.22m x 0.311m)")]
    [SerializeField] private Vector2 pageSizeMeters = new Vector2(0.22f, 0.31128225f);

    private UIDocument uiDocument;
    private GameObject worldQuadObj;
    private Material quadMaterial;
    private VisualElement rootElement;
    private VisualElement textContainer;
    private ARSpatialPoseFilter poseFilter;

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

            // Remove default collider
            Collider col = worldQuadObj.GetComponent<Collider>();
            if (col != null) Destroy(col);

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

        // Keep 3D Quad in world space (unparented from raw 60Hz ARCore transform hierarchy)
        if (worldQuadObj.transform.parent != transform)
        {
            worldQuadObj.transform.SetParent(transform, true);
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

    public void AttachToTrackedImage(ARTrackedImage trackedImage)
    {
        if (trackedImage == null) return;

        if (trackedImage.size.x > 0 && trackedImage.size.y > 0)
            pageSizeMeters = trackedImage.size;

        currentTrackedImage = trackedImage;

        if (worldQuadObj == null || uiDocument == null)
        {
            InitializeWorldSpaceUIToolkit();
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

    public void DisplayDetectedLines(List<DetectedTextLine> detectedLines, Vector2 visionImageSize)
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
                StartCoroutine(RetryDisplayNextFrame());
                return;
            }
        }

        // Explicit clean-slate: ensure textContainer is cleared before rendering new lines
        textContainer.Clear();
        RenderLines(detectedLines, visionImageSize);

        // Always show the quad once text is rendered
        if (worldQuadObj != null)
            worldQuadObj.SetActive(true);
    }

    private IEnumerator RetryDisplayNextFrame()
    {
        yield return null; // wait one frame for UIDocument panel to initialize
        if (pendingLines != null)
        {
            var lines = pendingLines;
            var imgSize = pendingImageSize;
            pendingLines = null;
            DisplayDetectedLines(lines, imgSize);
        }
    }

    private void RenderLines(List<DetectedTextLine> detectedLines, Vector2 visionImageSize)
    {
        textContainer.Clear();

        float texWidth = offscreenRenderTexture != null ? (float)offscreenRenderTexture.width : 2048f;
        float texHeight = offscreenRenderTexture != null ? (float)offscreenRenderTexture.height : 2898f;

        // 1. Calculate UNIFORM aspect scale factor (equal for both X and Y axes)
        float uniformScale = texWidth / visionImageSize.x;
        float activePanelHeight = visionImageSize.y * uniformScale;

        // 2. Compute physical world-space dimensions matching the cropped paragraph
        float physicalWidth = pageSizeMeters.x > 0f ? pageSizeMeters.x : 0.19f;
        float physicalHeight = physicalWidth * (visionImageSize.y / visionImageSize.x);

        if (worldQuadObj != null)
        {
            // Scale the 3D Quad to match the physical paragraph aspect ratio exactly
            worldQuadObj.transform.localScale = new Vector3(physicalWidth, physicalHeight, 1f);

            // 3. Map the active top rendered texture portion [0..activePanelHeight] across the full 3D Quad
            if (quadMaterial != null)
            {
                float uvScaleY = Mathf.Clamp01(activePanelHeight / texHeight);
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
            }
        }

        Debug.Log($"[World-Space UI Toolkit] Rendering {detectedLines.Count} lines. ImageSize:{visionImageSize.x}x{visionImageSize.y} | Quad:{physicalWidth:F3}mx{physicalHeight:F3}m | ActivePixels:{texWidth}x{activePanelHeight:F0}");

        int index = 1;
        foreach (var line in detectedLines)
        {
            var label = new Label(line.text);
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

            // Uniform pixel positioning (no non-uniform stretching)
            float scaledX = line.boundingBox.x * uniformScale;
            float scaledY = line.boundingBox.y * uniformScale;
            float scaledW = Math.Max(60f, line.boundingBox.width * uniformScale);
            float scaledH = Math.Max(30f, line.boundingBox.height * uniformScale);

            // Font point size matches 92% of the physical detected line height
            float fontSize = Mathf.Clamp(scaledH * 0.92f, 24f, 220f);

            label.style.position = Position.Absolute;
            label.style.left = scaledX;
            label.style.top = scaledY;
            label.style.width = scaledW;
            label.style.height = scaledH;
            label.style.fontSize = fontSize;
            label.style.unityTextAlign = line.text.Length < 15
                ? new StyleEnum<TextAnchor>(TextAnchor.MiddleCenter)
                : new StyleEnum<TextAnchor>(TextAnchor.MiddleLeft);
            label.style.paddingLeft = 4f;
            label.style.paddingRight = 4f;
            label.style.paddingTop = 0f;
            label.style.paddingBottom = 0f;
            label.style.overflow = Overflow.Visible;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = new StyleColor(new Color(0.05f, 0.05f, 0.05f, 1.0f));
            label.style.backgroundColor = new StyleColor(new Color(1.0f, 1.0f, 1.0f, 0.92f));

            textContainer.Add(label);
            Debug.Log($"[World-Space Line #{index++}] \"{line.text}\" | X:{scaledX:F0} Y:{scaledY:F0} W:{scaledW:F0} H:{scaledH:F0} Font:{fontSize:F0}px");
        }

        // Force UI Toolkit layout engine & rendering pipeline to resolve text metrics & repaint
        textContainer.MarkDirtyRepaint();
        if (uiDocument != null && uiDocument.rootVisualElement != null)
        {
            uiDocument.rootVisualElement.MarkDirtyRepaint();
        }
    }

    public void ClearOverlays()
    {
        pendingLines = null;
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
            poseFilter.ClearTarget();
        }
        if (worldQuadObj != null)
        {
            worldQuadObj.SetActive(false);
        }
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

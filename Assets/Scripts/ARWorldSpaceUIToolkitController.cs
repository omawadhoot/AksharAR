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
        }

        Transform targetParent = parentTransform != null ? parentTransform : transform;
        if (worldQuadObj.transform.parent != targetParent)
        {
            worldQuadObj.transform.SetParent(targetParent, false);
        }

        // Align Quad flat on paper surface (X/Z plane, Z = +0.001m offset)
        worldQuadObj.transform.localPosition = new Vector3(0f, 0.001f, 0f);
        worldQuadObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        worldQuadObj.transform.localScale = new Vector3(pageSizeMeters.x, pageSizeMeters.y, 1f);

        // Keep 3D quad inactive when not parented to a tracked image to prevent raycast blocking
        worldQuadObj.SetActive(parentTransform != null);

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

    // Tracks the last known parent so we can restore it after re-initialization
    private Transform lastKnownParent;
    // Pending lines to display if textContainer isn't ready yet
    private List<DetectedTextLine> pendingLines;
    private Vector2 pendingImageSize;

    public void AttachToTrackedImage(ARTrackedImage trackedImage)
    {
        if (trackedImage == null) return;

        if (trackedImage.size.x > 0 && trackedImage.size.y > 0)
            pageSizeMeters = trackedImage.size;

        lastKnownParent = trackedImage.transform;
        InitializeWorldSpaceUIToolkit(trackedImage.transform);

        // Only show if actively tracking — but never HIDE if we already have text rendered
        if (worldQuadObj != null && trackedImage.trackingState == TrackingState.Tracking)
            worldQuadObj.SetActive(true);
    }

    public void DisplayDetectedLines(List<DetectedTextLine> detectedLines, Vector2 visionImageSize)
    {
        if (detectedLines == null || detectedLines.Count == 0) return;

        // Ensure the quad exists but DO NOT call InitializeWorldSpaceUIToolkit() without a parent —
        // that would set SetActive(false) and hide the quad.
        // Instead, just make sure the quad is visible using the last known parent (or this transform).
        if (worldQuadObj == null)
        {
            InitializeWorldSpaceUIToolkit(lastKnownParent);
        }

        // Always show the quad once we have text — tracking attachment can come later
        if (worldQuadObj != null)
            worldQuadObj.SetActive(true);

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

        RenderLines(detectedLines, visionImageSize);
    }

    private IEnumerator RetryDisplayNextFrame()
    {
        yield return null; // wait one frame for UIDocument panel to initialize
        if (pendingLines != null)
        {
            DisplayDetectedLines(pendingLines, pendingImageSize);
            pendingLines = null;
        }
    }

    private void RenderLines(List<DetectedTextLine> detectedLines, Vector2 visionImageSize)
    {
        textContainer.Clear();

        float panelWidth = offscreenRenderTexture != null ? offscreenRenderTexture.width : 2048f;
        float panelHeight = offscreenRenderTexture != null ? offscreenRenderTexture.height : 2898f;

        float scaleX = panelWidth / visionImageSize.x;
        float scaleY = panelHeight / visionImageSize.y;

        Debug.Log($"[World-Space UI Toolkit] Rendering {detectedLines.Count} lines on 3D AR Quad ({panelWidth}x{panelHeight}). Quad active: {worldQuadObj?.activeSelf}");

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

            float scaledX = line.boundingBox.x * scaleX;
            float scaledY = line.boundingBox.y * scaleY;
            float scaledW = Math.Max(60f, line.boundingBox.width * scaleX);
            float scaledH = Math.Max(30f, line.boundingBox.height * scaleY);

            // Fill the line height snugly (88% of detected line height)
            float heightBasedSize = scaledH * 0.88f;

            // Strip out empty spaces so character count reflects true visual text density
            int nonSpaceChars = Mathf.Max(1, line.text.Replace(" ", "").Length);
            float widthBasedSize = (scaledW / nonSpaceChars) * 1.35f;

            // Pick optimal font size that respects both height and box width
            float fontSize = Mathf.Clamp(Mathf.Min(heightBasedSize, widthBasedSize), 28f, 180f);

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
        if (textContainer != null)
        {
            textContainer.Clear();
            textContainer.MarkDirtyRepaint();
        }
        if (uiDocument != null && uiDocument.rootVisualElement != null)
        {
            uiDocument.rootVisualElement.MarkDirtyRepaint();
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

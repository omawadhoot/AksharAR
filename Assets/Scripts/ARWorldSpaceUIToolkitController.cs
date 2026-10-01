using System;
using System.Collections;
using System.Collections.Generic;
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
        float physicalWidth = pageSizeMeters.x > 0f ? pageSizeMeters.x : 0.14f;
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
                if (quadMaterial.HasProperty("_Cull"))
                {
                    quadMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                }
            }
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

        bool useRaycastUnprojection = lastCapturePose.isValid && currentTrackedImage != null && worldQuadObj != null;
        Plane paperPlane = default;
        if (useRaycastUnprojection)
        {
            paperPlane = new Plane(currentTrackedImage.transform.up, currentTrackedImage.transform.position);
            Debug.Log($"[World-Space UI Toolkit] Active 3D Raycast Plane Unprojection ENABLED. Paper Normal: {currentTrackedImage.transform.up:F3}");
        }

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

            // 1. Default Uniform 2D scale fallback
            float scaledX = line.boundingBox.x * uniformScale;
            float scaledY = line.boundingBox.y * uniformScale;
            float scaledW = Math.Max(40f, line.boundingBox.width * uniformScale);
            float scaledH = Math.Max(20f, line.boundingBox.height * uniformScale);

            // 2. 3D Raycast Plane Unprojection: eliminates perspective keystoning / trapezoidal distortion
            if (useRaycastUnprojection)
            {
                Rect bb = line.boundingBox;
                float scaleX = (float)lastCapturePose.pixelCropRect.width / visionImageSize.x;
                float scaleY = (float)lastCapturePose.pixelCropRect.height / visionImageSize.y;

                float screenLeftX = lastCapturePose.pixelCropRect.x + bb.x * scaleX;
                float screenRightX = lastCapturePose.pixelCropRect.x + (bb.x + bb.width) * scaleX;
                float screenCenterX = lastCapturePose.pixelCropRect.x + (bb.x + bb.width * 0.5f) * scaleX;

                // Invert Y: Cloud Vision (0 is top) -> Screen pixels (0 is bottom)
                float screenCenterY = lastCapturePose.pixelCropRect.y + (visionImageSize.y - (bb.y + bb.height * 0.5f)) * scaleY;
                float screenTopY = lastCapturePose.pixelCropRect.y + (visionImageSize.y - bb.y) * scaleY;
                float screenBottomY = lastCapturePose.pixelCropRect.y + (visionImageSize.y - (bb.y + bb.height)) * scaleY;

                Ray rayLeft = GetRayFromPose(lastCapturePose, new Vector2(screenLeftX, screenCenterY));
                Ray rayRight = GetRayFromPose(lastCapturePose, new Vector2(screenRightX, screenCenterY));
                Ray rayTop = GetRayFromPose(lastCapturePose, new Vector2(screenCenterX, screenTopY));
                Ray rayBottom = GetRayFromPose(lastCapturePose, new Vector2(screenCenterX, screenBottomY));

                if (paperPlane.Raycast(rayLeft, out float distL) &&
                    paperPlane.Raycast(rayRight, out float distR) &&
                    paperPlane.Raycast(rayTop, out float distT) &&
                    paperPlane.Raycast(rayBottom, out float distB))
                {
                    Vector3 ptL = worldQuadObj.transform.InverseTransformPoint(rayLeft.GetPoint(distL));
                    Vector3 ptR = worldQuadObj.transform.InverseTransformPoint(rayRight.GetPoint(distR));
                    Vector3 ptT = worldQuadObj.transform.InverseTransformPoint(rayTop.GetPoint(distT));
                    Vector3 ptB = worldQuadObj.transform.InverseTransformPoint(rayBottom.GetPoint(distB));

                    // Quad mesh local space is [-0.5..+0.5] in X and Y
                    // Normalize to [0..1] and scale to UI Toolkit panel dimensions
                    float unprojectedX = (ptL.x + 0.5f) * texWidth;
                    float unprojectedY = (0.5f - ptT.y) * activePanelHeight;
                    float unprojectedW = Mathf.Max(40f, (ptR.x - ptL.x) * texWidth);
                    float unprojectedH = Mathf.Max(20f, (ptT.y - ptB.y) * activePanelHeight);

                    // Sanity check to protect against extreme grazing raycast angles (>75 degrees)
                    if (unprojectedW > 10f && unprojectedW < texWidth * 1.5f &&
                        unprojectedH > 10f && unprojectedH < activePanelHeight)
                    {
                        scaledX = unprojectedX;
                        scaledY = unprojectedY;
                        scaledW = unprojectedW;
                        scaledH = unprojectedH;
                    }
                }
            }

            // Font point size matches 72% of the physical detected line height (Devanagari cap-height ratio)
            float fontSize = Mathf.Clamp(scaledH * 0.72f, 16f, 130f);

            label.style.position = Position.Absolute;
            label.style.left = scaledX;
            label.style.top = scaledY;
            label.style.width = scaledW + 16f;
            label.style.height = scaledH;
            label.style.fontSize = fontSize;
            label.style.unityTextAlign = line.text.Length < 15
                ? new StyleEnum<TextAnchor>(TextAnchor.MiddleCenter)
                : new StyleEnum<TextAnchor>(TextAnchor.MiddleLeft);
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.style.paddingLeft = 6f;
            label.style.paddingRight = 6f;
            label.style.paddingTop = 0f;
            label.style.paddingBottom = 0f;
            label.style.borderLeftWidth = 0f;
            label.style.borderRightWidth = 0f;
            label.style.borderTopWidth = 0f;
            label.style.borderBottomWidth = 0f;
            label.style.overflow = Overflow.Visible;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.color = new StyleColor(new Color(0.05f, 0.05f, 0.05f, 1.0f));
            label.style.backgroundColor = new StyleColor(new Color(1.0f, 1.0f, 1.0f, 0.94f));

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

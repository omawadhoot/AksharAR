using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ARWorldSpaceOverlayController : MonoBehaviour
{
    [Header("World-Space Canvas Config")]
    [SerializeField] private Canvas worldSpaceCanvas;
    [SerializeField] private RectTransform canvasRectTransform;
    [SerializeField] private TMP_FontAsset dyslexiaFontAsset;

    [Header("Page Dimensions (Meters)")]
    [Tooltip("Base physical page metrics in meters. Matches ReferenceImageLibrary.asset (0.22m x 0.311m)")]
    [SerializeField] private Vector2 pageSizeMeters = new Vector2(0.22f, 0.31128225f);

    [Header("Line Strip Styling")]
    [SerializeField] private Color textBackgroundColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private Color textColor = new Color(0.07f, 0.07f, 0.07f, 1f);

    private List<GameObject> activeLineObjects = new List<GameObject>();

    private void Awake()
    {
        EnsureCanvasSetup();
        LoadFontAssetIfNeeded();
    }

    private void EnsureCanvasSetup(Transform parentTransform = null)
    {
        if (worldSpaceCanvas == null)
            worldSpaceCanvas = GetComponentInChildren<Canvas>();

        if (worldSpaceCanvas == null)
        {
            GameObject canvasObj = new GameObject("AR_WorldSpace_Canvas");
            Transform targetParent = parentTransform != null ? parentTransform : transform;
            canvasObj.transform.SetParent(targetParent, false);
            
            worldSpaceCanvas = canvasObj.AddComponent<Canvas>();
            worldSpaceCanvas.renderMode = RenderMode.WorldSpace;
            canvasObj.AddComponent<CanvasScaler>();
        }

        canvasRectTransform = worldSpaceCanvas.GetComponent<RectTransform>();

        if (parentTransform != null && worldSpaceCanvas.transform.parent != parentTransform)
        {
            worldSpaceCanvas.transform.SetParent(parentTransform, false);
        }
        
        // Orient flat along the 3D plane flush with page surface (X/Z plane, Z = +0.001m offset)
        canvasRectTransform.localPosition = new Vector3(0f, 0.001f, 0f);
        canvasRectTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        
        float highResCanvasW = 2048f;
        float highResCanvasH = 2048f * (pageSizeMeters.y / pageSizeMeters.x);

        canvasRectTransform.sizeDelta = new Vector2(highResCanvasW, highResCanvasH);
        canvasRectTransform.localScale = new Vector3(pageSizeMeters.x / highResCanvasW, pageSizeMeters.y / highResCanvasH, 1f);
    }

    public void AttachToTrackedImage(UnityEngine.XR.ARFoundation.ARTrackedImage trackedImage)
    {
        if (trackedImage == null) return;

        if (trackedImage.size.x > 0 && trackedImage.size.y > 0)
        {
            pageSizeMeters = trackedImage.size;
        }

        EnsureCanvasSetup(trackedImage.transform);

        bool isTracking = trackedImage.trackingState == UnityEngine.XR.ARSubsystems.TrackingState.Tracking;
        if (worldSpaceCanvas != null)
        {
            worldSpaceCanvas.gameObject.SetActive(isTracking);
        }
    }

    private void LoadFontAssetIfNeeded()
    {
        if (dyslexiaFontAsset == null)
        {
            dyslexiaFontAsset = Resources.Load<TMP_FontAsset>("NeevA-Dyslexia-Regular SDF");

#if UNITY_EDITOR
            if (dyslexiaFontAsset == null)
            {
                dyslexiaFontAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NeevA-Dyslexia-Regular SDF.asset");
            }
#endif

            if (dyslexiaFontAsset == null)
            {
                Debug.LogWarning("[ARWorldSpaceOverlayController] NeevA-Dyslexia-Regular SDF font asset not found! Using fallback TMP font.");
                dyslexiaFontAsset = TMP_Settings.defaultFontAsset;
            }
            else
            {
                Debug.Log("[ARWorldSpaceOverlayController] Successfully loaded Devanagari Dyslexia TMP Font Asset.");
            }
        }
    }

    public void DisplayDetectedLinesOnPlane(List<DetectedTextLine> detectedLines, Vector2 visionImageSize)
    {
        EnsureCanvasSetup();
        ClearOverlays();

        if (detectedLines == null || detectedLines.Count == 0) return;

        float canvasW = canvasRectTransform.sizeDelta.x;
        float canvasH = canvasRectTransform.sizeDelta.y;

        Debug.Log($"[AR 2D-on-3D Plane] Rendering {detectedLines.Count} Hindi text lines flat on 3D AR Canvas ({canvasW}x{canvasH}).");

        int index = 1;
        foreach (var line in detectedLines)
        {
            // Calculate normalized top-left percentages (0.0 to 1.0)
            float normX = line.boundingBox.x / visionImageSize.x;
            float normY = line.boundingBox.y / visionImageSize.y;
            float normW = line.boundingBox.width / visionImageSize.x;
            float normH = line.boundingBox.height / visionImageSize.y;

            // Convert normalized percentages to RectTransform local space (Center origin = 0,0)
            float localX = (normX - 0.5f) * canvasW;
            float localY = (0.5f - normY) * canvasH;
            float stripW = Mathf.Max(60f, normW * canvasW);
            float stripH = Mathf.Max(24f, normH * canvasH * 1.15f);

            // Create Line Strip Container
            GameObject lineObj = new GameObject($"LineStrip_{index++}");
            lineObj.transform.SetParent(canvasRectTransform, false);

            RectTransform stripRect = lineObj.AddComponent<RectTransform>();
            stripRect.anchorMin = new Vector2(0.5f, 0.5f);
            stripRect.anchorMax = new Vector2(0.5f, 0.5f);
            stripRect.pivot = new Vector2(0f, 1f); // Top-Left pivot
            stripRect.anchoredPosition = new Vector2(localX, localY);
            stripRect.sizeDelta = new Vector2(stripW, stripH);

            // Add Background Pill Image
            Image bgImage = lineObj.AddComponent<Image>();
            bgImage.color = textBackgroundColor;

            // Create Text Child
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(lineObj.transform, false);

            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            TextMeshProUGUI tmpText = textObj.AddComponent<TextMeshProUGUI>();
            if (dyslexiaFontAsset != null) tmpText.font = dyslexiaFontAsset;
            tmpText.text = line.text;
            tmpText.color = textColor;
            tmpText.alignment = TextAlignmentOptions.MidlineLeft;
            tmpText.enableWordWrapping = false;
            tmpText.overflowMode = TextOverflowModes.Overflow;
            tmpText.enableAutoSizing = true;
            tmpText.fontSizeMin = 28f;
            tmpText.fontSizeMax = Mathf.Max(36f, stripH * 0.85f);
            tmpText.margin = new Vector4(16f, 4f, 16f, 4f);

            activeLineObjects.Add(lineObj);
        }
    }

    public void ClearOverlays()
    {
        foreach (var obj in activeLineObjects)
        {
            if (obj != null) Destroy(obj);
        }
        activeLineObjects.Clear();
    }
}

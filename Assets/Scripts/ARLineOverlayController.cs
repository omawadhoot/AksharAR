using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UIToolkit = UnityEngine.UIElements;

[Serializable]
public struct DetectedTextLine
{
    public string text;
    public Rect boundingBox;
}

/// <summary>
/// Manages the screen HUD for AksharAR.
/// The Scan button is a uGUI Button on a Screen Space - Overlay Canvas
/// so that GraphicRaycaster handles Android touch independently of AR Foundation.
/// UI Toolkit (ARLineOverlayDocument) is kept for purely visual overlay content — no interactivity.
/// </summary>
public class ARLineOverlayController : MonoBehaviour
{
    [Header("uGUI Scan Button (Screen Space Overlay Canvas)")]
    [SerializeField] private Button scanButton; // UnityEngine.UI.Button

    [Header("UI Toolkit (Visual Overlay Only — no touch)")]
    [SerializeField] private UIToolkit.UIDocument uiDocument;

    [Header("Scan Controller Reference")]
    [SerializeField] private ARPageScanController pageScanController;

    // UI Toolkit visual root (read-only, pass-through)
    private UIToolkit.VisualElement rootElement;

    private float lastClickTimestamp = -1f;
    private bool isReadingMode = false;

    [Header("Camera App HUD Elements")]
    private RectTransform bottomBarRect;
    private Image bottomBarBg;
    private RectTransform shutterOuterRingRect;
    private Image shutterOuterRingImg;
    private Image shutterCoreImg;
    private RectTransform shutterCoreRect;

    // Mode Carousel Elements
    private GameObject modeCarouselObj;
    private Text poemModeText;
    private GameObject poemDotObj;
    private Text chapterModeText;
    private GameObject chapterDotObj;

    // Secondary Quick-Rescan / Flip Button
    private GameObject quickRescanBtnObj;

    // Procedural Sprites
    private Sprite circleRingSprite;
    private Sprite circleSolidSprite;
    private Sprite circleGlassSprite;
    private Sprite circleDotSprite;

    private void Start()
    {
        GenerateProceduralCameraSprites();
        InitializeCameraUI();
        InitializeUIToolkitVisual();
    }

    private void OnDestroy()
    {
        if (scanButton != null)
            scanButton.onClick.RemoveListener(OnScanButtonClicked);
    }

    // ─── Procedural Circle Sprites Generator ──────────────────────────────────

    private void GenerateProceduralCameraSprites()
    {
        circleRingSprite = CreateCircleSprite(128, Color.clear, 6f, Color.white);
        circleSolidSprite = CreateCircleSprite(128, Color.white, 0f, Color.white);
        circleGlassSprite = CreateCircleSprite(128, new Color(0.12f, 0.15f, 0.22f, 0.70f), 3f, new Color(1f, 1f, 1f, 0.65f));
        circleDotSprite = CreateCircleSprite(32, new Color(1.0f, 0.75f, 0.05f, 1.0f), 0f, Color.white); // Camera Amber/Gold
    }

    private Sprite CreateCircleSprite(int size, Color fillColor, float borderWidth, Color borderColor)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        float radius = size * 0.5f;
        float center = radius;
        float borderInnerRadius = radius - borderWidth;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(center, center));
                if (dist > radius)
                {
                    pixels[y * size + x] = Color.clear;
                }
                else if (borderWidth > 0 && dist > borderInnerRadius)
                {
                    float alpha = Mathf.Clamp01(radius - dist);
                    pixels[y * size + x] = new Color(borderColor.r, borderColor.g, borderColor.b, borderColor.a * alpha);
                }
                else
                {
                    float alpha = Mathf.Clamp01(radius - dist);
                    pixels[y * size + x] = new Color(fillColor.r, fillColor.g, fillColor.b, fillColor.a * alpha);
                }
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }

    // ─── Native Camera App UI Setup ───────────────────────────────────────────

    private void InitializeCameraUI()
    {
        if (scanButton == null)
        {
            var btnObj = GameObject.Find("ScanButton_UGUI");
            if (btnObj != null)
                scanButton = btnObj.GetComponent<Button>();
        }

        if (scanButton == null)
        {
            Debug.LogError("[ARLineOverlayController] uGUI ScanButton not found!");
            return;
        }

        // 1. Upgrade BottomBar to Cinematic Dark Camera Bottom Bar
        Transform parentTransform = scanButton.transform.parent;
        if (parentTransform != null)
        {
            bottomBarRect = parentTransform.GetComponent<RectTransform>();
            bottomBarBg = parentTransform.GetComponent<Image>();

            if (bottomBarRect != null)
            {
                bottomBarRect.anchorMin = new Vector2(0f, 0f);
                bottomBarRect.anchorMax = new Vector2(1f, 0f);
                bottomBarRect.pivot = new Vector2(0.5f, 0f);
                bottomBarRect.anchoredPosition = Vector2.zero;
                bottomBarRect.sizeDelta = new Vector2(0f, 195f);
            }

            if (bottomBarBg != null)
            {
                bottomBarBg.color = new Color(0.01f, 0.02f, 0.04f, 0.88f);
            }
        }

        // 2. Transform ScanButton into Native Camera Shutter Button
        RectTransform btnRect = scanButton.GetComponent<RectTransform>();
        if (btnRect != null)
        {
            btnRect.anchorMin = new Vector2(0.5f, 0f);
            btnRect.anchorMax = new Vector2(0.5f, 0f);
            btnRect.pivot = new Vector2(0.5f, 0.5f);
            btnRect.anchoredPosition = new Vector2(0f, 68f);
            btnRect.sizeDelta = new Vector2(86f, 86f);
        }

        shutterCoreImg = scanButton.GetComponent<Image>();
        if (shutterCoreImg != null)
        {
            shutterCoreImg.sprite = circleSolidSprite;
            shutterCoreImg.color = Color.white;
            shutterCoreImg.type = Image.Type.Simple;
        }

        // Outer Bezel Ring for the Shutter
        Transform existingBezel = scanButton.transform.Find("Shutter_OuterRing");
        if (existingBezel == null)
        {
            GameObject bezelObj = new GameObject("Shutter_OuterRing");
            bezelObj.transform.SetParent(scanButton.transform, false);
            shutterOuterRingRect = bezelObj.AddComponent<RectTransform>();
            shutterOuterRingRect.anchorMin = Vector2.zero;
            shutterOuterRingRect.anchorMax = Vector2.one;
            shutterOuterRingRect.sizeDelta = new Vector2(18f, 18f); // Slightly larger than core
            shutterOuterRingRect.anchoredPosition = Vector2.zero;

            shutterOuterRingImg = bezelObj.AddComponent<Image>();
            shutterOuterRingImg.sprite = circleRingSprite;
            shutterOuterRingImg.color = new Color(1f, 1f, 1f, 0.90f);
            shutterOuterRingImg.raycastTarget = false;
        }

        // Center Shutter Icon / Label
        var tmp = scanButton.GetComponentInChildren<TMPro.TMP_Text>();
        if (tmp != null)
        {
            tmp.text = ""; // Crisp solid white shutter by default
            tmp.fontSize = 28f;
            tmp.color = Color.white;
        }
        var legacyText = scanButton.GetComponentInChildren<Text>();
        if (legacyText != null)
        {
            legacyText.text = "";
            legacyText.fontSize = 28;
            legacyText.color = Color.white;
        }

        // 3. Build Mode Carousel (POEM | CHAPTER) directly above Shutter Button
        BuildCameraModeCarousel(parentTransform != null ? (RectTransform)parentTransform : btnRect);

        // 4. Build Secondary Quick Rescan / Flip Button to the right of shutter
        BuildQuickRescanButton(parentTransform != null ? (RectTransform)parentTransform : btnRect);

        scanButton.onClick.RemoveAllListeners();
        scanButton.onClick.AddListener(OnScanButtonClicked);
        Debug.Log("[ARLineOverlayController] Native Camera Shutter & Carousel initialized.");
    }

    private void BuildCameraModeCarousel(RectTransform parent)
    {
        if (modeCarouselObj != null) return;

        modeCarouselObj = new GameObject("Camera_ModeCarousel");
        modeCarouselObj.transform.SetParent(parent, false);
        RectTransform carouselRect = modeCarouselObj.AddComponent<RectTransform>();
        carouselRect.anchorMin = new Vector2(0.5f, 0f);
        carouselRect.anchorMax = new Vector2(0.5f, 0f);
        carouselRect.pivot = new Vector2(0.5f, 0.5f);
        carouselRect.anchoredPosition = new Vector2(0f, 142f);
        carouselRect.sizeDelta = new Vector2(300f, 40f);

        // Poem Mode Tab
        GameObject poemObj = new GameObject("Tab_Poem");
        poemObj.transform.SetParent(carouselRect, false);
        RectTransform poemRect = poemObj.AddComponent<RectTransform>();
        poemRect.anchorMin = new Vector2(0.5f, 0.5f);
        poemRect.anchorMax = new Vector2(0.5f, 0.5f);
        poemRect.pivot = new Vector2(0.5f, 0.5f);
        poemRect.anchoredPosition = new Vector2(-65f, 0f);
        poemRect.sizeDelta = new Vector2(100f, 36f);

        Button poemBtn = poemObj.AddComponent<Button>();
        Image poemHit = poemObj.AddComponent<Image>();
        poemHit.color = Color.clear;
        poemBtn.targetGraphic = poemHit;
        poemBtn.onClick.AddListener(() => SwitchReadingMode(ReadingMode.Poem));

        GameObject poemTextObj = new GameObject("Label");
        poemTextObj.transform.SetParent(poemObj.transform, false);
        RectTransform ptRect = poemTextObj.AddComponent<RectTransform>();
        ptRect.anchorMin = Vector2.zero;
        ptRect.anchorMax = Vector2.one;
        ptRect.sizeDelta = Vector2.zero;
        ptRect.anchoredPosition = new Vector2(0f, 4f);

        poemModeText = poemTextObj.AddComponent<Text>();
        poemModeText.text = "POEM";
        poemModeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (poemModeText.font == null) poemModeText.font = Font.CreateDynamicFontFromOSFont("Arial", 16);
        poemModeText.fontSize = 17;
        poemModeText.alignment = TextAnchor.MiddleCenter;
        poemModeText.raycastTarget = false;

        // Active Dot under Poem
        poemDotObj = new GameObject("Dot");
        poemDotObj.transform.SetParent(poemObj.transform, false);
        RectTransform pdRect = poemDotObj.AddComponent<RectTransform>();
        pdRect.anchorMin = new Vector2(0.5f, 0f);
        pdRect.anchorMax = new Vector2(0.5f, 0f);
        pdRect.pivot = new Vector2(0.5f, 0.5f);
        pdRect.anchoredPosition = new Vector2(0f, 2f);
        pdRect.sizeDelta = new Vector2(6f, 6f);
        Image pdImg = poemDotObj.AddComponent<Image>();
        pdImg.sprite = circleDotSprite;
        pdImg.color = new Color(1.0f, 0.75f, 0.05f, 1.0f);
        pdImg.raycastTarget = false;

        // Chapter Mode Tab
        GameObject chapterObj = new GameObject("Tab_Chapter");
        chapterObj.transform.SetParent(carouselRect, false);
        RectTransform chapterRect = chapterObj.AddComponent<RectTransform>();
        chapterRect.anchorMin = new Vector2(0.5f, 0.5f);
        chapterRect.anchorMax = new Vector2(0.5f, 0.5f);
        chapterRect.pivot = new Vector2(0.5f, 0.5f);
        chapterRect.anchoredPosition = new Vector2(65f, 0f);
        chapterRect.sizeDelta = new Vector2(100f, 36f);

        Button chapterBtn = chapterObj.AddComponent<Button>();
        Image chapterHit = chapterObj.AddComponent<Image>();
        chapterHit.color = Color.clear;
        chapterBtn.targetGraphic = chapterHit;
        chapterBtn.onClick.AddListener(() => SwitchReadingMode(ReadingMode.Chapter));

        GameObject chapterTextObj = new GameObject("Label");
        chapterTextObj.transform.SetParent(chapterObj.transform, false);
        RectTransform ctRect = chapterTextObj.AddComponent<RectTransform>();
        ctRect.anchorMin = Vector2.zero;
        ctRect.anchorMax = Vector2.one;
        ctRect.sizeDelta = Vector2.zero;
        ctRect.anchoredPosition = new Vector2(0f, 4f);

        chapterModeText = chapterTextObj.AddComponent<Text>();
        chapterModeText.text = "CHAPTER";
        chapterModeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (chapterModeText.font == null) chapterModeText.font = Font.CreateDynamicFontFromOSFont("Arial", 16);
        chapterModeText.fontSize = 17;
        chapterModeText.alignment = TextAnchor.MiddleCenter;
        chapterModeText.raycastTarget = false;

        // Active Dot under Chapter
        chapterDotObj = new GameObject("Dot");
        chapterDotObj.transform.SetParent(chapterObj.transform, false);
        RectTransform cdRect = chapterDotObj.AddComponent<RectTransform>();
        cdRect.anchorMin = new Vector2(0.5f, 0f);
        cdRect.anchorMax = new Vector2(0.5f, 0f);
        cdRect.pivot = new Vector2(0.5f, 0.5f);
        cdRect.anchoredPosition = new Vector2(0f, 2f);
        cdRect.sizeDelta = new Vector2(6f, 6f);
        Image cdImg = chapterDotObj.AddComponent<Image>();
        cdImg.sprite = circleDotSprite;
        cdImg.color = new Color(1.0f, 0.75f, 0.05f, 1.0f);
        cdImg.raycastTarget = false;

        // Query initial mode from Viewfinder
        ReadingWindowViewfinder vf = FindFirstObjectByType<ReadingWindowViewfinder>();
        ReadingMode initialMode = vf != null ? vf.ActiveReadingMode : ReadingMode.Poem;
        UpdateModeCarouselVisuals(initialMode);
    }

    private void BuildQuickRescanButton(RectTransform parent)
    {
        if (quickRescanBtnObj != null) return;

        quickRescanBtnObj = new GameObject("Btn_QuickRescan");
        quickRescanBtnObj.transform.SetParent(parent, false);
        RectTransform btnRect = quickRescanBtnObj.AddComponent<RectTransform>();
        btnRect.anchorMin = new Vector2(0.5f, 0f);
        btnRect.anchorMax = new Vector2(0.5f, 0f);
        btnRect.pivot = new Vector2(0.5f, 0.5f);
        btnRect.anchoredPosition = new Vector2(130f, 68f);
        btnRect.sizeDelta = new Vector2(52f, 52f);

        Image btnImg = quickRescanBtnObj.AddComponent<Image>();
        btnImg.sprite = circleGlassSprite;
        btnImg.color = Color.white;
        btnImg.raycastTarget = true;

        Button btn = quickRescanBtnObj.AddComponent<Button>();
        btn.targetGraphic = btnImg;
        btn.onClick.AddListener(OnScanButtonClicked);

        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(quickRescanBtnObj.transform, false);
        RectTransform iconRect = iconObj.AddComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.sizeDelta = Vector2.zero;
        iconRect.anchoredPosition = Vector2.zero;

        Text iconText = iconObj.AddComponent<Text>();
        iconText.text = "🔄";
        iconText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (iconText.font == null) iconText.font = Font.CreateDynamicFontFromOSFont("Arial", 22);
        iconText.fontSize = 22;
        iconText.alignment = TextAnchor.MiddleCenter;
        iconText.color = Color.white;
        iconText.raycastTarget = false;
    }

    private void SwitchReadingMode(ReadingMode mode)
    {
        ReadingWindowViewfinder vf = FindFirstObjectByType<ReadingWindowViewfinder>();
        if (vf != null)
        {
            vf.SetReadingMode(mode);
        }
        UpdateModeCarouselVisuals(mode);
    }

    public void SyncModeCarousel(ReadingMode mode)
    {
        UpdateModeCarouselVisuals(mode);
    }

    private void UpdateModeCarouselVisuals(ReadingMode mode)
    {
        Color goldColor = new Color(1.0f, 0.75f, 0.05f, 1.0f); // Camera Amber
        Color inactiveColor = new Color(1.0f, 1.0f, 1.0f, 0.50f);

        if (poemModeText != null)
        {
            poemModeText.color = (mode == ReadingMode.Poem) ? goldColor : inactiveColor;
            poemModeText.fontStyle = (mode == ReadingMode.Poem) ? FontStyle.Bold : FontStyle.Normal;
        }

        if (poemDotObj != null)
            poemDotObj.SetActive(mode == ReadingMode.Poem);

        if (chapterModeText != null)
        {
            chapterModeText.color = (mode == ReadingMode.Chapter) ? goldColor : inactiveColor;
            chapterModeText.fontStyle = (mode == ReadingMode.Chapter) ? FontStyle.Bold : FontStyle.Normal;
        }

        if (chapterDotObj != null)
            chapterDotObj.SetActive(mode == ReadingMode.Chapter);
    }

    // ─── UI Toolkit Visual Setup (pass-through, no interaction) ───────────────

    private void InitializeUIToolkitVisual()
    {
        if (uiDocument == null)
        {
            var docObj = GameObject.Find("ARLineOverlayDocument");
            if (docObj != null)
                uiDocument = docObj.GetComponent<UIToolkit.UIDocument>();
        }

        if (uiDocument == null) return;

        rootElement = uiDocument.rootVisualElement;
        if (rootElement == null) return;

        // Entire UI Toolkit tree is pass-through — no interactive elements remain here
        SetPickingModeRecursive(rootElement, UIToolkit.PickingMode.Ignore);
        Debug.Log("[ARLineOverlayController] UI Toolkit visual overlay initialized (pass-through).");
    }

    private void SetPickingModeRecursive(UIToolkit.VisualElement el, UIToolkit.PickingMode mode)
    {
        el.pickingMode = mode;
        foreach (var child in el.Children())
            SetPickingModeRecursive(child, mode);
    }

    // ─── Button Callback ──────────────────────────────────────────────────────

    public void OnScanButtonClicked()
    {
        if (Time.unscaledTime - lastClickTimestamp < 0.6f) return;
        lastClickTimestamp = Time.unscaledTime;

        // If currently in Reading Mode, tapping "🔄 Rescan" restores the viewfinder to frame a new paragraph:
        if (isReadingMode)
        {
            isReadingMode = false;
            var viewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();
            if (viewfinder != null)
                viewfinder.SetVisibility(true);

            SetScanButtonText("📷 Scan Page");
            return;
        }

        // Otherwise, trigger the camera capture & OCR scan
        Debug.Log("[ARLineOverlayController] >>> CAPTURE / SCAN BUTTON CLICKED <<<");

        SetScanButtonText("⏳ Scanning...");
        SetScanButtonInteractable(false);

        // Hide viewfinder while scanning so screen stays clean
        var vf = FindFirstObjectByType<ReadingWindowViewfinder>();
        if (vf != null)
            vf.SetVisibility(false);

        if (pageScanController == null)
            pageScanController = FindAnyObjectByType<ARPageScanController>();

        if (pageScanController != null)
        {
            pageScanController.TriggerManualScan();
        }
        else
        {
            Debug.LogError("[ARLineOverlayController] ARPageScanController not found!");
            ResetScanButton();
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public void ResetScanButton()
    {
        isReadingMode = false;
        SetScanButtonText("📷 Scan Page");
        SetScanButtonInteractable(true);

        var viewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();
        if (viewfinder != null)
            viewfinder.SetVisibility(true);
    }

    public void DisplayDetectedLines(List<DetectedTextLine> detectedLines, Vector2 visionImageSize)
    {
        // OCR text received & rendered onto AR Quad!
        // 1. Completely hide the viewfinder so the reader enjoys an unobstructed view:
        var viewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();
        if (viewfinder != null)
            viewfinder.SetVisibility(false);

        // 2. Transition button to "🔄 Rescan" mode:
        isReadingMode = true;
        SetScanButtonText("🔄 Rescan");
        SetScanButtonInteractable(true);
    }

    public void SetUIVisibility(bool visible)
    {
        if (scanButton != null)
            scanButton.gameObject.SetActive(visible);

        if (rootElement != null)
            rootElement.style.display = visible ? UIToolkit.DisplayStyle.Flex : UIToolkit.DisplayStyle.None;

        var viewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();
        if (viewfinder != null)
        {
            // Do not re-show viewfinder if in reading mode
            if (!visible || !isReadingMode)
                viewfinder.SetVisibility(visible);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    public void SetScanButtonText(string text)
    {
        if (scanButton == null) return;

        // Adapt Shutter Core appearance based on state
        if (shutterCoreImg != null)
        {
            if (text.Contains("Scanning"))
            {
                shutterCoreImg.color = new Color(0.20f, 0.80f, 1.0f, 0.90f); // Pulsing Cyan
            }
            else if (text.Contains("Rescan"))
            {
                shutterCoreImg.color = new Color(0.0f, 0.90f, 0.46f, 0.95f); // Emerald Green
            }
            else
            {
                shutterCoreImg.color = Color.white; // Pure white camera shutter
            }
        }

        string buttonIcon = "";
        if (text.Contains("Scanning")) buttonIcon = "⏳";
        else if (text.Contains("Rescan")) buttonIcon = "🔄";

        var label = scanButton.GetComponentInChildren<Text>();
        if (label != null) label.text = buttonIcon;

        // Also support TextMeshPro on the button label if present
        var tmp = scanButton.GetComponentInChildren<TMPro.TMP_Text>();
        if (tmp != null) tmp.text = buttonIcon;
    }

    public void SetScanButtonInteractable(bool interactable)
    {
        if (scanButton != null)
            scanButton.interactable = interactable;
    }
}

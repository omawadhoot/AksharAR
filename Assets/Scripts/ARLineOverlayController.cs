using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[Serializable]
public struct DetectedTextLine
{
    public string text;
    public Rect boundingBox;
}

/// <summary>
/// Manages the native camera app screen HUD for AksharAR entirely via UI Toolkit.
/// Controls the circular Shutter Button, Mode Carousel (POEM | CHAPTER),
/// secondary Quick-Rescan / Flip Button, and camera states.
/// </summary>
public class ARLineOverlayController : MonoBehaviour
{
    [Header("UI Toolkit Document")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Scan Controller Reference")]
    [SerializeField] private ARPageScanController pageScanController;

    // UI Toolkit Elements
    private VisualElement rootElement;
    private VisualElement bottomBar;
    private Button shutterButton;
    private Label shutterIcon;
    private Button quickRescanBtn;
    private Button tabPoem;
    private Button tabChapter;
    private VisualElement poemDot;
    private VisualElement chapterDot;

    private float lastClickTimestamp = -1f;
    private bool isReadingMode = false;

    private void Awake()
    {
        DisableLegacyCanvasIfPresent();
    }

    private void Start()
    {
        InitializeUIToolkit();
    }

    private void OnDestroy()
    {
        UnbindCallbacks();
    }

    private void DisableLegacyCanvasIfPresent()
    {
        var legacyCanvas = GameObject.Find("ScanButtonCanvas");
        if (legacyCanvas != null)
        {
            var canvasComp = legacyCanvas.GetComponent<UnityEngine.Canvas>();
            if (canvasComp != null) canvasComp.enabled = false;
            var raycaster = legacyCanvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;
            Debug.Log("[ARLineOverlayController] Disabled legacy Canvas component in favor of UI Toolkit.");
        }

        var bottomBar = GameObject.Find("BottomBar");
        if (bottomBar != null)
        {
            bottomBar.SetActive(false);
        }

        var btnObj = GameObject.Find("ScanButton_UGUI");
        if (btnObj != null)
        {
            btnObj.SetActive(false);
        }
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
            Debug.LogError("[ARLineOverlayController] UIDocument not found!");
            return;
        }

        rootElement = uiDocument.rootVisualElement;
        if (rootElement == null) return;

        bottomBar = rootElement.Q<VisualElement>("BottomBar");
        shutterButton = rootElement.Q<Button>("ShutterButton");
        shutterIcon = rootElement.Q<Label>("ShutterIcon");
        quickRescanBtn = rootElement.Q<Button>("QuickRescanButton");
        tabPoem = rootElement.Q<Button>("TabPoem");
        tabChapter = rootElement.Q<Button>("TabChapter");
        poemDot = rootElement.Q<VisualElement>("PoemDot");
        chapterDot = rootElement.Q<VisualElement>("ChapterDot");

        BindCallbacks();

        // Query initial reading mode from viewfinder
        ReadingWindowViewfinder vf = FindFirstObjectByType<ReadingWindowViewfinder>();
        ReadingMode initialMode = vf != null ? vf.ActiveReadingMode : ReadingMode.Poem;
        SyncModeCarousel(initialMode);

        SetScanButtonText("📷 Scan Page");
        Debug.Log("[ARLineOverlayController] UI Toolkit Camera HUD initialized successfully.");
    }

    private void BindCallbacks()
    {
        if (shutterButton != null)
            shutterButton.clicked += OnScanButtonClicked;

        if (quickRescanBtn != null)
            quickRescanBtn.clicked += OnScanButtonClicked;

        if (tabPoem != null)
            tabPoem.clicked += () => SwitchReadingMode(ReadingMode.Poem);

        if (tabChapter != null)
            tabChapter.clicked += () => SwitchReadingMode(ReadingMode.Chapter);
    }

    private void UnbindCallbacks()
    {
        if (shutterButton != null)
            shutterButton.clicked -= OnScanButtonClicked;

        if (quickRescanBtn != null)
            quickRescanBtn.clicked -= OnScanButtonClicked;
    }

    private void SwitchReadingMode(ReadingMode mode)
    {
        ReadingWindowViewfinder vf = FindFirstObjectByType<ReadingWindowViewfinder>();
        if (vf != null)
        {
            vf.SetReadingMode(mode);
        }
        SyncModeCarousel(mode);
    }

    public void SyncModeCarousel(ReadingMode mode)
    {
        if (tabPoem != null)
            tabPoem.EnableInClassList("mode-tab--active", mode == ReadingMode.Poem);

        if (tabChapter != null)
            tabChapter.EnableInClassList("mode-tab--active", mode == ReadingMode.Chapter);

        if (poemDot != null)
            poemDot.EnableInClassList("mode-dot--hidden", mode != ReadingMode.Poem);

        if (chapterDot != null)
            chapterDot.EnableInClassList("mode-dot--hidden", mode != ReadingMode.Chapter);
    }

    // ─── Button Callback ──────────────────────────────────────────────────────

    public void OnScanButtonClicked()
    {
        if (Time.unscaledTime - lastClickTimestamp < 0.6f) return;
        lastClickTimestamp = Time.unscaledTime;

        // If currently in Reading Mode, tapping Rescan restores the viewfinder
        if (isReadingMode)
        {
            isReadingMode = false;
            var viewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();
            if (viewfinder != null)
                viewfinder.SetVisibility(true);

            SetScanButtonText("📷 Scan Page");
            return;
        }

        // Trigger camera capture & OCR scan
        Debug.Log("[ARLineOverlayController] >>> SHUTTER BUTTON CLICKED <<<");

        SetScanButtonText("⏳ Scanning...");
        SetScanButtonInteractable(false);

        // Hide viewfinder while scanning so screen stays clean
        var vf = FindFirstObjectByType<ReadingWindowViewfinder>();
        if (vf != null)
            vf.SetVisibility(false);

        if (pageScanController == null)
            pageScanController = FindFirstObjectByType<ARPageScanController>();

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

        // 2. Transition shutter button to Emerald Rescan state:
        isReadingMode = true;
        SetScanButtonText("🔄 Rescan");
        SetScanButtonInteractable(true);
    }

    public void SetUIVisibility(bool visible)
    {
        if (rootElement != null)
            rootElement.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        var viewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();
        if (viewfinder != null)
        {
            if (!visible || !isReadingMode)
                viewfinder.SetVisibility(visible);
        }
    }

    public void SetScanButtonText(string text)
    {
        if (shutterButton == null) return;

        if (text.Contains("Scanning"))
        {
            shutterButton.RemoveFromClassList("shutter-core--rescan");
            shutterButton.AddToClassList("shutter-core--scanning");
            if (shutterIcon != null) shutterIcon.text = "⏳";
        }
        else if (text.Contains("Rescan"))
        {
            shutterButton.RemoveFromClassList("shutter-core--scanning");
            shutterButton.AddToClassList("shutter-core--rescan");
            if (shutterIcon != null) shutterIcon.text = "🔄";
        }
        else
        {
            shutterButton.RemoveFromClassList("shutter-core--scanning");
            shutterButton.RemoveFromClassList("shutter-core--rescan");
            if (shutterIcon != null) shutterIcon.text = "";
        }
    }

    public void SetScanButtonInteractable(bool interactable)
    {
        if (shutterButton != null)
            shutterButton.SetEnabled(interactable);

        if (quickRescanBtn != null)
            quickRescanBtn.SetEnabled(interactable);
    }
}

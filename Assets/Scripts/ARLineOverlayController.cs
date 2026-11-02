using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

[Serializable]
public struct DetectedTextLine
{
    public string text;
    public Rect boundingBox;

    public float minX => boundingBox.xMin;
    public float maxX => boundingBox.xMax;
    public float minY => boundingBox.yMin;
    public float maxY => boundingBox.yMax;
    public float width => boundingBox.width;
    public float height => boundingBox.height;
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

    [Header("Dyslexia Font")]
    [SerializeField] private Font dyslexiaFont;
    [SerializeField] private UnityEngine.TextCore.Text.FontAsset dyslexiaSdfFont;

    // UI Toolkit Elements
    private VisualElement rootElement;
    private VisualElement freezeFrameBackdrop;
    private VisualElement freezeFrameVignette;
    private VisualElement freezeFrameTextContainer;
    private VisualElement bottomBar;
    private Button shutterButton;
    private Label shutterIcon;
    private Button quickRescanBtn;
    private VisualElement rescanIcon;
    private Button tabPoem;
    private Button tabChapter;
    private Label poemLabelDevanagari;
    private Label chapterLabelDevanagari;
    private Label chapterLabelLatin;
    private VisualElement poemDot;
    private VisualElement chapterDot;
    private Button accessibilitySettingsBtn;
    private Button homeButton;

    private Texture2D currentFreezeTexture;
    private float lastScanClickTimestamp = -1f;
    private float lastRescanClickTimestamp = -1f;
    private bool isReadingMode = false;
    private bool isScanningActive = false;
    private ReadingWindowViewfinder cachedViewfinder;

    private EventCallback<PointerDownEvent> shutterPointerDownCallback;
    private EventCallback<PointerDownEvent> quickRescanPointerDownCallback;
    private EventCallback<PointerDownEvent> tabPoemPointerDownCallback;
    private EventCallback<PointerDownEvent> tabChapterPointerDownCallback;

    private System.Action onPoemClicked;
    private System.Action onChapterClicked;

    // 2D Focus Reader UI Elements & State
    private VisualElement focusReaderOverlay;
    private VisualElement focusReaderSlateCard;
    private VisualElement focusReaderLinesContainer;
    private Button focusAudioToggleBtn;
    private Label focusAudioIcon;
    private Label focusAudioLabel;
    private Button focusRevealAllBtn;
    private Label focusRevealAllLabel;
    private Button focusCloseBtn;
    private Button focusPrevLineBtn;
    private Label focusPrevLineLabel;
    private Button focusNextLineBtn;
    private Label focusNextLineLabel;
    private Button focusReplayAudioBtn;
    private Label focusLineProgress;

    public bool IsFocusReaderOpen { get; private set; } = false;
    private bool isFocusAudioEnabled = true;
    private bool isFocusRevealAll = false;
    private int focusWindowStartIndex = 0;
    private int focusActiveLineIndex = 0;
    private HashSet<int> focusRevealedLineIndices = new HashSet<int>();
    private List<string> cachedFocusLines = new List<string>();

    private void Awake()
    {
        DisableLegacyCanvasIfPresent();
        EnsureUIRenderTextureBridge();
        EnsureDyslexiaComponents();
    }

    private void EnsureDyslexiaComponents()
    {
        if (FindFirstObjectByType<DyslexiaProfileManager>() == null)
        {
            var mgrObj = new GameObject("DyslexiaProfileManager");
            mgrObj.AddComponent<DyslexiaProfileManager>();
        }
    }

    private void EnsureUIRenderTextureBridge()
    {
        var docObj = GameObject.Find("ARLineOverlayDocument");
        if (docObj != null)
        {
            var oldFitter = docObj.GetComponent<ARCameraHUDWorldSpaceFitter>();
            if (oldFitter != null)
            {
                Destroy(oldFitter);
            }

            if (docObj.GetComponent<ARUIRenderTextureBridge>() == null)
            {
                docObj.AddComponent<ARUIRenderTextureBridge>();
            }
        }
    }

    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;

    private void Start()
    {
        if (DyslexiaProfileManager.Instance != null && !DyslexiaProfileManager.Instance.Profile.hasCompletedOnboarding)
        {
            OpenAccessibilityWizard();
            return;
        }

        InitializeUIToolkit();
    }

    private void Update()
    {
        if (rootElement == null || rootElement.panel == null || (uiDocument != null && uiDocument.rootVisualElement != rootElement))
        {
            InitializeUIToolkit();
        }

        if (rootElement != null && (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight))
        {
            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            ApplyOrientationLayout(Screen.width > Screen.height);
        }

        UpdateShutterButtonState();
    }

    private void OnEnable()
    {
        AppLanguageManager.OnLanguageChanged += OnLanguageChanged;
    }

    private void OnDisable()
    {
        AppLanguageManager.OnLanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(AppLanguage lang)
    {
        UpdateLocalizedModeLabels();
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
            return;
        }

        if (rootElement != null)
        {
            UnbindCallbacks();
        }

        rootElement = uiDocument.rootVisualElement;
        if (rootElement == null || rootElement.panel == null)
        {
            rootElement = null;
            return;
        }

        freezeFrameBackdrop = rootElement.Q<VisualElement>("FreezeFrameBackdrop");
        freezeFrameVignette = rootElement.Q<VisualElement>("FreezeFrameVignette");
        freezeFrameTextContainer = rootElement.Q<VisualElement>("FreezeFrameTextContainer");
        bottomBar = rootElement.Q<VisualElement>("BottomBar");
        shutterButton = rootElement.Q<Button>("ShutterButton");
        shutterIcon = rootElement.Q<Label>("ShutterIcon");
        quickRescanBtn = rootElement.Q<Button>("QuickRescanButton");
        rescanIcon = rootElement.Q<VisualElement>("RescanIcon");
        if (rescanIcon != null)
        {
            var refreshTex = Resources.Load<Texture2D>("Icons/Icon_Refresh");
            if (refreshTex != null)
            {
                rescanIcon.style.backgroundImage = new StyleBackground(refreshTex);
            }
        }

        var settingsIconElem = rootElement.Q<VisualElement>("SettingsButtonIcon");
        if (settingsIconElem != null)
        {
            var settingsTex = Resources.Load<Texture2D>("Icons/Icon_Settings");
            if (settingsTex != null)
            {
                settingsIconElem.style.backgroundImage = new StyleBackground(settingsTex);
            }
        }

        var homeIconElem = rootElement.Q<VisualElement>("HomeButtonIcon");
        if (homeIconElem != null)
        {
            var homeTex = Resources.Load<Texture2D>("Icons/Icon_Home");
            if (homeTex != null)
            {
                homeIconElem.style.backgroundImage = new StyleBackground(homeTex);
            }
        }

        homeButton = rootElement.Q<Button>("HomeButton");
        accessibilitySettingsBtn = rootElement.Q<Button>("AccessibilitySettingsButton");

        tabPoem = rootElement.Q<Button>("TabPoem");
        tabChapter = rootElement.Q<Button>("TabChapter");
        poemDot = rootElement.Q<VisualElement>("PoemDot");
        chapterDot = rootElement.Q<VisualElement>("ChapterDot");

        poemLabelDevanagari = rootElement.Q<Label>("PoemLabelDevanagari");
        chapterLabelDevanagari = rootElement.Q<Label>("ChapterLabelDevanagari");
        chapterLabelLatin = rootElement.Q<Label>("ChapterLabelLatin");
        ApplyDyslexiaFont(poemLabelDevanagari);
        ApplyDyslexiaFont(chapterLabelDevanagari);
        ApplyDyslexiaFont(chapterLabelLatin);
        ApplyDyslexiaFont(shutterIcon);

        UpdateLocalizedModeLabels();

        BindCallbacks();

        // Query initial reading mode from viewfinder
        ReadingWindowViewfinder vf = FindAnyObjectByType<ReadingWindowViewfinder>(FindObjectsInactive.Include);
        if (vf != null && !vf.gameObject.activeInHierarchy)
        {
            vf.gameObject.SetActive(true);
        }
        ReadingMode initialMode = vf != null ? vf.ActiveReadingMode : ReadingMode.Poem;
        SyncModeCarousel(initialMode);

        SetScanButtonText("📷 Scan Page");
        ApplyOrientationLayout(initialMode == ReadingMode.Chapter || Screen.width > Screen.height);
        InitializeFocusReaderElements();
        Debug.Log("[ARLineOverlayController] UI Toolkit Camera HUD initialized successfully.");
    }

    private void BindCallbacks()
    {
        if (shutterButton != null)
        {
            shutterPointerDownCallback = evt =>
            {
                if (!shutterButton.enabledSelf)
                {
                    evt.StopPropagation();
                    return;
                }
                OnScanButtonClicked();
                evt.StopPropagation();
            };
            shutterButton.RegisterCallback(shutterPointerDownCallback);
            shutterButton.clicked += OnScanButtonClicked;
        }

        if (quickRescanBtn != null)
        {
            quickRescanBtn.SetEnabled(true);
            quickRescanPointerDownCallback = evt =>
            {
                OnRescanButtonClicked();
            };
            quickRescanBtn.RegisterCallback(quickRescanPointerDownCallback);
            quickRescanBtn.RegisterCallback<ClickEvent>(evt => OnRescanButtonClicked());
            quickRescanBtn.clicked += OnRescanButtonClicked;
        }

        if (homeButton != null)
        {
            homeButton.RegisterCallback<ClickEvent>(evt =>
            {
                OnHomeButtonClicked();
                evt.StopPropagation();
            });
            homeButton.clicked += OnHomeButtonClicked;
        }

        if (accessibilitySettingsBtn != null)
        {
            accessibilitySettingsBtn.RegisterCallback<ClickEvent>(evt =>
            {
                OpenAccessibilityWizard();
                evt.StopPropagation();
            });
            accessibilitySettingsBtn.clicked += OpenAccessibilityWizard;
        }

        if (tabPoem != null)
        {
            tabPoem.RegisterCallback<ClickEvent>(evt =>
            {
                SwitchReadingMode(ReadingMode.Poem);
                evt.StopPropagation();
            });
            onPoemClicked = () => SwitchReadingMode(ReadingMode.Poem);
            tabPoem.clicked += onPoemClicked;
        }

        if (tabChapter != null)
        {
            tabChapter.RegisterCallback<ClickEvent>(evt =>
            {
                SwitchReadingMode(ReadingMode.Chapter);
                evt.StopPropagation();
            });
            onChapterClicked = () => SwitchReadingMode(ReadingMode.Chapter);
            tabChapter.clicked += onChapterClicked;
        }
    }

    private void UnbindCallbacks()
    {
        if (shutterButton != null)
        {
            if (shutterPointerDownCallback != null)
                shutterButton.UnregisterCallback(shutterPointerDownCallback);
            shutterButton.clicked -= OnScanButtonClicked;
        }

        if (quickRescanBtn != null)
        {
            if (quickRescanPointerDownCallback != null)
                quickRescanBtn.UnregisterCallback(quickRescanPointerDownCallback);
            quickRescanBtn.clicked -= OnRescanButtonClicked;
        }

        if (tabPoem != null)
        {
            if (onPoemClicked != null)
                tabPoem.clicked -= onPoemClicked;
        }

        if (tabChapter != null)
        {
            if (onChapterClicked != null)
                tabChapter.clicked -= onChapterClicked;
        }

        if (accessibilitySettingsBtn != null)
        {
            accessibilitySettingsBtn.clicked -= OpenAccessibilityWizard;
        }

        if (homeButton != null)
        {
            homeButton.clicked -= OnHomeButtonClicked;
        }

        if (focusCloseBtn != null) focusCloseBtn.clicked -= CloseFocusReader;
        if (focusAudioToggleBtn != null) focusAudioToggleBtn.clicked -= ToggleFocusAudio;
        if (focusRevealAllBtn != null) focusRevealAllBtn.clicked -= ToggleFocusRevealAll;
        if (focusPrevLineBtn != null) focusPrevLineBtn.clicked -= StepFocusPrevLine;
        if (focusNextLineBtn != null) focusNextLineBtn.clicked -= StepFocusNextLine;
    }

    private void SwitchReadingMode(ReadingMode mode)
    {
        ReadingWindowViewfinder vf = FindAnyObjectByType<ReadingWindowViewfinder>(FindObjectsInactive.Include);
        if (vf != null)
        {
            if (!vf.gameObject.activeInHierarchy)
            {
                vf.gameObject.SetActive(true);
            }
            vf.SetReadingMode(mode);
        }
        ApplyOrientationLayout(mode == ReadingMode.Chapter);
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

        if (accessibilitySettingsBtn != null)
            accessibilitySettingsBtn.style.display = DisplayStyle.Flex;
    }

    private void UpdateLocalizedModeLabels()
    {
        if (poemLabelDevanagari != null)
            poemLabelDevanagari.text = AppLanguageManager.ModePoemDevanagari;

        if (chapterLabelDevanagari != null)
            chapterLabelDevanagari.text = AppLanguageManager.ModeChapterDevanagari;

        if (chapterLabelLatin != null)
            chapterLabelLatin.text = AppLanguageManager.ModeChapterLatin;
    }

    private void EnsureDyslexiaFont()
    {
        if (dyslexiaFont == null)
            dyslexiaFont = Resources.Load<Font>("Fonts/NeevA-Dyslexia-Regular");
        if (dyslexiaFont == null)
            dyslexiaFont = Resources.Load<Font>("NeevA-Dyslexia-Regular");
#if UNITY_EDITOR
        if (dyslexiaFont == null)
            dyslexiaFont = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NeevA-Dyslexia-Regular.ttf");
#endif

        if (dyslexiaSdfFont == null)
            dyslexiaSdfFont = Resources.Load<UnityEngine.TextCore.Text.FontAsset>("Fonts/NeevA-Dyslexia-Regular SDF");
        if (dyslexiaSdfFont == null)
            dyslexiaSdfFont = Resources.Load<UnityEngine.TextCore.Text.FontAsset>("NeevA-Dyslexia-Regular SDF");
#if UNITY_EDITOR
        if (dyslexiaSdfFont == null)
            dyslexiaSdfFont = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>("Assets/Fonts/NeevA-Dyslexia-Regular SDF.asset");
#endif
    }

    private void ApplyDyslexiaFont(Label label)
    {
        if (label == null) return;
        EnsureDyslexiaFont();
        label.style.unityTextGenerator = TextGeneratorType.Advanced;
        if (dyslexiaFont != null)
        {
            label.style.unityFont = new StyleFont(dyslexiaFont);
            label.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(dyslexiaFont));
        }
        else if (dyslexiaSdfFont != null)
        {
            label.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(dyslexiaSdfFont));
        }
    }

    public void ApplyOrientationLayout(bool isLandscape)
    {
        if (rootElement == null)
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) uiDocument = FindFirstObjectByType<UIDocument>();
            if (uiDocument != null) rootElement = uiDocument.rootVisualElement;
            if (rootElement != null)
            {
                bottomBar = rootElement.Q<VisualElement>("BottomBar");
            }
        }
        if (rootElement == null) return;

        rootElement.EnableInClassList("hud-overlay--landscape", isLandscape);

        if (bottomBar != null)
            bottomBar.EnableInClassList("camera-bar--landscape", isLandscape);

        var carousel = rootElement.Q<VisualElement>("ModeCarousel");
        if (carousel != null)
            carousel.EnableInClassList("mode-carousel--landscape", isLandscape);

        var controlsRow = rootElement.Q<VisualElement>("CameraControlsRow");
        if (controlsRow != null)
            controlsRow.EnableInClassList("camera-controls-row--landscape", isLandscape);

        var viewfinder = rootElement.Q<VisualElement>("ViewfinderRoot");
        if (viewfinder != null)
            viewfinder.EnableInClassList("viewfinder-root--landscape", isLandscape);

        var topControls = rootElement.Q<VisualElement>("TopControlsContainer");
        if (topControls != null)
            topControls.EnableInClassList("top-controls-container--landscape", isLandscape);

        var levelPillElem = rootElement.Q<VisualElement>("LevelPill");
        if (levelPillElem != null)
            levelPillElem.EnableInClassList("level-pill--landscape", isLandscape);

        if (accessibilitySettingsBtn != null)
            accessibilitySettingsBtn.EnableInClassList("settings-button--landscape", isLandscape);

        var leftSpacer = rootElement.Q<VisualElement>("LeftSpacerSlot");
        if (leftSpacer != null)
            leftSpacer.EnableInClassList("left-spacer--landscape", isLandscape);

        var sideSlots = rootElement.Query<VisualElement>(className: "control-side-slot").ToList();
        foreach (var slot in sideSlots)
        {
            slot.EnableInClassList("control-side-slot--landscape", isLandscape);
        }

        Debug.Log($"[ARLineOverlayController] Applied {(isLandscape ? "LANDSCAPE" : "PORTRAIT")} HUD layout.");
    }

    // ─── Button Callback ──────────────────────────────────────────────────────

    private bool isTransitioningToOnboarding = false;

    public void OpenAccessibilityWizard()
    {
        if (isTransitioningToOnboarding) return;
        isTransitioningToOnboarding = true;
        StartCoroutine(TransitionToOnboardingRoutine());
    }

    private System.Collections.IEnumerator TransitionToOnboardingRoutine()
    {
        Debug.Log("[ARLineOverlayController] >>> ACCESSIBILITY SETTINGS BUTTON CLICKED <<< Safely waiting for frame completion before orientation switch & scene load.");
        
        if (accessibilitySettingsBtn != null)
            accessibilitySettingsBtn.SetEnabled(false);

        // Wait until all OpenGL draw calls and UI render textures for the current frame finish
        yield return new WaitForEndOfFrame();

        // Switch to portrait safely outside the active render loop
        Screen.autorotateToLandscapeLeft = false;
        Screen.autorotateToLandscapeRight = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToPortrait = true;
        Screen.orientation = ScreenOrientation.Portrait;
#if UNITY_EDITOR
        ReadingWindowViewfinder.SetEditorGameViewOrientation(false);
#endif

        yield return null;

        UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Onboarding");
    }

    private bool isTransitioningToStart = false;

    public void OnHomeButtonClicked()
    {
        if (isTransitioningToStart) return;
        isTransitioningToStart = true;
        Debug.Log("[ARLineOverlayController] >>> HOME BUTTON CLICKED <<< Returning to StartScreen.");
        StartCoroutine(TransitionToStartScreenRoutine());
    }

    private System.Collections.IEnumerator TransitionToStartScreenRoutine()
    {
        if (homeButton != null)
            homeButton.SetEnabled(false);

        yield return new WaitForEndOfFrame();

        Screen.autorotateToLandscapeLeft = false;
        Screen.autorotateToLandscapeRight = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToPortrait = true;
        Screen.orientation = ScreenOrientation.Portrait;
#if UNITY_EDITOR
        ReadingWindowViewfinder.SetEditorGameViewOrientation(false);
#endif

        yield return null;

        UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("StartScreen");
    }

    public void OnRescanButtonClicked()
    {
        if (Time.unscaledTime - lastRescanClickTimestamp < 0.25f) return;
        lastRescanClickTimestamp = Time.unscaledTime;

        Debug.Log("[ARLineOverlayController] >>> RESCAN BUTTON CLICKED <<< Wiping overlay and resetting tracking.");

        if (pageScanController == null)
            pageScanController = FindFirstObjectByType<ARPageScanController>();

        if (pageScanController != null)
        {
            pageScanController.ResetTrackingSession();
        }
        else
        {
            var worldSpaceUIToolkit = FindFirstObjectByType<ARWorldSpaceUIToolkitController>();
            if (worldSpaceUIToolkit != null)
                worldSpaceUIToolkit.ClearOverlays();

            var worldSpaceOverlay = FindFirstObjectByType<ARWorldSpaceOverlayController>();
            if (worldSpaceOverlay != null)
                worldSpaceOverlay.ClearOverlays();
        }

        ResetScanButton();
    }

    public void UpdateShutterButtonState()
    {
        // Rescan button must ALWAYS be interactable and ready to reset
        if (quickRescanBtn != null && !quickRescanBtn.enabledSelf)
        {
            quickRescanBtn.SetEnabled(true);
        }

        if (shutterButton == null) return;

        // 1. Actively scanning
        if (isScanningActive)
        {
            shutterButton.SetEnabled(false);
            shutterButton.EnableInClassList("shutter-core--scanning", true);
            shutterButton.EnableInClassList("shutter-core--tilted", false);
            shutterButton.EnableInClassList("shutter-core--reading-locked", false);
            if (shutterIcon != null) shutterIcon.text = "• • •";
            return;
        }

        // 2. Reading Mode: Area has been scanned!
        // The scanning button CANNOT work again until the Rescan button is pressed!
        if (isReadingMode)
        {
            shutterButton.SetEnabled(false);
            shutterButton.EnableInClassList("shutter-core--scanning", false);
            shutterButton.EnableInClassList("shutter-core--tilted", false);
            shutterButton.EnableInClassList("shutter-core--reading-locked", true);
            if (shutterIcon != null) shutterIcon.text = "";
            return;
        }

        // 3. Framing Mode:
        // Only functional when the camera holding angle is right and the viewfinder is clear!
        if (cachedViewfinder == null)
            cachedViewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();

        bool isLevelAndClear = (cachedViewfinder == null || cachedViewfinder.IsDeviceLevel);

        shutterButton.SetEnabled(isLevelAndClear);
        shutterButton.EnableInClassList("shutter-core--tilted", !isLevelAndClear);
        shutterButton.EnableInClassList("shutter-core--scanning", false);
        shutterButton.EnableInClassList("shutter-core--reading-locked", false);
        if (shutterIcon != null) shutterIcon.text = "";
    }

    public void OnScanButtonClicked()
    {
        if (Time.unscaledTime - lastScanClickTimestamp < 0.5f) return;
        lastScanClickTimestamp = Time.unscaledTime;

        // 1. Once an area is scanned, scanning button can ONLY work again once Rescan is pressed!
        if (isReadingMode)
        {
            Debug.Log("[ARLineOverlayController] Scan button locked in Reading Mode. Tap RESCAN button to frame a new stanza.");
            return;
        }

        // 2. If actively scanning, ignore
        if (isScanningActive)
        {
            return;
        }

        // 3. Angle / Viewfinder Gatekeeper: Only functional when viewfinder is clear and angle is right!
        if (cachedViewfinder == null)
            cachedViewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();

        if (cachedViewfinder != null && !cachedViewfinder.IsDeviceLevel)
        {
            Debug.LogWarning($"[ARLineOverlayController] Cannot scan: Device tilted ({cachedViewfinder.CurrentTiltAngle:F0}° > limit). Hold camera level until viewfinder turns clear.");
            return;
        }

        // All checks passed! Trigger camera capture & OCR scan
        Debug.Log("[ARLineOverlayController] >>> SHUTTER BUTTON CLICKED (Angle Level & Ready) <<<");

        isScanningActive = true;
        if (quickRescanBtn != null)
            quickRescanBtn.SetEnabled(true);
        UpdateShutterButtonState();

        // Hide viewfinder while scanning so screen stays clean
        if (cachedViewfinder != null)
            cachedViewfinder.SetVisibility(false);

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

    // ─── Public API: In-Situ Freeze-Frame AR ─────────────────────────────────

    public void SetFreezeFrameTexture(Texture2D liveFrame)
    {
        if (liveFrame == null) return;

        if (currentFreezeTexture != null && currentFreezeTexture != liveFrame)
        {
            Destroy(currentFreezeTexture);
        }
        currentFreezeTexture = liveFrame;

        if (freezeFrameBackdrop == null && rootElement != null)
            freezeFrameBackdrop = rootElement.Q<VisualElement>("FreezeFrameBackdrop");

        if (freezeFrameVignette == null && rootElement != null)
            freezeFrameVignette = rootElement.Q<VisualElement>("FreezeFrameVignette");

        if (freezeFrameTextContainer == null && rootElement != null)
            freezeFrameTextContainer = rootElement.Q<VisualElement>("FreezeFrameTextContainer");

        if (freezeFrameBackdrop != null)
        {
            freezeFrameBackdrop.style.backgroundImage = new StyleBackground(liveFrame);
            freezeFrameBackdrop.style.display = DisplayStyle.Flex;
        }

        if (freezeFrameVignette != null)
        {
            var vigTex = Resources.Load<Texture2D>("Textures/Vignette_Spotlight");
            if (vigTex != null)
            {
                freezeFrameVignette.style.backgroundImage = new StyleBackground(vigTex);
            }
            freezeFrameVignette.style.display = DisplayStyle.Flex;
            freezeFrameVignette.style.opacity = 1f;
        }

        if (freezeFrameTextContainer != null)
        {
            freezeFrameTextContainer.Clear();
            freezeFrameTextContainer.style.display = DisplayStyle.Flex;
        }

        // Hide viewfinder letterbox masks during reading so full image is visible
        if (cachedViewfinder == null)
            cachedViewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();

        if (cachedViewfinder != null)
            cachedViewfinder.SetVisibility(false);
    }

    public void ClearFreezeFrame()
    {
        if (freezeFrameBackdrop != null)
        {
            freezeFrameBackdrop.style.display = DisplayStyle.None;
            freezeFrameBackdrop.style.backgroundImage = StyleKeyword.None;
        }

        if (freezeFrameVignette != null)
        {
            freezeFrameVignette.style.display = DisplayStyle.None;
        }

        if (freezeFrameTextContainer != null)
        {
            freezeFrameTextContainer.Clear();
            freezeFrameTextContainer.style.display = DisplayStyle.None;
        }

        if (currentFreezeTexture != null)
        {
            Destroy(currentFreezeTexture);
            currentFreezeTexture = null;
        }
    }

    public void ResetScanButton()
    {
        if (IsFocusReaderOpen)
        {
            CloseFocusReader();
        }

        isReadingMode = false;
        isScanningActive = false;

        ClearFreezeFrame();

        if (quickRescanBtn != null)
            quickRescanBtn.SetEnabled(true);

        if (cachedViewfinder == null)
            cachedViewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();

        if (cachedViewfinder != null)
            cachedViewfinder.SetVisibility(true);

        UpdateShutterButtonState();
    }

    public void DisplayDetectedLines(List<DetectedTextLine> detectedLines, Vector2 visionImageSize)
    {
        DisplayDetectedLines(detectedLines, visionImageSize, currentFreezeTexture);
    }

    public void DisplayDetectedLines(List<DetectedTextLine> detectedLines, Vector2 visionImageSize, Texture2D capturedFrameTexture)
    {
        if (detectedLines == null || detectedLines.Count == 0)
        {
            ResetScanButton();
            return;
        }

        // 1. Query viewfinder Crop Rect BEFORE hiding it to ensure valid UI Toolkit geometry:
        if (cachedViewfinder == null)
            cachedViewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();

        Rect normCrop = (cachedViewfinder != null) 
            ? cachedViewfinder.GetNormalizedCropRect() 
            : new Rect(0.07f, 0.31f, 0.86f, 0.38f);

        // Completely hide the viewfinder so the reader enjoys an unobstructed view:
        if (cachedViewfinder != null)
            cachedViewfinder.SetVisibility(false);

        // 2. Lock scanning button until Rescan button is pressed:
        isReadingMode = true;
        isScanningActive = false;

        if (quickRescanBtn != null)
            quickRescanBtn.SetEnabled(true);

        UpdateShutterButtonState();

        if (freezeFrameTextContainer == null && rootElement != null)
            freezeFrameTextContainer = rootElement.Q<VisualElement>("FreezeFrameTextContainer");

        if (freezeFrameTextContainer == null) return;
        freezeFrameTextContainer.Clear();
        freezeFrameTextContainer.style.display = DisplayStyle.Flex;

        // 3. Compute viewport screen geometry
        float panelW = (rootElement != null && rootElement.layout.width > 10f) ? rootElement.layout.width : (float)Screen.width;
        float panelH = (rootElement != null && rootElement.layout.height > 10f) ? rootElement.layout.height : (float)Screen.height;
        bool isLandscape = panelW > panelH;
        float maxSafeRight = isLandscape ? (panelW - 320f) : (panelW - 16f);

        float boxLeft = normCrop.x * panelW;
        float boxTop = (1.0f - normCrop.y - normCrop.height) * panelH;
        float boxWidth = normCrop.width * panelW;
        float boxHeight = normCrop.height * panelH;

        float scaleX = (visionImageSize.x > 0f) ? (boxWidth / visionImageSize.x) : 1f;
        float scaleY = (visionImageSize.y > 0f) ? (boxHeight / visionImageSize.y) : 1f;

        // 4. Retrieve Active Dyslexia Profile
        DyslexiaProfile activeProfile = (DyslexiaProfileManager.Instance != null)
            ? DyslexiaProfileManager.Instance.Profile
            : new DyslexiaProfile();

        // 5. Stanza & typography harmonization
        var sortedLines = detectedLines.OrderBy(l => l.minY).ToList();
        float stanzaMinX = sortedLines.Min(l => l.minX) * scaleX;
        float stanzaMaxX = sortedLines.Max(l => l.maxX) * scaleX;
        float stanzaMinY = sortedLines.Min(l => l.minY) * scaleY;
        float stanzaMaxY = sortedLines.Max(l => l.maxY) * scaleY;

        var rawHeights = sortedLines.Select(l => l.height * scaleY).OrderBy(h => h).ToList();
        float medianLineHeight = rawHeights.Count > 0 ? rawHeights[rawHeights.Count / 2] : 40f * scaleY;
        float baseFontSize = Mathf.Clamp(medianLineHeight * 0.75f, 18f, 90f);
        float harmonizedFontSize = baseFontSize * activeProfile.fontScaleMultiplier;

        // 6. Paper Substrate Color & CVD-Safe Color Scheme
        Color sampledPaperColor = SamplePaperColorFromFrame(capturedFrameTexture != null ? capturedFrameTexture : currentFreezeTexture, sortedLines.Count > 0 ? sortedLines[sortedLines.Count / 2] : default, visionImageSize);
        Color finalSubstrateColor = activeProfile.GetSubstrateColor(sampledPaperColor);

        Color colA = activeProfile.GetSyllableColorA();
        Color colB = activeProfile.GetSyllableColorB();
        bool useAlternatingSyllables = activeProfile.enableSyllableSegmentation &&
                                      activeProfile.colorPalette != ColorBlindPalette.Monochrome &&
                                      activeProfile.severity != DyslexiaSeverity.Mild;

        // 7. Direct 2D Line-by-Line Anchoring (Exact In-Situ Placement)
        List<FreezeLineLayoutData> layouts = new List<FreezeLineLayoutData>();

        for (int i = 0; i < sortedLines.Count; i++)
        {
            var line = sortedLines[i];
            float scaledX = line.minX * scaleX;
            float scaledY = line.minY * scaleY;
            float scaledW = Mathf.Max(30f, line.width * scaleX);
            float scaledH = Mathf.Max(24f, line.height * scaleY);

            string cleanText = CloudVisionService.NormalizePunctuationSpacing(line.text);
            layouts.Add(new FreezeLineLayoutData
            {
                text = cleanText,
                screenX = boxLeft + scaledX,
                screenY = boxTop + scaledY,
                width = scaledW,
                height = scaledH
            });
        }

        float hMarginPx = 24f;
        float vMarginPx = 20f;

        bool insertSeparators = activeProfile.ShouldInsertSeparators();

        // 8. Backing Card Mask: Perfectly blankets the entire detected stanza
        VisualElement blockCard = new VisualElement { name = "FreezeBlockCard" };
        blockCard.AddToClassList("freeze-frame-card");
        if (activeProfile.substrateTint == SubstrateTint.InpaintedPaper)
        {
            blockCard.AddToClassList("freeze-frame-card--natural");
        }
        else
        {
            blockCard.AddToClassList("freeze-frame-card--tinted");
        }

        float cardLeft = Mathf.Max(0f, boxLeft + stanzaMinX - hMarginPx);
        float rawCardWidth = (stanzaMaxX - stanzaMinX) + (hMarginPx * 2f);
        float clampedCardWidth = Mathf.Min(rawCardWidth, Mathf.Max(60f, maxSafeRight - cardLeft));

        blockCard.style.left = cardLeft;
        blockCard.style.top = Mathf.Max(0f, boxTop + stanzaMinY - vMarginPx);
        blockCard.style.width = clampedCardWidth;
        blockCard.style.height = (stanzaMaxY - stanzaMinY) + (vMarginPx * 2f);
        blockCard.style.backgroundColor = new StyleColor(finalSubstrateColor);
        freezeFrameTextContainer.Add(blockCard);

        cachedFocusLines = layouts.Select(l => l.text).ToList();

        // In Chapter Mode (or Landscape), provide a dedicated "📖 वाचन कक्ष (Focus Reader)" launch button
        if (isLandscape || (cachedViewfinder != null && cachedViewfinder.ActiveReadingMode == ReadingMode.Chapter))
        {
            Button focusReaderBtn = new Button { name = "CardFocusReaderBtn" };
            focusReaderBtn.AddToClassList("card-focus-reader-btn");
            focusReaderBtn.text = "📖 वाचन कक्ष (Focus Reader)";
            
            var capturedLines = new List<string>(cachedFocusLines);
            focusReaderBtn.RegisterCallback<ClickEvent>(evt =>
            {
                OpenFocusReader(capturedLines);
                evt.StopPropagation();
            });

            float btnW = 310f;
            float btnH = 48f;
            focusReaderBtn.style.position = Position.Absolute;
            focusReaderBtn.style.top = Mathf.Max(12f, boxTop + stanzaMinY - 58f);
            focusReaderBtn.style.left = Mathf.Max(cardLeft, cardLeft + clampedCardWidth - btnW);
            focusReaderBtn.style.width = btnW;
            focusReaderBtn.style.height = btnH;

            freezeFrameTextContainer.Add(focusReaderBtn);
        }

        EnsureDyslexiaFont();

        // 9. Render Dyslexic Labels with Devanagari Akshar CVD Segmentation & Unbroken Clusters
        List<Label> createdLabels = new List<Label>();
        List<float> targetWidths = new List<float>();

        float letterSpacing = activeProfile.GetLetterSpacingEm();
        float wordSpacing = activeProfile.GetWordSpacingMultiplier();

        foreach (var data in layouts)
        {
            // Logical clean string is preserved in data.text; display string gets rich-text formatting with spacing
            string formattedText = DevanagariSyllableParser.ColorizeSyllablesRichText(
                data.text, colA, colB, useAlternatingSyllables, insertSeparators, letterSpacing, wordSpacing
            );
            Label label = new Label(formattedText);
            label.AddToClassList("freeze-frame-line");

            if (dyslexiaFont != null)
            {
                label.style.unityFont = new StyleFont(dyslexiaFont);
                label.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(dyslexiaFont));
            }
            else if (dyslexiaSdfFont != null)
            {
                label.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromSDFFont(dyslexiaSdfFont));
            }

            label.style.left = data.screenX;
            label.style.top = data.screenY;
            label.style.width = data.width;
            label.style.height = data.height;
            label.style.fontSize = harmonizedFontSize;
            label.style.color = new StyleColor(colA);

            // Store clean unformatted text in userData for TTS and copy operations
            label.userData = data.text;

            // Interactive Click Highlight & Clean Read-Aloud
            if (activeProfile.enableTouchHighlight)
            {
                string cleanLineText = data.text;
                label.RegisterCallback<PointerDownEvent>(evt =>
                {
                    label.AddToClassList("freeze-frame-line--highlighted");
                    Debug.Log($"[Freeze-Frame AR] Tapped line (Clean OCR): \"{cleanLineText}\"");
                    MarathiTTSHelper.Speak(cleanLineText);
                });

                label.RegisterCallback<PointerUpEvent>(evt =>
                {
                    label.schedule.Execute(() => label.RemoveFromClassList("freeze-frame-line--highlighted")).StartingIn(400);
                });
            }

            freezeFrameTextContainer.Add(label);
            createdLabels.Add(label);
            targetWidths.Add(data.width);
        }

        freezeFrameTextContainer.MarkDirtyRepaint();

        // 10. Multi-Stage Layout Fit Policy (Width & Font-Floor Adjustment)
        if (createdLabels.Count > 0)
        {
            StartCoroutine(OptimizeFreezeFittingLoop(createdLabels, targetWidths, harmonizedFontSize, blockCard, cardLeft, hMarginPx, panelW, isLandscape));
        }

        Debug.Log($"[Freeze-Frame AR] Rendered {createdLabels.Count} lines with Profile [Severity={activeProfile.severity}, Palette={activeProfile.colorPalette}, Tint={activeProfile.substrateTint}, Separators={insertSeparators}]");
    }

    private IEnumerator OptimizeFreezeFittingLoop(List<Label> labels, List<float> targetWidths, float baseFontSize, VisualElement blockCard, float cardLeftX, float hMarginPx, float maxScreenWidth, bool isLandscape)
    {
        for (int i = 0; i < labels.Count; i++)
        {
            labels[i].style.width = StyleKeyword.Auto;
        }

        yield return null;

        float maxAllowedRight = isLandscape ? (maxScreenWidth - 320f) : (maxScreenWidth - 16f);
        float actualMaxRight = cardLeftX;
        bool needsFontScaleDown = false;

        for (int i = 0; i < labels.Count && i < targetWidths.Count; i++)
        {
            Label label = labels[i];
            if (label == null) continue;

            float lineRight = label.layout.x + label.layout.width;
            if (lineRight > maxAllowedRight)
            {
                needsFontScaleDown = true;
            }
            if (lineRight > actualMaxRight)
            {
                actualMaxRight = lineRight;
            }
        }

        // Policy Step 1 & 2: Scale font down toward readable floor if overflowing boundary
        if (needsFontScaleDown)
        {
            float maxOverflowRatio = 1f;
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i] == null) continue;
                float lineRight = labels[i].layout.x + labels[i].layout.width;
                if (lineRight > maxAllowedRight)
                {
                    float avail = Mathf.Max(50f, maxAllowedRight - labels[i].layout.x);
                    float ratio = avail / Mathf.Max(50f, labels[i].layout.width);
                    if (ratio < maxOverflowRatio) maxOverflowRatio = ratio;
                }
            }

            float reducedFontSize = Mathf.Clamp(baseFontSize * maxOverflowRatio, 18f, baseFontSize);
            for (int i = 0; i < labels.Count; i++)
            {
                labels[i].style.fontSize = reducedFontSize;
            }
            yield return null;

            actualMaxRight = cardLeftX;
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i] == null) continue;
                float lineRight = labels[i].layout.x + labels[i].layout.width;
                if (lineRight > actualMaxRight) actualMaxRight = lineRight;
            }
        }

        // Policy Step 3: Expand card into free page margin up to screen / rail boundary
        if (blockCard != null && actualMaxRight > cardLeftX)
        {
            float maxCardRight = isLandscape ? (maxScreenWidth - 320f) : (maxScreenWidth - 16f);
            float requiredW = Mathf.Min(maxCardRight - cardLeftX, (actualMaxRight - cardLeftX) + (hMarginPx * 2f));
            blockCard.style.width = Mathf.Max(blockCard.style.width.value.value, requiredW);
        }
    }

    private Color SamplePaperColorFromFrame(Texture2D source, DetectedTextLine centralLine, Vector2 visionImageSize)
    {
        if (source == null || !source.isReadable) return new Color(0.98f, 0.97f, 0.95f, 0.98f);

        try
        {
            int imgW = source.width;
            int imgH = source.height;

            float scaleX = (visionImageSize.x > 0f) ? (float)imgW / visionImageSize.x : 1f;
            float scaleY = (visionImageSize.y > 0f) ? (float)imgH / visionImageSize.y : 1f;

            int xMin = Mathf.Clamp(Mathf.RoundToInt(centralLine.minX * scaleX), 0, imgW - 1);
            int xMax = Mathf.Clamp(Mathf.RoundToInt((centralLine.minX + centralLine.width) * scaleX), 0, imgW - 1);
            int yMin = Mathf.Clamp(Mathf.RoundToInt(centralLine.minY * scaleY), 0, imgH - 1);
            int yMax = Mathf.Clamp(Mathf.RoundToInt((centralLine.minY + centralLine.height) * scaleY), 0, imgH - 1);

            int marginY = Mathf.Max(6, Mathf.RoundToInt((yMax - yMin) * 0.5f));
            yMin = Mathf.Clamp(yMin - marginY, 0, imgH - 1);
            yMax = Mathf.Clamp(yMax + marginY, 0, imgH - 1);

            int width = xMax - xMin;
            int height = yMax - yMin;
            if (width <= 0 || height <= 0) return new Color(0.98f, 0.97f, 0.95f, 0.98f);

            int strideX = Mathf.Max(2, width / 30);
            int strideY = Mathf.Max(2, height / 15);
            List<Color> samples = new List<Color>(256);

            for (int y = yMin; y <= yMax; y += strideY)
            {
                int texY = Mathf.Clamp(imgH - 1 - y, 0, imgH - 1);
                for (int x = xMin; x <= xMax; x += strideX)
                {
                    samples.Add(source.GetPixel(x, texY));
                }
            }

            if (samples.Count == 0) return new Color(0.98f, 0.97f, 0.95f, 0.98f);

            samples.Sort((a, b) =>
            {
                float lumA = (0.299f * a.r) + (0.587f * a.g) + (0.114f * a.b);
                float lumB = (0.299f * b.r) + (0.587f * b.g) + (0.114f * b.b);
                return lumA.CompareTo(lumB);
            });

            int idx = Mathf.Clamp((int)(samples.Count * 0.85f), 0, samples.Count - 1);
            Color paper = samples[idx];
            float lum = (0.299f * paper.r) + (0.587f * paper.g) + (0.114f * paper.b);

            // Guarantee a bright, legible paper substrate floor (>0.92) so dark text is always crystal clear
            if (lum < 0.90f)
            {
                float boost = 0.95f / Mathf.Max(0.08f, lum);
                float r = Mathf.Clamp01(paper.r * boost);
                float g = Mathf.Clamp01(paper.g * boost);
                float b = Mathf.Clamp01(paper.b * boost);
                return new Color(r, g, b, 0.98f);
            }

            return new Color(paper.r, paper.g, paper.b, 0.98f);
        }
        catch (Exception)
        {
            return new Color(0.98f, 0.97f, 0.95f, 0.98f);
        }
    }

    private struct FreezeLineLayoutData
    {
        public string text;
        public float screenX;
        public float screenY;
        public float width;
        public float height;
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
            if (shutterIcon != null) shutterIcon.text = "• • •";
        }
        else if (text.Contains("Rescan"))
        {
            shutterButton.RemoveFromClassList("shutter-core--scanning");
            shutterButton.AddToClassList("shutter-core--rescan");
            if (shutterIcon != null) shutterIcon.text = "पुन्हा";
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
            quickRescanBtn.SetEnabled(true);
    }

    // ══════════════════════════════════════════════════════════════════════
    // 2D Focus Reader: Cleanly Isolated Modal Controller
    // ══════════════════════════════════════════════════════════════════════

    private void InitializeFocusReaderElements()
    {
        if (rootElement == null) return;

        focusReaderOverlay = rootElement.Q<VisualElement>("FocusReaderOverlay");
        if (focusReaderOverlay == null) return;

        focusReaderSlateCard = focusReaderOverlay.Q<VisualElement>("FocusReaderSlateCard");
        focusReaderLinesContainer = focusReaderOverlay.Q<VisualElement>("FocusReaderLinesContainer");
        focusAudioToggleBtn = focusReaderOverlay.Q<Button>("FocusAudioToggleBtn");
        focusAudioIcon = focusReaderOverlay.Q<Label>("FocusAudioIcon");
        focusAudioLabel = focusReaderOverlay.Q<Label>("FocusAudioLabel");
        focusRevealAllBtn = focusReaderOverlay.Q<Button>("FocusRevealAllBtn");
        focusRevealAllLabel = focusReaderOverlay.Q<Label>("FocusRevealAllLabel");
        focusCloseBtn = focusReaderOverlay.Q<Button>("FocusCloseBtn");
        focusPrevLineBtn = focusReaderOverlay.Q<Button>("FocusPrevLineBtn");
        focusPrevLineLabel = focusReaderOverlay.Q<Label>("FocusPrevLineLabel");
        focusNextLineBtn = focusReaderOverlay.Q<Button>("FocusNextLineBtn");
        focusNextLineLabel = focusReaderOverlay.Q<Label>("FocusNextLineLabel");
        focusReplayAudioBtn = focusReaderOverlay.Q<Button>("FocusReplayAudioBtn");
        focusLineProgress = focusReaderOverlay.Q<Label>("FocusLineProgress");

        ApplyDyslexiaFont(focusReaderOverlay.Q<Label>("FocusReaderTitle"));
        ApplyDyslexiaFont(focusReaderOverlay.Q<Label>("FocusReaderSubtitle"));
        ApplyDyslexiaFont(focusAudioLabel);
        ApplyDyslexiaFont(focusRevealAllLabel);
        ApplyDyslexiaFont(focusPrevLineLabel);
        ApplyDyslexiaFont(focusNextLineLabel);
        ApplyDyslexiaFont(focusLineProgress);
        if (focusReplayAudioBtn != null)
        {
            ApplyDyslexiaFont(focusReplayAudioBtn.Q<Label>("FocusReplayAudioLabel"));
        }

        if (focusCloseBtn != null)
        {
            focusCloseBtn.clicked -= CloseFocusReader;
            focusCloseBtn.clicked += CloseFocusReader;
        }

        if (focusAudioToggleBtn != null)
        {
            focusAudioToggleBtn.clicked -= ToggleFocusAudio;
            focusAudioToggleBtn.clicked += ToggleFocusAudio;
        }

        if (focusRevealAllBtn != null)
        {
            focusRevealAllBtn.clicked -= ToggleFocusRevealAll;
            focusRevealAllBtn.clicked += ToggleFocusRevealAll;
        }

        if (focusPrevLineBtn != null)
        {
            focusPrevLineBtn.clicked -= StepFocusPrevLine;
            focusPrevLineBtn.clicked += StepFocusPrevLine;
        }

        if (focusNextLineBtn != null)
        {
            focusNextLineBtn.clicked -= StepFocusNextLine;
            focusNextLineBtn.clicked += StepFocusNextLine;
        }

        if (focusReplayAudioBtn != null)
        {
            focusReplayAudioBtn.clicked -= ReplayFocusAudio;
            focusReplayAudioBtn.clicked += ReplayFocusAudio;
        }

        // Default to hidden
        focusReaderOverlay.style.display = DisplayStyle.None;
    }

    /// <summary>
    /// Opens the 2D Focus Reader in Landscape with the provided chapter text lines.
    /// Completely hides the AR HUD so no elements bleed or intercept touches.
    /// </summary>
    public void OpenFocusReader(List<string> lines)
    {
        if (lines == null || lines.Count == 0)
        {
            Debug.LogWarning("[ARLineOverlayController] Cannot open Focus Reader: lines list is empty.");
            return;
        }

        if (focusReaderOverlay == null)
        {
            InitializeFocusReaderElements();
        }

        cachedFocusLines = new List<string>(lines);
        IsFocusReaderOpen = true;
        focusWindowStartIndex = 0;
        focusActiveLineIndex = 0;
        isFocusRevealAll = false;
        focusRevealedLineIndices.Clear();
        focusRevealedLineIndices.Add(0); // Reveal the first line initially so it's ready to read

        MarathiTTSHelper.Stop();

        // 1. Completely hide the underlying AR HUD
        VisualElement hudOverlay = rootElement != null ? rootElement.Q<VisualElement>("HUDOverlay") : null;
        if (hudOverlay != null)
        {
            hudOverlay.style.display = DisplayStyle.None;
        }

        // 2. Display the isolated Focus Reader modal
        if (focusReaderOverlay != null)
        {
            focusReaderOverlay.style.display = DisplayStyle.Flex;
            focusReaderOverlay.BringToFront();
        }

        // 3. Apply calibrated dyslexia substrate color to slate card
        if (focusReaderSlateCard != null)
        {
            var profileMgr = DyslexiaProfileManager.Instance;
            if (profileMgr != null)
            {
                Color slatePaperColor = profileMgr.Profile.GetSubstrateColor(new Color(0.98f, 0.97f, 0.94f, 1f));
                focusReaderSlateCard.style.backgroundColor = new StyleColor(slatePaperColor);
            }
        }

        UpdateFocusAudioButtonUI();
        if (focusRevealAllLabel != null) focusRevealAllLabel.text = "सर्व दाखवा";

        RenderFocusWindow();
        Debug.Log($"[ARLineOverlayController] 2D Focus Reader opened cleanly with {cachedFocusLines.Count} lines.");
    }

    /// <summary>
    /// Closes the 2D Focus Reader and returns to the Freeze & Frame AR screen cleanly.
    /// </summary>
    public void CloseFocusReader()
    {
        IsFocusReaderOpen = false;
        MarathiTTSHelper.Stop();

        if (focusReaderOverlay != null)
        {
            focusReaderOverlay.style.display = DisplayStyle.None;
        }

        // Restore the underlying AR HUD
        VisualElement hudOverlay = rootElement != null ? rootElement.Q<VisualElement>("HUDOverlay") : null;
        if (hudOverlay != null)
        {
            hudOverlay.style.display = DisplayStyle.Flex;
        }

        Debug.Log("[ARLineOverlayController] Closed 2D Focus Reader, restored AR HUD cleanly.");
    }

    public void ToggleFocusAudio()
    {
        isFocusAudioEnabled = !isFocusAudioEnabled;
        UpdateFocusAudioButtonUI();
        if (!isFocusAudioEnabled)
        {
            MarathiTTSHelper.Stop();
        }
    }

    private void UpdateFocusAudioButtonUI()
    {
        if (focusAudioToggleBtn != null)
            focusAudioToggleBtn.EnableInClassList("focus-audio-btn--muted", !isFocusAudioEnabled);

        if (focusAudioIcon != null)
            focusAudioIcon.text = isFocusAudioEnabled ? "🔊" : "🔇";

        if (focusAudioLabel != null)
            focusAudioLabel.text = isFocusAudioEnabled ? "आवाज: चालू" : "आवाज: बंद";
    }

    public void ToggleFocusRevealAll()
    {
        isFocusRevealAll = !isFocusRevealAll;
        if (focusRevealAllLabel != null)
            focusRevealAllLabel.text = isFocusRevealAll ? "पुन्हा लपवा" : "सर्व दाखवा";

        if (isFocusRevealAll && cachedFocusLines != null)
        {
            for (int i = 0; i < cachedFocusLines.Count; i++)
            {
                focusRevealedLineIndices.Add(i);
            }
        }

        RenderFocusWindow();
    }

    public void ReplayFocusAudio()
    {
        if (cachedFocusLines == null || cachedFocusLines.Count == 0) return;
        if (focusActiveLineIndex >= 0 && focusActiveLineIndex < cachedFocusLines.Count)
        {
            MarathiTTSHelper.Speak(cachedFocusLines[focusActiveLineIndex]);
        }
    }

    public void StepFocusNextLine()
    {
        if (cachedFocusLines == null || cachedFocusLines.Count == 0) return;
        int total = cachedFocusLines.Count;

        // If user is on the final line and taps to complete, cleanly finish and return to AR
        if (focusActiveLineIndex >= total - 1)
        {
            CloseFocusReader();
            return;
        }

        focusActiveLineIndex++;

        if (focusActiveLineIndex >= focusWindowStartIndex + 4)
        {
            focusWindowStartIndex = Mathf.Min(Mathf.Max(0, total - 4), focusWindowStartIndex + 1);
        }

        focusRevealedLineIndices.Add(focusActiveLineIndex);

        if (isFocusAudioEnabled)
        {
            MarathiTTSHelper.Speak(cachedFocusLines[focusActiveLineIndex]);
        }

        RenderFocusWindow();
    }

    public void StepFocusPrevLine()
    {
        if (cachedFocusLines == null || cachedFocusLines.Count == 0) return;

        if (focusActiveLineIndex > 0)
        {
            focusActiveLineIndex--;
        }

        if (focusActiveLineIndex < focusWindowStartIndex)
        {
            focusWindowStartIndex = Mathf.Max(0, focusWindowStartIndex - 1);
        }

        if (isFocusAudioEnabled)
        {
            MarathiTTSHelper.Speak(cachedFocusLines[focusActiveLineIndex]);
        }

        RenderFocusWindow();
    }

    public void RenderFocusWindow()
    {
        if (focusReaderLinesContainer == null || cachedFocusLines == null || cachedFocusLines.Count == 0) return;

        focusReaderLinesContainer.Clear();

        var profileMgr = DyslexiaProfileManager.Instance;
        DyslexiaProfile profile = profileMgr != null ? profileMgr.Profile : new DyslexiaProfile();
        Color textCol = profile.GetSyllableColorA();
        Color secondaryCol = profile.GetSyllableColorB();
        bool useAlt = profile.severity != DyslexiaSeverity.Mild;
        bool insertSeps = profile.severity == DyslexiaSeverity.Intensive;
        float letSpace = profile.GetLetterSpacingEm();
        float wordSpace = profile.GetWordSpacingMultiplier();

        int totalLines = cachedFocusLines.Count;
        int visibleCount = Mathf.Min(4, totalLines - focusWindowStartIndex);

        for (int i = 0; i < visibleCount; i++)
        {
            int lineIdx = focusWindowStartIndex + i;
            string rawLine = cachedFocusLines[lineIdx];

            VisualElement lineSlot = new VisualElement();
            lineSlot.AddToClassList("focus-kindle-line");

            bool isRevealed = focusRevealedLineIndices.Contains(lineIdx) || isFocusRevealAll;
            bool isActive = (lineIdx == focusActiveLineIndex);

            if (isRevealed)
            {
                lineSlot.AddToClassList("focus-kindle-line--revealed");
                if (isActive)
                {
                    lineSlot.AddToClassList("focus-kindle-line--active");
                }

                string formattedText = DevanagariSyllableParser.ColorizeSyllablesRichText(
                    rawLine, textCol, secondaryCol, useAlt, insertSeps, letSpace, wordSpace
                );
                Label textLabel = new Label(formattedText);
                textLabel.AddToClassList("focus-kindle-line-text");
                ApplyDyslexiaFont(textLabel);
                textLabel.style.color = new StyleColor(textCol);
                lineSlot.Add(textLabel);
            }
            else
            {
                lineSlot.AddToClassList("focus-kindle-line--masked");
                Label placeholderLabel = new Label("📖 टॅप करून वाचा  (Tap to read)");
                placeholderLabel.AddToClassList("focus-kindle-line-placeholder");
                ApplyDyslexiaFont(placeholderLabel);
                lineSlot.Add(placeholderLabel);
            }

            int capturedIdx = lineIdx;
            lineSlot.RegisterCallback<ClickEvent>(evt =>
            {
                focusActiveLineIndex = capturedIdx;
                focusRevealedLineIndices.Add(capturedIdx);

                if (isFocusAudioEnabled)
                {
                    MarathiTTSHelper.Speak(cachedFocusLines[capturedIdx]);
                }

                RenderFocusWindow();
                evt.StopPropagation();
            });

            focusReaderLinesContainer.Add(lineSlot);
        }

        if (focusLineProgress != null)
        {
            focusLineProgress.text = $"ओळ {focusActiveLineIndex + 1} / {totalLines}";
        }

        if (focusPrevLineBtn != null)
        {
            focusPrevLineBtn.SetEnabled(focusWindowStartIndex > 0 || focusActiveLineIndex > 0);
        }

        if (focusNextLineBtn != null)
        {
            bool isLastLine = (focusActiveLineIndex >= totalLines - 1);
            focusNextLineBtn.EnableInClassList("focus-nav-btn--done", isLastLine);
            focusNextLineBtn.EnableInClassList("focus-nav-btn--next", !isLastLine);

            if (focusNextLineLabel != null)
            {
                focusNextLineLabel.text = isLastLine ? "✓ वाचन पूर्ण" : "पुढील ओळ ▶";
            }
            focusNextLineBtn.SetEnabled(true);
        }

        if (focusReplayAudioBtn != null)
        {
            focusReplayAudioBtn.SetEnabled(isFocusAudioEnabled);
        }
    }
}

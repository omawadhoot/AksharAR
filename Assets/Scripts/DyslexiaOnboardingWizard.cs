using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Controls the visual, step-by-step Marathi Dyslexia Personalization and Calibration Wizard.
/// Operates directly inside the unified ARLineOverlay UIDocument panel to ensure seamless
/// rendering on HUD_Quad, zero-GC synthetic touch picking, and consistent font rendering.
/// </summary>
public class DyslexiaOnboardingWizard : MonoBehaviour
{
    [Header("Fonts")]
    [SerializeField] private Font dyslexiaFont;
    [SerializeField] private UnityEngine.TextCore.Text.FontAsset dyslexiaSdfFont;

    private UIDocument hudDocument;
    private VisualElement rootElement;
    private VisualElement modalBackdrop;
    private Button openSettingsBtn;
    private Button closeBtn;
    private Button btnBack;
    private Button btnNext;
    private Button btnSkip;
    private Label accessibleStepLabel;
    private VisualElement[] progressSegments = new VisualElement[TOTAL_STEPS];

    // Persistent Cumulative Live Preview
    private VisualElement livePreviewCard;
    private Label livePreviewLine1;
    private Label livePreviewLine2;

    // Step Pages
    private VisualElement step1Page; // Severity & Spacing
    private VisualElement step2Page; // CVD Color Palettes
    private VisualElement step3Page; // Substrate Paper Tint
    private VisualElement step4Page; // Final Live Preview

    // Step 1 Cards
    private VisualElement cardMild;
    private VisualElement cardModerate;
    private VisualElement cardIntensive;

    // Step 2 Palette Chips
    private VisualElement chipCobalt;
    private VisualElement chipAmber;
    private VisualElement chipViolet;
    private VisualElement chipMonochrome;

    // Step 3 Tint Chips
    private VisualElement chipWarmCream;
    private VisualElement chipMintIce;
    private VisualElement chipPeriwinkle;
    private VisualElement chipPaperInpaint;

    // Step 4 Review Subtitle
    private Label reviewValueSpacing, reviewValueColour, reviewValuePaper;

    private int currentStepIndex = 0; // 0: Step 1, 1: Step 2, 2: Step 3, 3: Step 4
    private const int TOTAL_STEPS = 4;

    // TTS Read-Aloud Buttons
    private Button btnTtsStep1;
    private Button btnTtsStep2;
    private Button btnTtsStep3;
    private Button btnTtsStep4;

    // Telemetry and Dwell Timing
    private float stepEnterTime = 0f;
    private float[] stepDwells = new float[TOTAL_STEPS];
    private int ttsClickCount = 0;
    private bool isOpenedFromSettings = false;
    private bool didChangeStep1 = false;
    private bool didChangeStep2 = false;
    private bool didChangeStep3 = false;

    private DyslexiaProfile draftProfile;
    private bool isInitialized = false;

    private void Awake()
    {
        EnsureDyslexiaFont();
    }

    private void Start()
    {
        InitializeWizardUI();

        // Check if user has already completed onboarding
        EnsureProfileManager();
        if (DyslexiaProfileManager.Instance != null && !DyslexiaProfileManager.Instance.Profile.hasCompletedOnboarding)
        {
            ShowWizard(0);
        }
        else
        {
            HideWizard();
        }
    }

    public void InitializeWizardUI()
    {
        if (isInitialized && rootElement != null && modalBackdrop != null) return;

        // Find the main HUD UIDocument
        var docObj = GameObject.Find("ARLineOverlayDocument");
        if (docObj != null)
        {
            hudDocument = docObj.GetComponent<UIDocument>();
        }
        if (hudDocument == null)
        {
            hudDocument = FindFirstObjectByType<UIDocument>();
        }

        if (hudDocument == null || hudDocument.rootVisualElement == null)
        {
            Debug.LogWarning("[DyslexiaOnboardingWizard] HUD UIDocument not ready yet, will retry on next call.");
            return;
        }

        rootElement = hudDocument.rootVisualElement;
        modalBackdrop = rootElement.Q<VisualElement>("OnboardingModalBackdrop");

        if (modalBackdrop == null)
        {
            Debug.LogWarning("[DyslexiaOnboardingWizard] OnboardingModalBackdrop element not found in root visual tree!");
            return;
        }

        closeBtn = rootElement.Q<Button>("WizardCloseButton");
        btnBack = rootElement.Q<Button>("WizardBackButton");
        btnNext = rootElement.Q<Button>("WizardNextButton");
        btnSkip = rootElement.Q<Button>("WizardSkipButton");
        accessibleStepLabel = rootElement.Q<Label>("WizardStepAccessibleLabel");

        progressSegments[0] = rootElement.Q<VisualElement>("ProgressSeg1");
        progressSegments[1] = rootElement.Q<VisualElement>("ProgressSeg2");
        progressSegments[2] = rootElement.Q<VisualElement>("ProgressSeg3");
        progressSegments[3] = rootElement.Q<VisualElement>("ProgressSeg4");

        // Persistent Cumulative Live Preview
        livePreviewCard = rootElement.Q<VisualElement>("WizardLivePreviewCard");
        livePreviewLine1 = rootElement.Q<Label>("PreviewLiveLine1");
        livePreviewLine2 = rootElement.Q<Label>("PreviewLiveLine2");

        step1Page = rootElement.Q<VisualElement>("WizardStep1");
        step2Page = rootElement.Q<VisualElement>("WizardStep2");
        step3Page = rootElement.Q<VisualElement>("WizardStep3");
        step4Page = rootElement.Q<VisualElement>("WizardStep4");

        // Step 1
        cardMild = rootElement.Q<VisualElement>("CardSeverityMild");
        cardModerate = rootElement.Q<VisualElement>("CardSeverityModerate");
        cardIntensive = rootElement.Q<VisualElement>("CardSeverityIntensive");

        // Step 2
        chipCobalt = rootElement.Q<VisualElement>("ChipPaletteCobalt");
        chipAmber = rootElement.Q<VisualElement>("ChipPaletteAmber");
        chipViolet = rootElement.Q<VisualElement>("ChipPaletteViolet");
        chipMonochrome = rootElement.Q<VisualElement>("ChipPaletteMonochrome");

        // Step 3
        chipWarmCream = rootElement.Q<VisualElement>("ChipTintCream");
        chipMintIce = rootElement.Q<VisualElement>("ChipTintMint");
        chipPeriwinkle = rootElement.Q<VisualElement>("ChipTintPeriwinkle");
        chipPaperInpaint = rootElement.Q<VisualElement>("ChipTintPaper");

        // Step 4
        reviewValueSpacing = rootElement.Q<Label>("ReviewValueSpacing");
        reviewValueColour = rootElement.Q<Label>("ReviewValueColour");
        reviewValuePaper = rootElement.Q<Label>("ReviewValuePaper");
        BindReviewRow("ReviewRowSpacing", 0);
        BindReviewRow("ReviewRowColour", 1);
        BindReviewRow("ReviewRowPaper", 2);

        // TTS Buttons
        btnTtsStep1 = rootElement.Q<Button>("BtnTtsStep1");
        btnTtsStep2 = rootElement.Q<Button>("BtnTtsStep2");
        btnTtsStep3 = rootElement.Q<Button>("BtnTtsStep3");
        btnTtsStep4 = rootElement.Q<Button>("BtnTtsStep4");

        ApplyDyslexiaFontsToWizard();
        BindWizardEvents();
        BindHUDTriggerButton();

        isInitialized = true;
        Debug.Log("[DyslexiaOnboardingWizard] Successfully initialized wizard UI inside unified HUD document.");
    }

    private void BindHUDTriggerButton()
    {
        if (rootElement == null) return;
        openSettingsBtn = rootElement.Q<Button>("AccessibilitySettingsButton");
        if (openSettingsBtn != null)
        {
            openSettingsBtn.clicked -= OnSettingsButtonClicked;
            openSettingsBtn.clicked += OnSettingsButtonClicked;
            openSettingsBtn.RegisterCallback<PointerDownEvent>(evt =>
            {
                OnSettingsButtonClicked();
                evt.StopPropagation();
            });
        }
    }

    private void OnSettingsButtonClicked()
    {
        Debug.Log("[DyslexiaOnboardingWizard] Settings button tapped -> Opening Onboarding Wizard.");
        isOpenedFromSettings = true;
        ShowWizard(0);
    }

    private void EnsureProfileManager()
    {
        if (DyslexiaProfileManager.Instance == null)
        {
            var existing = FindFirstObjectByType<DyslexiaProfileManager>();
            if (existing == null)
            {
                var mgrObj = new GameObject("DyslexiaProfileManager");
                mgrObj.AddComponent<DyslexiaProfileManager>();
            }
        }
    }

    private void ApplyDyslexiaFontsToWizard()
    {
        EnsureDyslexiaFont();
        if (modalBackdrop == null) return;

        StyleFontDefinition fontDef;
        if (dyslexiaSdfFont != null)
            fontDef = new StyleFontDefinition(FontDefinition.FromSDFFont(dyslexiaSdfFont));
        else if (dyslexiaFont != null)
            fontDef = new StyleFontDefinition(FontDefinition.FromFont(dyslexiaFont));
        else
            return;

        var labels = modalBackdrop.Query<Label>().ToList();
        foreach (var lbl in labels)
        {
            lbl.style.unityTextGenerator = TextGeneratorType.Advanced;
            lbl.style.unityFontDefinition = fontDef;
            if (dyslexiaFont != null)
            {
                lbl.style.unityFont = new StyleFont(dyslexiaFont);
            }
        }

        var buttons = modalBackdrop.Query<Button>().ToList();
        foreach (var btn in buttons)
        {
            btn.style.unityTextGenerator = TextGeneratorType.Advanced;
            btn.style.unityFontDefinition = fontDef;
            if (dyslexiaFont != null)
            {
                btn.style.unityFont = new StyleFont(dyslexiaFont);
            }
        }
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

    private void BindWizardEvents()
    {
        if (closeBtn != null)
        {
            closeBtn.clicked -= HideWizard;
            closeBtn.clicked += HideWizard;
            closeBtn.RegisterCallback<PointerDownEvent>(evt => { HideWizard(); evt.StopPropagation(); });
        }

        if (btnBack != null)
        {
            btnBack.clicked -= OnBackClicked;
            btnBack.clicked += OnBackClicked;
            btnBack.RegisterCallback<PointerDownEvent>(evt => { OnBackClicked(); evt.StopPropagation(); });
        }

        if (btnNext != null)
        {
            btnNext.clicked -= OnNextClicked;
            btnNext.clicked += OnNextClicked;
            btnNext.RegisterCallback<PointerDownEvent>(evt => { OnNextClicked(); evt.StopPropagation(); });
        }

        if (btnSkip != null)
        {
            btnSkip.clicked -= OnSkipClicked;
            btnSkip.clicked += OnSkipClicked;
            btnSkip.RegisterCallback<PointerDownEvent>(evt => { OnSkipClicked(); evt.StopPropagation(); });
        }

        // TTS Read-Aloud Bindings
        BindTTSButton(btnTtsStep1, "तुम्हाला कोणते वाचन अंतर योग्य वाटते?");
        BindTTSButton(btnTtsStep2, "कोणता अक्षरांचा रंग वाचायला सोपा वाटतो?");
        BindTTSButton(btnTtsStep3, "कोणता कागदाचा रंग वाचायला सोपा वाटतो?");
        BindTTSButton(btnTtsStep4, "वाचन पूर्वावलोकन. तुमची वाचन रचना तयार आहे.");

        // Step 1: Spacing selection
        BindCardSelection(cardMild, () =>
        {
            EnsureDraftProfile();
            draftProfile.severity = DyslexiaSeverity.Mild;
            didChangeStep1 = true;
            UpdateStep1Selections();
            UpdatePersistentLivePreview();
        });
        BindCardSelection(cardModerate, () =>
        {
            EnsureDraftProfile();
            draftProfile.severity = DyslexiaSeverity.Moderate;
            didChangeStep1 = true;
            UpdateStep1Selections();
            UpdatePersistentLivePreview();
        });
        BindCardSelection(cardIntensive, () =>
        {
            EnsureDraftProfile();
            draftProfile.severity = DyslexiaSeverity.Intensive;
            didChangeStep1 = true;
            UpdateStep1Selections();
            UpdatePersistentLivePreview();
        });

        // Step 2: CVD Palettes
        BindCardSelection(chipCobalt, () =>
        {
            EnsureDraftProfile();
            draftProfile.colorPalette = ColorBlindPalette.ClassicCobalt;
            didChangeStep2 = true;
            UpdateStep2Selections();
            UpdatePersistentLivePreview();
        });
        BindCardSelection(chipAmber, () =>
        {
            EnsureDraftProfile();
            draftProfile.colorPalette = ColorBlindPalette.WarmAmber;
            didChangeStep2 = true;
            UpdateStep2Selections();
            UpdatePersistentLivePreview();
        });
        BindCardSelection(chipViolet, () =>
        {
            EnsureDraftProfile();
            draftProfile.colorPalette = ColorBlindPalette.VioletIris;
            didChangeStep2 = true;
            UpdateStep2Selections();
            UpdatePersistentLivePreview();
        });
        BindCardSelection(chipMonochrome, () =>
        {
            EnsureDraftProfile();
            draftProfile.colorPalette = ColorBlindPalette.Monochrome;
            didChangeStep2 = true;
            UpdateStep2Selections();
            UpdatePersistentLivePreview();
        });

        // Step 3: Substrates
        BindCardSelection(chipWarmCream, () =>
        {
            EnsureDraftProfile();
            draftProfile.substrateTint = SubstrateTint.WarmCream;
            didChangeStep3 = true;
            UpdateStep3Selections();
            UpdatePersistentLivePreview();
        });
        BindCardSelection(chipMintIce, () =>
        {
            EnsureDraftProfile();
            draftProfile.substrateTint = SubstrateTint.MintIce;
            didChangeStep3 = true;
            UpdateStep3Selections();
            UpdatePersistentLivePreview();
        });
        BindCardSelection(chipPeriwinkle, () =>
        {
            EnsureDraftProfile();
            draftProfile.substrateTint = SubstrateTint.Periwinkle;
            didChangeStep3 = true;
            UpdateStep3Selections();
            UpdatePersistentLivePreview();
        });
        BindCardSelection(chipPaperInpaint, () =>
        {
            EnsureDraftProfile();
            draftProfile.substrateTint = SubstrateTint.InpaintedPaper;
            didChangeStep3 = true;
            UpdateStep3Selections();
            UpdatePersistentLivePreview();
        });
    }

    private void BindTTSButton(Button btn, string marathiText)
    {
        if (btn == null) return;
        btn.clicked -= () => PlayTTS(marathiText);
        btn.clicked += () => PlayTTS(marathiText);
        btn.RegisterCallback<PointerDownEvent>(evt =>
        {
            PlayTTS(marathiText);
            evt.StopPropagation();
        });
    }

    private void PlayTTS(string text)
    {
        ttsClickCount++;
        MarathiTTSHelper.Speak(text);
    }

    private void EnsureDraftProfile()
    {
        if (draftProfile == null)
        {
            EnsureProfileManager();
            if (DyslexiaProfileManager.Instance != null)
            {
                string json = JsonUtility.ToJson(DyslexiaProfileManager.Instance.Profile);
                draftProfile = JsonUtility.FromJson<DyslexiaProfile>(json);
            }
            else
            {
                draftProfile = new DyslexiaProfile();
            }
        }
    }

    private void BindCardSelection(VisualElement elem, Action onSelect)
    {
        if (elem == null) return;
        elem.RegisterCallback<PointerDownEvent>(evt =>
        {
            onSelect?.Invoke();
            evt.StopPropagation();
        });
        elem.RegisterCallback<ClickEvent>(evt =>
        {
            onSelect?.Invoke();
            evt.StopPropagation();
        });
    }

    public void ShowWizard(int startingStep = 0)
    {
        if (!isInitialized || modalBackdrop == null)
        {
            InitializeWizardUI();
        }

        EnsureDraftProfile();
        currentStepIndex = Mathf.Clamp(startingStep, 0, TOTAL_STEPS - 1);
        stepEnterTime = Time.realtimeSinceStartup;

        if (modalBackdrop != null)
        {
            modalBackdrop.style.display = DisplayStyle.Flex;
        }

        UpdateWizardPageDisplay();
        Debug.Log($"[DyslexiaOnboardingWizard] Opened Wizard at Step {currentStepIndex + 1}/{TOTAL_STEPS}");
    }

    public void HideWizard()
    {
        if (modalBackdrop != null)
        {
            modalBackdrop.style.display = DisplayStyle.None;
        }
    }

    private void BindReviewRow(string rowName, int targetStep)
    {
        var row = rootElement.Q<VisualElement>(rowName);
        if (row == null) return;
        row.RegisterCallback<ClickEvent>(evt =>
        {
            RecordStepDwellTime();
            currentStepIndex = targetStep;
            stepEnterTime = Time.realtimeSinceStartup;
            UpdateWizardPageDisplay();
        });
    }

    private void OnBackClicked()
    {
        if (currentStepIndex == 0)
        {
            OnSkipClicked();
            return;
        }

        RecordStepDwellTime();
        if (currentStepIndex > 0)
        {
            currentStepIndex--;
            stepEnterTime = Time.realtimeSinceStartup;
            UpdateWizardPageDisplay();
        }
    }

    private void OnNextClicked()
    {
        RecordStepDwellTime();
        if (currentStepIndex < TOTAL_STEPS - 1)
        {
            currentStepIndex++;
            stepEnterTime = Time.realtimeSinceStartup;
            UpdateWizardPageDisplay();
        }
        else
        {
            CommitProfileAndClose(-1);
        }
    }

    private void OnSkipClicked()
    {
        RecordStepDwellTime();
        Debug.Log($"[DyslexiaOnboardingWizard] User chose 'Skip / Use Defaults' at Step {currentStepIndex + 1}");
        // Set remaining choices to standard defaults if not explicitly modified
        EnsureDraftProfile();
        if (currentStepIndex <= 0 && !didChangeStep1) draftProfile.severity = DyslexiaSeverity.Moderate;
        if (currentStepIndex <= 1 && !didChangeStep2) draftProfile.colorPalette = ColorBlindPalette.ClassicCobalt;
        if (currentStepIndex <= 2 && !didChangeStep3) draftProfile.substrateTint = SubstrateTint.WarmCream;

        CommitProfileAndClose(currentStepIndex);
    }

    private void RecordStepDwellTime()
    {
        if (currentStepIndex >= 0 && currentStepIndex < TOTAL_STEPS)
        {
            float dwell = Time.realtimeSinceStartup - stepEnterTime;
            stepDwells[currentStepIndex] += Mathf.Max(0f, dwell);
        }
    }

    private void CommitProfileAndClose(int skippedAtStep = -1)
    {
        RecordStepDwellTime();
        EnsureProfileManager();
        if (DyslexiaProfileManager.Instance != null && draftProfile != null)
        {
            draftProfile.hasCompletedOnboarding = true;
            DyslexiaProfileManager.Instance.SetSeverity(draftProfile.severity);
            DyslexiaProfileManager.Instance.SetColorPalette(draftProfile.colorPalette);
            DyslexiaProfileManager.Instance.SetSubstrateTint(draftProfile.substrateTint);
            DyslexiaProfileManager.Instance.SetOnboardingComplete(true);
        }

        // Record telemetry
        if (isOpenedFromSettings)
        {
            CalibrationTelemetry.RecordSettingChangedLater();
        }
        else
        {
            CalibrationTelemetry.RecordSession(
                stepDwells[0], stepDwells[1], stepDwells[2], stepDwells[3],
                draftProfile.severity, draftProfile.colorPalette, draftProfile.substrateTint,
                ttsClickCount,
                skippedAtStep,
                didChangeStep1,
                didChangeStep2,
                didChangeStep3
            );
        }

        HideWizard();
        Debug.Log("[DyslexiaOnboardingWizard] Completed onboarding calibration, logged telemetry, and committed user profile.");
    }

    private void UpdateWizardPageDisplay()
    {
        if (step1Page != null) step1Page.style.display = (currentStepIndex == 0) ? DisplayStyle.Flex : DisplayStyle.None;
        if (step2Page != null) step2Page.style.display = (currentStepIndex == 1) ? DisplayStyle.Flex : DisplayStyle.None;
        if (step3Page != null) step3Page.style.display = (currentStepIndex == 2) ? DisplayStyle.Flex : DisplayStyle.None;
        if (step4Page != null) step4Page.style.display = (currentStepIndex == 3) ? DisplayStyle.Flex : DisplayStyle.None;

        if (accessibleStepLabel != null)
        {
            accessibleStepLabel.text = $"{currentStepIndex + 1}/{TOTAL_STEPS}";
        }

        for (int i = 0; i < TOTAL_STEPS; i++)
        {
            if (progressSegments[i] != null)
            {
                progressSegments[i].EnableInClassList("wizard-progress-seg--active", i <= currentStepIndex);
            }
        }

        if (btnBack != null)
        {
            btnBack.style.visibility = Visibility.Visible;
            btnBack.text = (currentStepIndex == 0) ? "डिफॉल्ट वापरा" : "← मागे";
        }

        if (btnNext != null)
        {
            btnNext.text = (currentStepIndex == TOTAL_STEPS - 1) ? "वाचन सुरू करा ✓" : "पुढे →";
        }

        UpdateStep1Selections();
        UpdateStep2Selections();
        UpdateStep3Selections();
        UpdatePersistentLivePreview();
    }

    private void UpdateStep1Selections()
    {
        if (draftProfile == null) return;
        SetSelectedClass(cardMild, draftProfile.severity == DyslexiaSeverity.Mild);
        SetSelectedClass(cardModerate, draftProfile.severity == DyslexiaSeverity.Moderate);
        SetSelectedClass(cardIntensive, draftProfile.severity == DyslexiaSeverity.Intensive);
    }

    private void UpdateStep2Selections()
    {
        if (draftProfile == null) return;
        SetSelectedClass(chipCobalt, draftProfile.colorPalette == ColorBlindPalette.ClassicCobalt);
        SetSelectedClass(chipAmber, draftProfile.colorPalette == ColorBlindPalette.WarmAmber);
        SetSelectedClass(chipViolet, draftProfile.colorPalette == ColorBlindPalette.VioletIris);
        SetSelectedClass(chipMonochrome, draftProfile.colorPalette == ColorBlindPalette.Monochrome);
    }

    private void UpdateStep3Selections()
    {
        if (draftProfile == null) return;
        SetSelectedClass(chipWarmCream, draftProfile.substrateTint == SubstrateTint.WarmCream);
        SetSelectedClass(chipMintIce, draftProfile.substrateTint == SubstrateTint.MintIce);
        SetSelectedClass(chipPeriwinkle, draftProfile.substrateTint == SubstrateTint.Periwinkle);
        SetSelectedClass(chipPaperInpaint, draftProfile.substrateTint == SubstrateTint.InpaintedPaper);
    }

    private void UpdatePersistentLivePreview()
    {
        if (draftProfile == null) return;

        // Apply substrate tint to the live preview card
        if (livePreviewCard != null)
        {
            Color tintColor = draftProfile.GetSubstrateColor(new Color(0.98f, 0.97f, 0.95f, 1f));
            livePreviewCard.style.backgroundColor = new StyleColor(tintColor);
        }

        string line1Raw = "सुंदर फुले उमलली बागेत छान";
        string line2Raw = "हळूच वारा सांगे आनंदाचे गाण";

        Color colA = draftProfile.GetSyllableColorA();
        Color colB = draftProfile.GetSyllableColorB();
        bool useAlternating = draftProfile.colorPalette != ColorBlindPalette.Monochrome && draftProfile.severity != DyslexiaSeverity.Mild;
        bool insertSeparators = draftProfile.ShouldInsertSeparators();
        float letterSpacing = draftProfile.GetLetterSpacingEm();
        float wordSpacing = draftProfile.GetWordSpacingMultiplier();
        bool isLightBackground = draftProfile.substrateTint != SubstrateTint.InpaintedPaper;

        if (livePreviewLine1 != null)
        {
            livePreviewLine1.text = DevanagariSyllableParser.ColorizeSyllablesRichText(
                line1Raw, colA, colB, useAlternating, insertSeparators, letterSpacing, wordSpacing, isLightBackground
            );
        }

        if (livePreviewLine2 != null)
        {
            livePreviewLine2.text = DevanagariSyllableParser.ColorizeSyllablesRichText(
                line2Raw, colA, colB, useAlternating, insertSeparators, letterSpacing, wordSpacing, isLightBackground
            );
        }

        if (reviewValueSpacing != null)
        {
            string palName = draftProfile.colorPalette switch
            {
                ColorBlindPalette.ClassicCobalt => "निळा रंग",
                ColorBlindPalette.WarmAmber => "सोनेरी रंग",
                ColorBlindPalette.VioletIris => "जांभळा रंग",
                _ => "साधा / काळा रंग"
            };

            string sevName = draftProfile.severity switch
            {
                DyslexiaSeverity.Mild => "कमी अंतर",
                DyslexiaSeverity.Moderate => "मध्यम अंतर",
                _ => "जास्त अंतर"
            };

            string tintName = draftProfile.substrateTint switch
            {
                SubstrateTint.WarmCream => "क्रीमी कागद",
                SubstrateTint.MintIce => "हलका हिरवा",
                SubstrateTint.Periwinkle => "हलका निळा",
                _ => "नैसर्गिक कागद"
            };

            if (reviewValueSpacing != null) reviewValueSpacing.text = sevName;
            if (reviewValueColour != null) reviewValueColour.text = palName;
            if (reviewValuePaper != null) reviewValuePaper.text = tintName;
        }
    }

    private void SetSelectedClass(VisualElement elem, bool selected)
    {
        if (elem == null) return;
        elem.EnableInClassList("wizard-card--selected", selected);
        elem.EnableInClassList("wizard-chip--selected", selected);
    }
}

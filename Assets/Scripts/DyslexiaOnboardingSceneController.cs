using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Dedicated controller for the standalone 2D Screen-Space Onboarding & Dyslexia Calibration Scene.
/// Operates with direct native UI Toolkit pointer/touch events (no 3D quad or synthetic event injection).
/// Features full-screen responsive layout, 4-segment progress bar, persistent live preview, and skip support.
/// Transitions to 'Index' (AR Scene) upon completion.
/// </summary>
public class DyslexiaOnboardingSceneController : MonoBehaviour
{
    [Header("UI Document Reference")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Target AR Scene Name")]
    [SerializeField] private string targetSceneName = "Index";

    private VisualElement rootElement;
    private VisualElement modalBackdrop;
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
    private Label previewSubtitle;

    private int currentStepIndex = 0;
    private const int TOTAL_STEPS = 4;

    // TTS Buttons
    private Button btnTtsStep1;
    private Button btnTtsStep2;
    private Button btnTtsStep3;
    private Button btnTtsStep4;

    // Telemetry Timing & Changes
    private float stepEnterTime = 0f;
    private float[] stepDwells = new float[TOTAL_STEPS];
    private int ttsClickCount = 0;
    private bool didChangeStep1 = false;
    private bool didChangeStep2 = false;
    private bool didChangeStep3 = false;

    private DyslexiaProfile draftProfile;
    private UnityEngine.TextCore.Text.FontAsset dyslexiaSdfFont;

    private void Awake()
    {
        EnsureProfileManager();
        LoadDyslexiaSdfFont();
    }

    private void Start()
    {
        InitializeUI();
    }

    private void LoadDyslexiaSdfFont()
    {
        dyslexiaSdfFont = Resources.Load<UnityEngine.TextCore.Text.FontAsset>("Fonts/NeevA-Dyslexia-Regular SDF");
        if (dyslexiaSdfFont == null)
        {
            dyslexiaSdfFont = Resources.Load<UnityEngine.TextCore.Text.FontAsset>("NeevA-Dyslexia-Regular SDF");
        }
#if UNITY_EDITOR
        if (dyslexiaSdfFont == null)
        {
            dyslexiaSdfFont = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>("Assets/Resources/Fonts/NeevA-Dyslexia-Regular SDF.asset");
        }
        if (dyslexiaSdfFont == null)
        {
            dyslexiaSdfFont = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>("Assets/Fonts/NeevA-Dyslexia-Regular SDF.asset");
        }
#endif
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

    public void InitializeUI()
    {
        if (uiDocument == null)
        {
            uiDocument = GetComponent<UIDocument>();
        }
        if (uiDocument == null)
        {
            uiDocument = FindFirstObjectByType<UIDocument>();
        }

        if (uiDocument == null || uiDocument.rootVisualElement == null)
        {
            Debug.LogError("[DyslexiaOnboardingSceneController] UIDocument or root visual element not found!");
            return;
        }

        rootElement = uiDocument.rootVisualElement;
        modalBackdrop = rootElement.Q<VisualElement>("OnboardingModalBackdrop");

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
        previewSubtitle = rootElement.Q<Label>("WizardPreviewSubtitle");

        // TTS Buttons
        btnTtsStep1 = rootElement.Q<Button>("BtnTtsStep1");
        btnTtsStep2 = rootElement.Q<Button>("BtnTtsStep2");
        btnTtsStep3 = rootElement.Q<Button>("BtnTtsStep3");
        btnTtsStep4 = rootElement.Q<Button>("BtnTtsStep4");

        ApplyFonts();
        BindEvents();
        EnsureDraftProfile();
        ShowStep(0);

        Debug.Log("[DyslexiaOnboardingSceneController] Native 2D Screen-Space Onboarding UI ready.");
    }

    private void ApplyFonts()
    {
        if (dyslexiaSdfFont == null || rootElement == null) return;

        var fontDef = new StyleFontDefinition(FontDefinition.FromSDFFont(dyslexiaSdfFont));

        var labels = rootElement.Query<Label>().ToList();
        foreach (var l in labels)
        {
            l.style.unityTextGenerator = TextGeneratorType.Advanced;
            l.style.unityFontDefinition = fontDef;
        }

        var buttons = rootElement.Query<Button>().ToList();
        foreach (var b in buttons)
        {
            b.style.unityTextGenerator = TextGeneratorType.Advanced;
            b.style.unityFontDefinition = fontDef;
        }
    }

    private void EnsureDraftProfile()
    {
        if (draftProfile == null)
        {
            if (DyslexiaProfileManager.Instance != null && DyslexiaProfileManager.Instance.Profile != null)
            {
                var p = DyslexiaProfileManager.Instance.Profile;
                draftProfile = new DyslexiaProfile
                {
                    severity = p.severity,
                    colorPalette = p.colorPalette,
                    substrateTint = p.substrateTint,
                    fontScaleMultiplier = p.fontScaleMultiplier,
                    enableSyllableSegmentation = p.enableSyllableSegmentation,
                    hasCompletedOnboarding = p.hasCompletedOnboarding
                };
            }
            else
            {
                draftProfile = new DyslexiaProfile();
            }
        }
    }

    private void BindEvents()
    {
        if (closeBtn != null)
        {
            closeBtn.clicked -= OnCloseOrFinishClicked;
            closeBtn.clicked += OnCloseOrFinishClicked;
        }

        if (btnBack != null)
        {
            btnBack.clicked -= OnBackClicked;
            btnBack.clicked += OnBackClicked;
        }

        if (btnNext != null)
        {
            btnNext.clicked -= OnNextClicked;
            btnNext.clicked += OnNextClicked;
        }

        if (btnSkip != null)
        {
            btnSkip.clicked -= OnSkipClicked;
            btnSkip.clicked += OnSkipClicked;
        }

        // TTS Read-Aloud Bindings
        BindTTSButton(btnTtsStep1, "तुम्हाला कोणते वाचन अंतर योग्य वाटते?");
        BindTTSButton(btnTtsStep2, "कोणता अक्षरांचा रंग वाचायला सोपा वाटतो?");
        BindTTSButton(btnTtsStep3, "कोणता कागदाचा रंग वाचायला सोपा वाटतो?");
        BindTTSButton(btnTtsStep4, "वाचन पूर्वावलोकन. तुमची वाचन रचना तयार आहे.");

        // Step 1 Severity Bindings
        BindCard(cardMild, () => SelectSeverity(DyslexiaSeverity.Mild));
        BindCard(cardModerate, () => SelectSeverity(DyslexiaSeverity.Moderate));
        BindCard(cardIntensive, () => SelectSeverity(DyslexiaSeverity.Intensive));

        // Step 2 CVD Palette Bindings
        BindCard(chipCobalt, () => SelectPalette(ColorBlindPalette.ClassicCobalt));
        BindCard(chipAmber, () => SelectPalette(ColorBlindPalette.WarmAmber));
        BindCard(chipViolet, () => SelectPalette(ColorBlindPalette.VioletIris));
        BindCard(chipMonochrome, () => SelectPalette(ColorBlindPalette.Monochrome));

        // Step 3 Substrate Paper Tint Bindings
        BindCard(chipWarmCream, () => SelectSubstrate(SubstrateTint.WarmCream));
        BindCard(chipMintIce, () => SelectSubstrate(SubstrateTint.MintIce));
        BindCard(chipPeriwinkle, () => SelectSubstrate(SubstrateTint.Periwinkle));
        BindCard(chipPaperInpaint, () => SelectSubstrate(SubstrateTint.InpaintedPaper));
    }

    private void BindTTSButton(Button btn, string marathiText)
    {
        if (btn == null) return;
        btn.clicked -= () => PlayTTS(marathiText);
        btn.clicked += () => PlayTTS(marathiText);
    }

    private void PlayTTS(string text)
    {
        ttsClickCount++;
        MarathiTTSHelper.Speak(text);
    }

    private void BindCard(VisualElement card, Action onSelect)
    {
        if (card == null) return;
        card.RegisterCallback<ClickEvent>(evt =>
        {
            onSelect?.Invoke();
            evt.StopPropagation();
        });
    }

    public void ShowStep(int stepIndex)
    {
        RecordStepDwellTime();
        currentStepIndex = Mathf.Clamp(stepIndex, 0, TOTAL_STEPS - 1);
        stepEnterTime = Time.realtimeSinceStartup;

        if (step1Page != null) step1Page.style.display = currentStepIndex == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        if (step2Page != null) step2Page.style.display = currentStepIndex == 1 ? DisplayStyle.Flex : DisplayStyle.None;
        if (step3Page != null) step3Page.style.display = currentStepIndex == 2 ? DisplayStyle.Flex : DisplayStyle.None;
        if (step4Page != null) step4Page.style.display = currentStepIndex == 3 ? DisplayStyle.Flex : DisplayStyle.None;

        if (accessibleStepLabel != null)
        {
            accessibleStepLabel.text = $"Step {currentStepIndex + 1} of {TOTAL_STEPS}";
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
            btnBack.style.visibility = currentStepIndex == 0 ? Visibility.Hidden : Visibility.Visible;
        }

        if (btnNext != null)
        {
            btnNext.text = currentStepIndex == TOTAL_STEPS - 1 ? "वाचन सुरू करा ✓" : "पुढे →";
        }

        UpdateVisualSelections();
        UpdatePersistentLivePreview();
    }

    private void RecordStepDwellTime()
    {
        if (currentStepIndex >= 0 && currentStepIndex < TOTAL_STEPS)
        {
            float dwell = Time.realtimeSinceStartup - stepEnterTime;
            stepDwells[currentStepIndex] += Mathf.Max(0f, dwell);
        }
    }

    private void UpdateVisualSelections()
    {
        EnsureDraftProfile();

        // Step 1
        SetCardSelected(cardMild, draftProfile.severity == DyslexiaSeverity.Mild);
        SetCardSelected(cardModerate, draftProfile.severity == DyslexiaSeverity.Moderate);
        SetCardSelected(cardIntensive, draftProfile.severity == DyslexiaSeverity.Intensive);

        // Step 2
        SetCardSelected(chipCobalt, draftProfile.colorPalette == ColorBlindPalette.ClassicCobalt);
        SetCardSelected(chipAmber, draftProfile.colorPalette == ColorBlindPalette.WarmAmber);
        SetCardSelected(chipViolet, draftProfile.colorPalette == ColorBlindPalette.VioletIris);
        SetCardSelected(chipMonochrome, draftProfile.colorPalette == ColorBlindPalette.Monochrome);

        // Step 3
        SetCardSelected(chipWarmCream, draftProfile.substrateTint == SubstrateTint.WarmCream);
        SetCardSelected(chipMintIce, draftProfile.substrateTint == SubstrateTint.MintIce);
        SetCardSelected(chipPeriwinkle, draftProfile.substrateTint == SubstrateTint.Periwinkle);
        SetCardSelected(chipPaperInpaint, draftProfile.substrateTint == SubstrateTint.InpaintedPaper);
    }

    private void SetCardSelected(VisualElement card, bool selected)
    {
        if (card == null) return;
        if (selected)
        {
            card.AddToClassList("wizard-card--selected");
        }
        else
        {
            card.RemoveFromClassList("wizard-card--selected");
        }
    }

    private void SelectSeverity(DyslexiaSeverity severity)
    {
        EnsureDraftProfile();
        draftProfile.severity = severity;
        didChangeStep1 = true;
        UpdateVisualSelections();
        UpdatePersistentLivePreview();
    }

    private void SelectPalette(ColorBlindPalette palette)
    {
        EnsureDraftProfile();
        draftProfile.colorPalette = palette;
        didChangeStep2 = true;
        UpdateVisualSelections();
        UpdatePersistentLivePreview();
    }

    private void SelectSubstrate(SubstrateTint tint)
    {
        EnsureDraftProfile();
        draftProfile.substrateTint = tint;
        didChangeStep3 = true;
        UpdateVisualSelections();
        UpdatePersistentLivePreview();
    }

    private void UpdatePersistentLivePreview()
    {
        if (draftProfile == null) return;

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

        if (previewSubtitle != null)
        {
            string palName = draftProfile.colorPalette switch
            {
                ColorBlindPalette.ClassicCobalt => "निळा रंग (Cobalt)",
                ColorBlindPalette.WarmAmber => "सोनेरी रंग (Amber)",
                ColorBlindPalette.VioletIris => "जांभळा रंग (Violet)",
                _ => "काळा रंग (None)"
            };

            string sevName = draftProfile.severity switch
            {
                DyslexiaSeverity.Mild => "कमी अंतर (Compact)",
                DyslexiaSeverity.Moderate => "मध्यम अंतर (Default)",
                _ => "जास्त अंतर (Expanded)"
            };

            string tintName = draftProfile.substrateTint switch
            {
                SubstrateTint.WarmCream => "क्रीमी (Warm Cream)",
                SubstrateTint.MintIce => "हलका हिरवा (Mint)",
                SubstrateTint.Periwinkle => "हलका निळा (Soft Blue)",
                _ => "नैसर्गिक कागद (Natural)"
            };

            previewSubtitle.text = $"निवड: {sevName} • {palName} • {tintName}";
        }
    }

    private void OnBackClicked()
    {
        if (currentStepIndex > 0)
        {
            ShowStep(currentStepIndex - 1);
        }
    }

    private void OnNextClicked()
    {
        if (currentStepIndex < TOTAL_STEPS - 1)
        {
            ShowStep(currentStepIndex + 1);
        }
        else
        {
            FinishAndTransition(-1);
        }
    }

    private void OnSkipClicked()
    {
        RecordStepDwellTime();
        Debug.Log($"[DyslexiaOnboardingSceneController] User chose 'Skip / Use Defaults' at Step {currentStepIndex + 1}");
        EnsureDraftProfile();
        if (currentStepIndex <= 0 && !didChangeStep1) draftProfile.severity = DyslexiaSeverity.Moderate;
        if (currentStepIndex <= 1 && !didChangeStep2) draftProfile.colorPalette = ColorBlindPalette.ClassicCobalt;
        if (currentStepIndex <= 2 && !didChangeStep3) draftProfile.substrateTint = SubstrateTint.WarmCream;

        FinishAndTransition(currentStepIndex);
    }

    private void OnCloseOrFinishClicked()
    {
        FinishAndTransition(-1);
    }

    private void FinishAndTransition(int skippedAtStep)
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

        // Record telemetry from Day 1
        CalibrationTelemetry.RecordSession(
            stepDwells[0], stepDwells[1], stepDwells[2], stepDwells[3],
            draftProfile.severity, draftProfile.colorPalette, draftProfile.substrateTint,
            ttsClickCount,
            skippedAtStep,
            didChangeStep1,
            didChangeStep2,
            didChangeStep3
        );

        Debug.Log($"[DyslexiaOnboardingSceneController] Calibration completed & logged. Loading AR Scene '{targetSceneName}'...");
        SceneManager.LoadScene(targetSceneName);
    }
}

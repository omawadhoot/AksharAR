using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Controller for the Start / Welcome Launchpad Screen of AksharAR.
/// Allows young readers to:
///  1. Start the personalized 4-step dyslexia onboarding wizard ('सुरुवात करा' / Get Started)
///  2. Skip straight to the AR Scanner ('पुढे वाचा' / Continue Reading)
///  3. Practice on a digital sample page ('नमुना पुस्तक' / Demo Practice)
///  4. Listen to spoken voice instructions ('🔊 Audio Help')
///  5. Switch between Marathi / Hindi / English
/// </summary>
public class AksharStartScreenController : MonoBehaviour
{
    [Header("UI Document Reference")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Target Scene Names")]
    [SerializeField] private string onboardingSceneName = "Onboarding";
    [SerializeField] private string scannerSceneName = "Index";

    [Header("Audio Guide")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip welcomeAudioClip;

    private VisualElement rootElement;
    private Button btnLanguageSelect;
    private Label lblCurrentLanguage;
    private Button btnStartSettings;
    private Button btnAudioIntro;
    private Button btnGetStarted;
    private Button btnContinueReading;
    private Button btnDemoPractice;

    private Label lblBrandSubtitle;
    private Label lblGetStartedTitle;
    private Label lblGetStartedSub;
    private Label lblContinueReadingTitle;
    private Label lblContinueReadingSub;
    private Label lblDemoPracticeTitle;
    private Label lblDemoPracticeSub;

    private void Awake()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
        AppLanguageManager.OnLanguageChanged += OnLanguageChanged;
    }

    private void OnDisable()
    {
        AppLanguageManager.OnLanguageChanged -= OnLanguageChanged;
    }

    private void Start()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null)
        {
            Debug.LogWarning("[AksharStartScreenController] UIDocument or rootVisualElement is null.");
            return;
        }

        rootElement = uiDocument.rootVisualElement;

        btnLanguageSelect = rootElement.Q<Button>("BtnLanguageSelect");
        lblCurrentLanguage = rootElement.Q<Label>("LblCurrentLanguage");
        btnStartSettings = rootElement.Q<Button>("BtnStartSettings");
        btnAudioIntro = rootElement.Q<Button>("BtnAudioIntro");
        btnGetStarted = rootElement.Q<Button>("BtnGetStarted");
        btnContinueReading = rootElement.Q<Button>("BtnContinueReading");
        btnDemoPractice = rootElement.Q<Button>("BtnDemoPractice");

        lblBrandSubtitle = rootElement.Q<Label>("LblBrandSubtitle");
        lblGetStartedTitle = rootElement.Q<Label>("LblGetStartedTitle");
        lblGetStartedSub = rootElement.Q<Label>("LblGetStartedSub");
        lblContinueReadingTitle = rootElement.Q<Label>("LblContinueReadingTitle");
        lblContinueReadingSub = rootElement.Q<Label>("LblContinueReadingSub");
        lblDemoPracticeTitle = rootElement.Q<Label>("LblDemoPracticeTitle");
        lblDemoPracticeSub = rootElement.Q<Label>("LblDemoPracticeSub");

        if (btnGetStarted != null)
        {
            btnGetStarted.clicked += OnGetStartedClicked;
        }

        if (btnContinueReading != null)
        {
            btnContinueReading.clicked += OnContinueReadingClicked;
        }

        if (btnDemoPractice != null)
        {
            btnDemoPractice.clicked += OnDemoPracticeClicked;
        }

        if (btnAudioIntro != null)
        {
            btnAudioIntro.clicked += OnAudioIntroClicked;
        }

        if (btnLanguageSelect != null)
        {
            btnLanguageSelect.clicked += OnLanguageToggleClicked;
        }

        if (btnStartSettings != null)
        {
            btnStartSettings.clicked += OnSettingsClicked;
        }

        UpdateLocalizedTexts(AppLanguageManager.CurrentLanguage);
        Debug.Log("[AksharStartScreenController] UI successfully bound and localized.");
    }

    private void OnLanguageChanged(AppLanguage newLang)
    {
        UpdateLocalizedTexts(newLang);
    }

    private void UpdateLocalizedTexts(AppLanguage lang)
    {
        if (lblCurrentLanguage != null)
            lblCurrentLanguage.text = AppLanguageManager.LanguagePillLabel;

        if (lblBrandSubtitle != null)
            lblBrandSubtitle.text = AppLanguageManager.StartBrandSubtitle;

        if (lblGetStartedTitle != null)
            lblGetStartedTitle.text = AppLanguageManager.StartGetStartedTitle;

        if (lblGetStartedSub != null)
            lblGetStartedSub.text = AppLanguageManager.StartGetStartedSub;

        if (lblContinueReadingTitle != null)
            lblContinueReadingTitle.text = AppLanguageManager.StartContinueReadingTitle;

        if (lblContinueReadingSub != null)
            lblContinueReadingSub.text = AppLanguageManager.StartContinueReadingSub;

        if (lblDemoPracticeTitle != null)
            lblDemoPracticeTitle.text = AppLanguageManager.StartDemoPracticeTitle;

        if (lblDemoPracticeSub != null)
            lblDemoPracticeSub.text = AppLanguageManager.StartDemoPracticeSub;
    }

    private void OnGetStartedClicked()
    {
        Debug.Log("[AksharStartScreenController] Launching Dyslexia Onboarding Wizard...");
        SceneManager.LoadScene(onboardingSceneName);
    }

    private void OnContinueReadingClicked()
    {
        Debug.Log("[AksharStartScreenController] Jumping directly to AR Scanner (Index)...");
        SceneManager.LoadScene(scannerSceneName);
    }

    private void OnDemoPracticeClicked()
    {
        Debug.Log("[AksharStartScreenController] Launching Demo Practice mode...");
        SceneManager.LoadScene(scannerSceneName);
    }

    private void OnAudioIntroClicked()
    {
        Debug.Log("[AksharStartScreenController] Playing Welcome Voice Guide...");
        if (audioSource != null && welcomeAudioClip != null)
        {
            audioSource.PlayOneShot(welcomeAudioClip);
        }
        else
        {
            MarathiTTSHelper.Speak(AppLanguageManager.StartWelcomeTTS);
        }
    }

    private void OnLanguageToggleClicked()
    {
        // Instantly toggles between Marathi and Hindi with zero artificial delay
        AppLanguageManager.ToggleLanguage();
    }

    private void OnSettingsClicked()
    {
        Debug.Log("[AksharStartScreenController] Opening Accessibility Settings / Onboarding...");
        SceneManager.LoadScene(onboardingSceneName);
    }
}

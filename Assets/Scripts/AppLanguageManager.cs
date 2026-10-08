using System;
using UnityEngine;

/// <summary>
/// Supported app-wide languages.
/// Only switches between Marathi and Hindi as per app requirements.
/// </summary>
public enum AppLanguage
{
    Marathi = 0,
    Hindi = 1
}

/// <summary>
/// Centralized App-Wide Language Manager.
/// Manages language state, PlayerPrefs persistence, reactive UI updates,
/// and localized text strings across Start Screen, Onboarding, and AR Scanner.
/// Swapping happens instantaneously (sub-millisecond) without artificial loading delays.
/// </summary>
public static class AppLanguageManager
{
    private const string PREF_KEY = "AksharAR_AppLanguage";
    private static AppLanguage currentLanguage = AppLanguage.Marathi;
    private static bool isInitialized = false;

    public static event Action<AppLanguage> OnLanguageChanged;

    public static AppLanguage CurrentLanguage
    {
        get
        {
            if (!isInitialized)
            {
                Initialize();
            }
            return currentLanguage;
        }
        set
        {
            if (!isInitialized)
            {
                Initialize();
            }

            if (currentLanguage != value)
            {
                currentLanguage = value;
                PlayerPrefs.SetInt(PREF_KEY, (int)currentLanguage);
                PlayerPrefs.Save();

                MarathiTTSHelper.SetLanguage(currentLanguage);
                OnLanguageChanged?.Invoke(currentLanguage);
                Debug.Log($"[AppLanguageManager] Language switched to: {currentLanguage}");
            }
        }
    }

    private static void Initialize()
    {
        isInitialized = true;
        int saved = PlayerPrefs.GetInt(PREF_KEY, (int)AppLanguage.Marathi);
        currentLanguage = (saved == (int)AppLanguage.Hindi) ? AppLanguage.Hindi : AppLanguage.Marathi;
        MarathiTTSHelper.SetLanguage(currentLanguage);
    }

    /// <summary>
    /// Toggles strictly between Marathi and Hindi.
    /// </summary>
    public static AppLanguage ToggleLanguage()
    {
        CurrentLanguage = (CurrentLanguage == AppLanguage.Marathi) ? AppLanguage.Hindi : AppLanguage.Marathi;
        return CurrentLanguage;
    }

    // ── Localized Strings ──

    // 1. Language Pill Display
    public static string LanguagePillLabel =>
        (CurrentLanguage == AppLanguage.Marathi) ? "मराठी  ▼" : "हिंदी  ▼";

    // 2. Start Screen
    public static string StartBrandSubtitle =>
        (CurrentLanguage == AppLanguage.Marathi) ? "तुमचा जादूई वाचन सोबती" : "आपका जादुई पठन साथी";

    public static string StartGetStartedTitle =>
        (CurrentLanguage == AppLanguage.Marathi) ? "सुरुवात करा" : "शुरुआत करें";

    public static string StartGetStartedSub =>
        (CurrentLanguage == AppLanguage.Marathi) ? "तुमची वाचन रचना निवडा" : "अपनी पठन शैली चुनें";

    public static string StartContinueReadingTitle =>
        (CurrentLanguage == AppLanguage.Marathi) ? "पुढे वाचा" : "आगे पढ़ें";

    public static string StartContinueReadingSub =>
        (CurrentLanguage == AppLanguage.Marathi) ? "थेट कॅमेऱ्याने पुस्तक वाचा" : "सीधे कैमरे से किताब पढ़ें";

    public static string StartDemoPracticeTitle =>
        (CurrentLanguage == AppLanguage.Marathi) ? "नमुना पुस्तक" : "नमूना पुस्तक";

    public static string StartDemoPracticeSub =>
        (CurrentLanguage == AppLanguage.Marathi) ? "सराव करण्यासाठी उदाहरण" : "अभ्यास के लिए उदाहरण";

    public static string StartWelcomeTTS =>
        (CurrentLanguage == AppLanguage.Marathi)
            ? "अक्षर ए आर मध्ये तुमचे स्वागत आहे. वाचन सुरू करण्यासाठी हिरव्या बटनावर दाबा."
            : "अक्षर ए आर में आपका स्वागत है। पढ़ना शुरू करने के लिए हरे बटन पर दबाएं।";

    // 3. AR Overlay Modes
    public static string ModePoemDevanagari => "कविता";
    public static string ModePoemLatin => "Kavita";

    public static string ModeChapterDevanagari =>
        (CurrentLanguage == AppLanguage.Marathi) ? "धडा" : "पाठ";

    public static string ModeChapterLatin =>
        (CurrentLanguage == AppLanguage.Marathi) ? "Dhada" : "Paath";

    // 4. Onboarding Wizard Prompts
    public static string WizardSlateHeaderTitle =>
        (CurrentLanguage == AppLanguage.Marathi) ? "📝 वाचन पाटी  •  READING SLATE" : "📝 पठन पाटी  •  READING SLATE";

    public static string WizardSlateLine1 =>
        (CurrentLanguage == AppLanguage.Marathi) ? "सुंदर फुले उमलली बागेत छान" : "सुंदर फूल खिले उपवन में प्यारे";

    public static string WizardSlateLine2 =>
        (CurrentLanguage == AppLanguage.Marathi) ? "हळूच वारा सांगे आनंदाचे गाण" : "धीमी हवा सुनाए गीत न्यारे";

    public static string WizardPromptStep1 =>
        (CurrentLanguage == AppLanguage.Marathi) ? "तुम्हाला कोणते वाचन अंतर योग्य वाटते?" : "आपको कौन सा पढ़ने का अंतर सही लगता है?";

    public static string WizardPromptStep2 =>
        (CurrentLanguage == AppLanguage.Marathi) ? "कोणता अक्षरांचा रंग वाचायला सोपा वाटतो?" : "अक्षरों का कौन सा रंग पढ़ने में आसान लगता है?";

    public static string WizardPromptStep3 =>
        (CurrentLanguage == AppLanguage.Marathi) ? "कोणता कागदाचा रंग वाचायला सोपा वाटतो?" : "कागज़ का कौन सा रंग पढ़ने में आसान लगता है?";

    public static string WizardPromptStep4 =>
        (CurrentLanguage == AppLanguage.Marathi) ? "वाचन पूर्वावलोकन. तुमची वाचन रचना तयार आहे." : "पठन पूर्वावलोकन। आपकी पठन शैली तैयार है।";

    public static string WizardSeverityMild =>
        (CurrentLanguage == AppLanguage.Marathi) ? "कमी अंतर" : "कम अंतर";

    public static string WizardSeverityModerate =>
        "मध्यम अंतर";

    public static string WizardSeverityIntensive =>
        (CurrentLanguage == AppLanguage.Marathi) ? "जास्त अंतर" : "अधिक अंतर";

    public static string WizardPaletteCobalt =>
        (CurrentLanguage == AppLanguage.Marathi) ? "निळा रंग" : "नीला रंग";

    public static string WizardPaletteAmber =>
        (CurrentLanguage == AppLanguage.Marathi) ? "सोनेरी रंग" : "सुनहरा रंग";

    public static string WizardPaletteViolet =>
        (CurrentLanguage == AppLanguage.Marathi) ? "जांभळा रंग" : "बैंगनी रंग";

    public static string WizardPaletteMonochrome =>
        (CurrentLanguage == AppLanguage.Marathi) ? "साधा / काळा रंग" : "सादा / काला रंग";

    public static string WizardSubstrateCream =>
        (CurrentLanguage == AppLanguage.Marathi) ? "क्रीमी कागद" : "क्रीमी कागज़";

    public static string WizardSubstrateMint =>
        (CurrentLanguage == AppLanguage.Marathi) ? "हलका हिरवा" : "हल्का हरा";

    public static string WizardSubstratePeriwinkle =>
        (CurrentLanguage == AppLanguage.Marathi) ? "हलका निळा" : "हल्का नीला";

    public static string WizardSubstratePaper =>
        (CurrentLanguage == AppLanguage.Marathi) ? "नैसर्गिक कागद" : "प्राकृतिक कागज़";

    public static string WizardEditChip =>
        (CurrentLanguage == AppLanguage.Marathi) ? "बदला ✎" : "बदलें ✎";

    public static string WizardChangeLaterNote =>
        (CurrentLanguage == AppLanguage.Marathi)
            ? "💡 तुम्ही हे पर्याय नंतर Settings मध्ये कधीही बदलू शकता."
            : "💡 आप ये विकल्प बाद में Settings में कभी भी बदल सकते हैं।";

    public static string WizardBtnUseDefaults =>
        (CurrentLanguage == AppLanguage.Marathi) ? "डिफॉल्ट वापरा" : "डिफ़ॉल्ट उपयोग करें";

    public static string WizardBtnBack =>
        (CurrentLanguage == AppLanguage.Marathi) ? "← मागे" : "← पीछे";

    public static string WizardBtnNext =>
        (CurrentLanguage == AppLanguage.Marathi) ? "पुढे →" : "आगे →";

    public static string WizardBtnFinish =>
        (CurrentLanguage == AppLanguage.Marathi) ? "वाचन सुरू करा ✓" : "पढ़ना शुरू करें ✓";
}

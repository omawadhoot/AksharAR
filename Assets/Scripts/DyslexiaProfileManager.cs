using System;
using UnityEngine;

/// <summary>
/// Central manager for the user's calibrated Dyslexia & Accessibility Profile.
/// Persists preferences across app sessions and broadcasts live change events to UI overlays.
/// </summary>
public class DyslexiaProfileManager : MonoBehaviour
{
    private const string PREFS_KEY = "AksharAR_DyslexiaProfile_v1";

    public static DyslexiaProfileManager Instance { get; private set; }

    [SerializeField] private DyslexiaProfile activeProfile = new DyslexiaProfile();

    public DyslexiaProfile Profile => activeProfile;

    public event Action<DyslexiaProfile> OnProfileChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadProfile();
    }

    public void LoadProfile()
    {
        if (PlayerPrefs.HasKey(PREFS_KEY))
        {
            try
            {
                string json = PlayerPrefs.GetString(PREFS_KEY);
                activeProfile = JsonUtility.FromJson<DyslexiaProfile>(json);
                if (activeProfile == null)
                {
                    activeProfile = new DyslexiaProfile();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DyslexiaProfileManager] Failed to parse saved profile: {ex.Message}. Resetting to defaults.");
                activeProfile = new DyslexiaProfile();
            }
        }
        else
        {
            activeProfile = new DyslexiaProfile();
        }

        Debug.Log($"[DyslexiaProfileManager] Loaded profile: Severity={activeProfile.severity}, Palette={activeProfile.colorPalette}, Tint={activeProfile.substrateTint}, OnboardingDone={activeProfile.hasCompletedOnboarding}");
    }

    public void SaveProfile()
    {
        try
        {
            string json = JsonUtility.ToJson(activeProfile);
            PlayerPrefs.SetString(PREFS_KEY, json);
            PlayerPrefs.Save();
            Debug.Log("[DyslexiaProfileManager] Profile saved successfully.");
            OnProfileChanged?.Invoke(activeProfile);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DyslexiaProfileManager] Failed to save profile: {ex.Message}");
        }
    }

    public void SetSeverity(DyslexiaSeverity severity)
    {
        activeProfile.severity = severity;
        SaveProfile();
    }

    public void SetColorPalette(ColorBlindPalette palette)
    {
        activeProfile.colorPalette = palette;
        SaveProfile();
    }

    public void SetSubstrateTint(SubstrateTint tint)
    {
        activeProfile.substrateTint = tint;
        SaveProfile();
    }

    public void SetFontScale(float scaleMultiplier)
    {
        activeProfile.fontScaleMultiplier = Mathf.Clamp(scaleMultiplier, 0.8f, 1.4f);
        SaveProfile();
    }

    public void SetSyllableSegmentation(bool enable)
    {
        activeProfile.enableSyllableSegmentation = enable;
        SaveProfile();
    }

    public void SetOnboardingComplete(bool complete)
    {
        activeProfile.hasCompletedOnboarding = complete;
        SaveProfile();
    }

    public void ResetToDefaults()
    {
        activeProfile = new DyslexiaProfile();
        SaveProfile();
    }
}

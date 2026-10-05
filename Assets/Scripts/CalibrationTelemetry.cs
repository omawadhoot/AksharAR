using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Telemetry Logger for the Dyslexia Calibration Wizard.
/// Records chosen options, dwell times per step, TTS usage, and subsequent modifications in Settings.
/// Enables defensible, empirical usage analysis without making unverified clinical claims.
/// </summary>
public static class CalibrationTelemetry
{
    private const string PREFS_LOG_KEY = "AksharAR_Calibration_Telemetry_v1";

    [Serializable]
    public class CalibrationSessionRecord
    {
        public string sessionId;
        public string timestampUtc;
        public float step1DwellSec;
        public float step2DwellSec;
        public float step3DwellSec;
        public float step4DwellSec;
        public float totalDurationSec;
        public string selectedSpacing;
        public string selectedPalette;
        public string selectedSubstrate;
        public int ttsClicks;
        public int skippedAtStep; // -1 if completed normally, 0..3 if skipped
        public bool didChangeStep1;
        public bool didChangeStep2;
        public bool didChangeStep3;
        public int settingsModifications;
    }

    [Serializable]
    public class CalibrationLogStore
    {
        public System.Collections.Generic.List<CalibrationSessionRecord> records = new System.Collections.Generic.List<CalibrationSessionRecord>();
    }

    public static void RecordSession(
        float step1Dwell,
        float step2Dwell,
        float step3Dwell,
        float step4Dwell,
        DyslexiaSeverity severity,
        ColorBlindPalette palette,
        SubstrateTint substrate,
        int ttsClicks,
        int skippedAtStep = -1,
        bool didChangeStep1 = false,
        bool didChangeStep2 = false,
        bool didChangeStep3 = false)
    {
        try
        {
            var record = new CalibrationSessionRecord
            {
                sessionId = Guid.NewGuid().ToString("N").Substring(0, 8),
                timestampUtc = DateTime.UtcNow.ToString("o"),
                step1DwellSec = Mathf.Round(step1Dwell * 10f) / 10f,
                step2DwellSec = Mathf.Round(step2Dwell * 10f) / 10f,
                step3DwellSec = Mathf.Round(step3Dwell * 10f) / 10f,
                step4DwellSec = Mathf.Round(step4Dwell * 10f) / 10f,
                totalDurationSec = Mathf.Round((step1Dwell + step2Dwell + step3Dwell + step4Dwell) * 10f) / 10f,
                selectedSpacing = severity.ToString(),
                selectedPalette = palette.ToString(),
                selectedSubstrate = substrate.ToString(),
                ttsClicks = ttsClicks,
                skippedAtStep = skippedAtStep,
                didChangeStep1 = didChangeStep1,
                didChangeStep2 = didChangeStep2,
                didChangeStep3 = didChangeStep3,
                settingsModifications = 0
            };

            // Save to PlayerPrefs
            CalibrationLogStore store = LoadStore();
            store.records.Add(record);
            string json = JsonUtility.ToJson(store, true);
            PlayerPrefs.SetString(PREFS_LOG_KEY, json);
            PlayerPrefs.Save();

            // Save to persistent file
            string filePath = Path.Combine(Application.persistentDataPath, "calibration_telemetry.json");
            File.WriteAllText(filePath, json);

            Debug.Log($"[CalibrationTelemetry] 📊 Logged onboarding session #{record.sessionId}: Spacing={record.selectedSpacing}, Palette={record.selectedPalette}, Tint={record.selectedSubstrate}, Duration={record.totalDurationSec}s, TTSClicks={ttsClicks}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CalibrationTelemetry] Logging failed: {ex.Message}");
        }
    }

    public static void RecordSettingChangedLater()
    {
        try
        {
            CalibrationLogStore store = LoadStore();
            if (store.records.Count > 0)
            {
                store.records[store.records.Count - 1].settingsModifications++;
                string json = JsonUtility.ToJson(store, true);
                PlayerPrefs.SetString(PREFS_LOG_KEY, json);
                PlayerPrefs.Save();

                string filePath = Path.Combine(Application.persistentDataPath, "calibration_telemetry.json");
                File.WriteAllText(filePath, json);
                Debug.Log("[CalibrationTelemetry] 📊 Logged subsequent setting modification.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CalibrationTelemetry] Setting change log failed: {ex.Message}");
        }
    }

    private static CalibrationLogStore LoadStore()
    {
        if (PlayerPrefs.HasKey(PREFS_LOG_KEY))
        {
            try
            {
                string raw = PlayerPrefs.GetString(PREFS_LOG_KEY);
                var parsed = JsonUtility.FromJson<CalibrationLogStore>(raw);
                if (parsed != null) return parsed;
            }
            catch {}
        }
        return new CalibrationLogStore();
    }
}

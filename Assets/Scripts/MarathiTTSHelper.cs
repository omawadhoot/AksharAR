using System;
using UnityEngine;

/// <summary>
/// Accessible Text-To-Speech (TTS) helper for Marathi and Hindi prompts.
/// Supports Android Native TextToSpeech, Windows SAPI speech synthesis, and Editor fallback.
/// Ensures dyslexic children can tap the speaker button to hear instructions read aloud.
/// </summary>
public class MarathiTTSHelper : MonoBehaviour
{
    private static MarathiTTSHelper instance;
    public static MarathiTTSHelper Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("MarathiTTSHelper");
                instance = go.AddComponent<MarathiTTSHelper>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject ttsInstance;
    private bool isAndroidTtsReady = false;
#endif

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeTTS();
    }

    private void InitializeTTS()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            AndroidJavaObject context = currentActivity.Call<AndroidJavaObject>("getApplicationContext");

            ttsInstance = new AndroidJavaObject("android.speech.tts.TextToSpeech", context, new TextToSpeechOnInitListener(this));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MarathiTTSHelper] Failed to init Android TTS: {ex.Message}");
        }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private class TextToSpeechOnInitListener : AndroidJavaProxy
    {
        private MarathiTTSHelper helper;
        public TextToSpeechOnInitListener(MarathiTTSHelper h) : base("android.speech.tts.TextToSpeech$OnInitListener")
        {
            helper = h;
        }

        public void onInit(int status)
        {
            if (status == 0) // TextToSpeech.SUCCESS
            {
                helper.isAndroidTtsReady = true;
                try
                {
                    AndroidJavaObject locale = new AndroidJavaObject("java.util.Locale", "mr", "IN");
                    int result = helper.ttsInstance.Call<int>("setLanguage", locale);
                    if (result < 0)
                    {
                        // Fallback to Hindi locale if Marathi voice is not installed
                        AndroidJavaObject hiLocale = new AndroidJavaObject("java.util.Locale", "hi", "IN");
                        helper.ttsInstance.Call<int>("setLanguage", hiLocale);
                    }
                    helper.ttsInstance.Call<int>("setSpeechRate", 0.9f); // Slightly slower for clarity
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MarathiTTSHelper] Locale setup: {e.Message}");
                }
            }
        }
    }
#endif

    private static AppLanguage currentLanguage = AppLanguage.Marathi;

    public static void SetLanguage(AppLanguage lang)
    {
        currentLanguage = lang;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (instance != null && instance.ttsInstance != null && instance.isAndroidTtsReady)
        {
            try
            {
                string code = (lang == AppLanguage.Hindi) ? "hi" : "mr";
                AndroidJavaObject loc = new AndroidJavaObject("java.util.Locale", code, "IN");
                instance.ttsInstance.Call<int>("setLanguage", loc);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MarathiTTSHelper] SetLanguage error: {ex.Message}");
            }
        }
#endif
    }

    public static void Speak(string text)
    {
        Instance.SpeakInternal(text);
    }

    public static void Stop()
    {
        Instance.StopInternal();
    }

    private void SpeakInternal(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Debug.Log($"[MarathiTTSHelper] 🔊 Speaking: \"{text}\"");

#if UNITY_ANDROID && !UNITY_EDITOR
        if (isAndroidTtsReady && ttsInstance != null)
        {
            try
            {
                ttsInstance.Call<int>("speak", text, 0 /* QUEUE_FLUSH */, null, "AksharARTTS_" + Time.time);
                return;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MarathiTTSHelper] Android speak error: {ex.Message}");
            }
        }
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            // Windows speech synthesis for testing/desktop runtime
            string cleanText = text.Replace("\"", "").Replace("'", "");
            string psScript = $"Add-Type -AssemblyName System.Speech; $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer; $synth.Rate = -1; $synth.SpeakAsync('{cleanText}')";
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psScript}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.Log($"[MarathiTTSHelper] Windows TTS fallback: {ex.Message}");
        }
#endif
    }

    private void StopInternal()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (isAndroidTtsReady && ttsInstance != null)
        {
            try { ttsInstance.Call<int>("stop"); } catch {}
        }
#endif
    }

    private void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (ttsInstance != null)
        {
            try
            {
                ttsInstance.Call("stop");
                ttsInstance.Call("shutdown");
            }
            catch {}
        }
#endif
    }
}

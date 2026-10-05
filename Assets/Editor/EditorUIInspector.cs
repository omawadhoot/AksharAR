using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public static class EditorUIInspector
{
    public static void RenderOnboardingWizard()
    {
        var hudDoc = GameObject.Find("ARLineOverlayDocument")?.GetComponent<UIDocument>();
        if (hudDoc == null) hudDoc = Object.FindFirstObjectByType<UIDocument>();
        if (hudDoc == null) return;
        var root = hudDoc.rootVisualElement;

        var wizard = Object.FindFirstObjectByType<DyslexiaOnboardingWizard>();
        if (wizard != null)
        {
            wizard.InitializeWizardUI();
            wizard.ShowWizard(0);
        }

        CaptureToFile(hudDoc, "Assets/Editor/wizard_step1_live.png");
    }

    private static void CaptureToFile(UIDocument hudDoc, string path)
    {
        var rt = hudDoc.panelSettings.targetTexture;
        if (rt == null) return;

        hudDoc.rootVisualElement.MarkDirtyRepaint();
        
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        Texture2D composite = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        Color[] pixels = tex.GetPixels();
        Color bg = new Color(0.12f, 0.12f, 0.15f, 1.0f);
        for (int i = 0; i < pixels.Length; i++)
        {
            Color src = pixels[i];
            pixels[i] = Color.Lerp(bg, src, src.a);
        }
        composite.SetPixels(pixels);
        composite.Apply();

        File.WriteAllBytes(path, composite.EncodeToPNG());
        Debug.Log("Saved " + path);
    }
}

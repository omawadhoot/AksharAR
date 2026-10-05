using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

public static class EditorSceneSnapshotTester
{
    [MenuItem("AksharAR/Capture Onboarding & Index Snapshots")]
    public static void CaptureAllSnapshots()
    {
        // 1. Capture Onboarding Scene
        EditorSceneManager.OpenScene("Assets/Onboarding.unity");
        var onboardingDoc = Object.FindFirstObjectByType<UIDocument>();
        if (onboardingDoc != null)
        {
            var controller = Object.FindFirstObjectByType<DyslexiaOnboardingSceneController>();
            if (controller != null) controller.InitializeUI();

            CaptureScreenSpaceDoc(onboardingDoc, "Assets/Editor/onboarding_scene_scaled.png");
        }

        // 2. Capture Index Scene
        EditorSceneManager.OpenScene("Assets/Index.unity");
        var indexDoc = GameObject.Find("ARLineOverlayDocument")?.GetComponent<UIDocument>() ?? Object.FindFirstObjectByType<UIDocument>();
        if (indexDoc != null)
        {
            CaptureRenderTextureDoc(indexDoc, "Assets/Editor/index_scene_hud.png");
        }

        Debug.Log("[EditorSceneSnapshotTester] Captured snapshots for both scenes.");
    }

    private static void CaptureScreenSpaceDoc(UIDocument doc, string path)
    {
        int width = 1080;
        int height = 2400;
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        rt.Create();

        var originalTarget = doc.panelSettings.targetTexture;
        doc.panelSettings.targetTexture = rt;
        
        // Force repaint
        doc.rootVisualElement.MarkDirtyRepaint();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;

        doc.panelSettings.targetTexture = originalTarget;
        rt.Release();
        Object.DestroyImmediate(rt);

        // Composite over dark background
        Texture2D composite = new Texture2D(width, height, TextureFormat.RGB24, false);
        Color[] pixels = tex.GetPixels();
        Color bg = new Color(0.06f, 0.09f, 0.16f, 1.0f);
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

    private static void CaptureRenderTextureDoc(UIDocument hudDoc, string path)
    {
        var rt = hudDoc.panelSettings.targetTexture;
        if (rt == null) return;

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

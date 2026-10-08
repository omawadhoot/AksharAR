using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

public class AndroidMonochromeIconInjector : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 999;

    [MenuItem("AksharAR/Inject Monochrome Icon Now")]
    public static void ManualExecute()
    {
        string defaultGradlePath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary"));
        new AndroidMonochromeIconInjector().OnPostGenerateGradleAndroidProject(defaultGradlePath);
    }

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        Debug.Log($"[AndroidMonochromeIconInjector] Post-processing Android Gradle project at: {path}");

        string monoSourcePath = Path.GetFullPath(Path.Combine(Application.dataPath, "Icons/Android_Monochrome_Icon.png"));
        if (!File.Exists(monoSourcePath))
        {
            Debug.LogError($"[AndroidMonochromeIconInjector] Monochrome icon not found at: {monoSourcePath}");
            return;
        }

        string launcherResDir = null;
        string[] candidatePaths = new[]
        {
            Path.Combine(path, "../launcher/src/main/res"),
            Path.Combine(path, "launcher/src/main/res"),
            Path.Combine(path, "src/main/res"),
            Path.Combine(path, "../src/main/res")
        };

        foreach (var c in candidatePaths)
        {
            string full = Path.GetFullPath(c);
            if (Directory.Exists(full) && Directory.Exists(Path.Combine(full, "mipmap-anydpi-v26")))
            {
                launcherResDir = full;
                break;
            }
        }

        if (string.IsNullOrEmpty(launcherResDir))
        {
            Debug.LogError($"[AndroidMonochromeIconInjector] Could not find launcher res directory from: {path}");
            return;
        }

        Debug.Log($"[AndroidMonochromeIconInjector] Target launcher res directory: {launcherResDir}");

        byte[] monoBytes = File.ReadAllBytes(monoSourcePath);

        var densities = new (string folder, int size)[]
        {
            ("mipmap-mdpi", 108),
            ("mipmap-hdpi", 162),
            ("mipmap-xhdpi", 216),
            ("mipmap-xxhdpi", 324),
            ("mipmap-xxxhdpi", 432)
        };

        foreach (var (folder, size) in densities)
        {
            string folderPath = Path.Combine(launcherResDir, folder);
            if (Directory.Exists(folderPath))
            {
                Texture2D srcTex = new Texture2D(2, 2);
                srcTex.LoadImage(monoBytes);

                RenderTexture rt = RenderTexture.GetTemporary(size, size);
                RenderTexture.active = rt;
                Graphics.Blit(srcTex, rt);
                Texture2D resTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                resTex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                resTex.Apply();
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);

                string outPath = Path.Combine(folderPath, "ic_launcher_monochrome.png");
                File.WriteAllBytes(outPath, resTex.EncodeToPNG());
                Object.DestroyImmediate(resTex);
                Object.DestroyImmediate(srcTex);
                Debug.Log($"[AndroidMonochromeIconInjector] Generated {size}x{size} monochrome in {folder}");
            }
        }

        string anyDpiDir = Path.Combine(launcherResDir, "mipmap-anydpi-v26");
        string[] xmlFiles = new[] { "app_icon.xml", "app_icon_round.xml" };
        foreach (var xmlName in xmlFiles)
        {
            string xmlPath = Path.Combine(anyDpiDir, xmlName);
            if (File.Exists(xmlPath))
            {
                string xml = File.ReadAllText(xmlPath);
                if (!xml.Contains("ic_launcher_monochrome"))
                {
                    xml = xml.Replace("</adaptive-icon>", "    <monochrome android:drawable=\"@mipmap/ic_launcher_monochrome\" />\n</adaptive-icon>");
                    File.WriteAllText(xmlPath, xml);
                    Debug.Log($"[AndroidMonochromeIconInjector] Successfully injected monochrome tag into {xmlName}");
                }
                else
                {
                    Debug.Log($"[AndroidMonochromeIconInjector] Monochrome tag already present in {xmlName}");
                }
            }
        }

        Debug.Log("[AndroidMonochromeIconInjector] Successfully finished configuring Nothing OS / Android 13+ monochrome icon!");
    }
}

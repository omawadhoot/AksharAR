using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class ApplyAdaptiveIcons
{
    private const string PATH_BG = "Assets/Icons/Android_Adaptive_Background.png";
    private const string PATH_FG = "Assets/Icons/Android_Adaptive_Foreground.png";
    private const string PATH_ROUND = "Assets/Icons/Android_Round_Icon.png";
    private const string PATH_LEGACY = "Assets/Icons/Android_Legacy_Icon.png";

    [MenuItem("AksharAR/Configure Adaptive Icons")]
    public static void Execute()
    {
        ConfigureTextureImporter(PATH_BG, false);
        ConfigureTextureImporter(PATH_FG, true);
        ConfigureTextureImporter(PATH_ROUND, false);
        ConfigureTextureImporter(PATH_LEGACY, false);

        Texture2D bgTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PATH_BG);
        Texture2D fgTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PATH_FG);
        Texture2D roundTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PATH_ROUND);
        Texture2D legacyTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PATH_LEGACY);

        if (bgTex == null || fgTex == null || roundTex == null || legacyTex == null)
        {
            Debug.LogError("[ApplyAdaptiveIcons] One or more icon textures could not be loaded.");
            return;
        }

        var target = NamedBuildTarget.Android;
        var kinds = PlayerSettings.GetSupportedIconKinds(target);

        foreach (var k in kinds)
        {
            var icons = PlayerSettings.GetPlatformIcons(target, k);
            string kindName = k.ToString();

            if (kindName.Contains("Adaptive"))
            {
                for (int i = 0; i < icons.Length; i++)
                {
                    icons[i].SetTextures(new Texture2D[] { bgTex, fgTex });
                }
                PlayerSettings.SetPlatformIcons(target, k, icons);
                Debug.Log($"[ApplyAdaptiveIcons] Configured {icons.Length} Adaptive icons (Background + Foreground).");
            }
            else if (kindName.Contains("Round"))
            {
                for (int i = 0; i < icons.Length; i++)
                {
                    icons[i].SetTextures(new Texture2D[] { roundTex });
                }
                PlayerSettings.SetPlatformIcons(target, k, icons);
                Debug.Log($"[ApplyAdaptiveIcons] Configured {icons.Length} Round icons.");
            }
            else if (kindName.Contains("Legacy"))
            {
                for (int i = 0; i < icons.Length; i++)
                {
                    icons[i].SetTextures(new Texture2D[] { legacyTex });
                }
                PlayerSettings.SetPlatformIcons(target, k, icons);
                Debug.Log($"[ApplyAdaptiveIcons] Configured {icons.Length} Legacy icons.");
            }
        }

        // Set default application icon to the composited legacy icon
        PlayerSettings.SetIcons(target, new Texture2D[] { legacyTex }, IconKind.Application);
        PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new Texture2D[] { legacyTex }, IconKind.Application);

        AssetDatabase.SaveAssets();
        Debug.Log("[ApplyAdaptiveIcons] Successfully configured all Android Adaptive & Platform Icons!");
    }

    private static void ConfigureTextureImporter(string path, bool alphaIsTransparency)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = alphaIsTransparency;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}

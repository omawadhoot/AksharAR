using System.IO;
using UnityEditor;
using UnityEngine;

public static class AppIconConfigurator
{
    private const string ICON_PATH = "Assets/Icons/AppIcon.png";

    [MenuItem("AksharAR/Apply App Icon")]
    public static void Execute()
    {
        if (!File.Exists(ICON_PATH))
        {
            Debug.LogError($"[AppIconConfigurator] Icon file not found at: {ICON_PATH}");
            return;
        }

        // Configure Texture Importer for Icon
        TextureImporter importer = AssetImporter.GetAtPath(ICON_PATH) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            Debug.Log("[AppIconConfigurator] TextureImporter settings updated for AppIcon.png");
        }

        Texture2D iconTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ICON_PATH);
        if (iconTex == null)
        {
            Debug.LogError("[AppIconConfigurator] Failed to load Texture2D after reimport.");
            return;
        }

        Texture2D[] icons = new Texture2D[] { iconTex };

        UnityEditor.Build.NamedBuildTarget unknownTarget = UnityEditor.Build.NamedBuildTarget.Unknown;
        UnityEditor.Build.NamedBuildTarget androidTarget = UnityEditor.Build.NamedBuildTarget.Android;
        UnityEditor.Build.NamedBuildTarget iosTarget = UnityEditor.Build.NamedBuildTarget.iOS;
        UnityEditor.Build.NamedBuildTarget standaloneTarget = UnityEditor.Build.NamedBuildTarget.Standalone;

        // 1. Default (Unknown platform)
        PlayerSettings.SetIcons(unknownTarget, icons, IconKind.Application);

        // 2. Android
        PlayerSettings.SetIcons(androidTarget, icons, IconKind.Application);

        // 3. iOS
        PlayerSettings.SetIcons(iosTarget, icons, IconKind.Application);

        // 4. Standalone
        PlayerSettings.SetIcons(standaloneTarget, icons, IconKind.Application);

        AssetDatabase.SaveAssets();
        Debug.Log($"[AppIconConfigurator] Successfully applied {ICON_PATH} as the default application icon across all platforms!");
    }
}

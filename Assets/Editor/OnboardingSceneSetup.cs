using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UIElements;

public static class OnboardingSceneSetup
{
    [MenuItem("AksharAR/Setup Dedicated Onboarding Scene")]
    public static void CreateOnboardingScene()
    {
        // 1. Create or Find ScreenSpacePanelSettings
        string panelSettingsPath = "Assets/Resources/ScreenSpacePanelSettings.asset";
        PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelSettingsPath);
        if (panelSettings == null)
        {
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "ScreenSpacePanelSettings";
            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1080, 2400);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            panelSettings.targetTexture = null; // Pure ScreenSpaceOverlay!

            // Text settings & SDF shader
            panelSettings.colorClearValue = new Color(0.06f, 0.09f, 0.16f, 1f);

            AssetDatabase.CreateAsset(panelSettings, panelSettingsPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OnboardingSceneSetup] Created {panelSettingsPath}");
        }

        // 2. Create the Onboarding Scene
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera
        var camObj = new GameObject("Main Camera");
        var cam = camObj.AddComponent<Camera>();
        camObj.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.09f, 0.16f, 1f); // #0F172A
        cam.orthographic = true;
        cam.orthographicSize = 5f;
        cam.cullingMask = 0; // No 3D objects needed

        // Event System with New Input System
        var eventSysObj = new GameObject("EventSystem");
        eventSysObj.AddComponent<EventSystem>();
        eventSysObj.AddComponent<InputSystemUIInputModule>();

        // Profile Manager
        var mgrObj = new GameObject("DyslexiaProfileManager");
        mgrObj.AddComponent<DyslexiaProfileManager>();

        // UI Document & Controller
        var uiDocObj = new GameObject("OnboardingUIDocument");
        var uiDoc = uiDocObj.AddComponent<UIDocument>();
        
        var loadedPanelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/Resources/ScreenSpacePanelSettings.asset");
        if (loadedPanelSettings != null)
        {
            uiDoc.panelSettings = loadedPanelSettings;
        }

        var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Resources/DyslexiaOnboarding.uxml");
        if (uxml != null)
        {
            uiDoc.visualTreeAsset = uxml;
        }

        var controller = uiDocObj.AddComponent<DyslexiaOnboardingSceneController>();

        // 3. Save Scene
        string scenePath = "Assets/Onboarding.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        Debug.Log($"[OnboardingSceneSetup] Successfully saved {scenePath}");

        // 4. Configure EditorBuildSettings
        var buildScenes = new EditorBuildSettingsScene[]
        {
            new EditorBuildSettingsScene("Assets/Onboarding.unity", true),
            new EditorBuildSettingsScene("Assets/Index.unity", true)
        };
        EditorBuildSettings.scenes = buildScenes;
        Debug.Log("[OnboardingSceneSetup] Updated Build Settings: Scene 0 = Onboarding.unity, Scene 1 = Index.unity");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}

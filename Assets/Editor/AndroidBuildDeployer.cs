using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class AndroidBuildDeployer
{
    public static string BuildAndroidApk()
    {
        string buildDirectory = "Builds";
        if (!Directory.Exists(buildDirectory))
        {
            Directory.CreateDirectory(buildDirectory);
        }

        string apkPath = Path.Combine(buildDirectory, "AksharAR.apk");

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Index.unity" },
            locationPathName = apkPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        Debug.Log("[AndroidBuildDeployer] Starting Android APK build...");
        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[AndroidBuildDeployer] Build succeeded: {summary.totalSize / (1024 * 1024)} MB in {summary.totalTime.TotalSeconds:F1}s");
            return "SUCCESS";
        }
        else
        {
            Debug.LogError($"[AndroidBuildDeployer] Build failed with result: {summary.result}, total errors: {summary.totalErrors}");
            return $"FAILED: {summary.result}";
        }
    }
}

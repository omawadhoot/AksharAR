using System.Collections.Generic;
using UnityEngine;

public class ARDebugLogger : MonoBehaviour
{
    [Header("On-Screen Log Display Settings")]
    [SerializeField] private bool showOnScreenLogs = false;
    [SerializeField] private int maxLogLines = 25;
    [SerializeField] private int fontSize = 20;

    public bool ShowOnScreenLogs
    {
        get => showOnScreenLogs;
        set => showOnScreenLogs = value;
    }

    private readonly List<string> logBuffer = new List<string>();
    private Vector2 scrollPosition;

    private void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        string colorTag = "#FFFFFF";
        switch (type)
        {
            case LogType.Error:
            case LogType.Exception:
                colorTag = "#FF5555";
                break;
            case LogType.Warning:
                colorTag = "#FFDD55";
                break;
            case LogType.Log:
                if (logString.Contains("SUCCESS"))
                    colorTag = "#55FF55";
                break;
        }

        string formattedLine = $"<color={colorTag}>[{System.DateTime.Now:HH:mm:ss}] {logString}</color>";
        logBuffer.Add(formattedLine);

        if (logBuffer.Count > maxLogLines)
        {
            logBuffer.RemoveAt(0);
        }
    }

    private void OnGUI()
    {
        if (!showOnScreenLogs) return;

        GUIStyle style = new GUIStyle(GUI.skin.textArea)
        {
            fontSize = fontSize,
            richText = true,
            wordWrap = true
        };

        float width = Screen.width * 0.95f;
        float height = Screen.height * 0.35f;
        float left = (Screen.width - width) / 2f;
        float top = Screen.height - height - 20f;

        Rect rect = new Rect(left, top, width, height);

        GUILayout.BeginArea(rect, "<b>AR Mobile Console Output</b>", GUI.skin.window);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        foreach (string line in logBuffer)
        {
            GUILayout.Label(line, style);
        }

        GUILayout.EndScrollView();

        if (GUILayout.Button("Clear Logs"))
        {
            logBuffer.Clear();
        }

        GUILayout.EndArea();
    }
}

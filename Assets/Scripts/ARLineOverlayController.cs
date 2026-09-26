using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

[Serializable]
public struct DetectedTextLine
{
    public string text;
    public Rect boundingBox; // Pixel coordinates: x, y, width, height
}

public class ARLineOverlayController : MonoBehaviour
{
    [Header("UI Toolkit Setup")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private StyleSheet lineStyleSheet;

    private VisualElement containerElement;

    private void Awake()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (uiDocument != null)
        {
            containerElement = uiDocument.rootVisualElement;

            if (lineStyleSheet != null && !containerElement.styleSheets.Contains(lineStyleSheet))
            {
                containerElement.styleSheets.Add(lineStyleSheet);
            }
        }
    }

    /// <summary>
    /// Spawns dynamic Hindi labels over detected line coordinates.
    /// </summary>
    public void DisplayDetectedLines(List<DetectedTextLine> detectedLines, Vector2 visionImageSize)
    {
        if (containerElement == null)
            InitializeUI();

        if (containerElement == null)
        {
            Debug.LogError("[ARLineOverlayController] UIDocument RootVisualElement is null!");
            return;
        }

        // 1. Clear previous labels
        containerElement.Clear();

        float containerWidth = containerElement.resolvedStyle.width;
        float containerHeight = containerElement.resolvedStyle.height;

        if (float.IsNaN(containerWidth) || containerWidth <= 0) containerWidth = 1080f;
        if (float.IsNaN(containerHeight) || containerHeight <= 0) containerHeight = 1920f;

        float scaleX = containerWidth / visionImageSize.x;
        float scaleY = containerHeight / visionImageSize.y;

        Debug.Log($"[ARLineOverlayController] Processing {detectedLines.Count} detected lines. Target Canvas: {containerWidth}x{containerHeight}, Scale: ({scaleX:F2}, {scaleY:F2})");

        int index = 1;
        foreach (var line in detectedLines)
        {
            Label label = new Label(line.text);
            label.AddToClassList("hindi-ar-line");

            // Explicitly force Advanced Text Generator for Devanagari shaping
            label.style.unityTextGenerator = new StyleEnum<TextGeneratorType>(TextGeneratorType.Advanced);

            float scaledX = line.boundingBox.x * scaleX;
            float scaledY = line.boundingBox.y * scaleY;
            float scaledWidth = line.boundingBox.width * scaleX;
            float scaledHeight = line.boundingBox.height * scaleY;

            label.style.position = Position.Absolute;
            label.style.left = scaledX;
            label.style.top = scaledY;
            label.style.width = scaledWidth;
            label.style.height = scaledHeight;

            containerElement.Add(label);

            Debug.Log($"[Line #{index++}] Text: \"{line.text}\" | Bounds: [X:{scaledX:F0}, Y:{scaledY:F0}, W:{scaledWidth:F0}, H:{scaledHeight:F0}]");
        }
    }
}

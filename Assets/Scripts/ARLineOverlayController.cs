using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UIToolkit = UnityEngine.UIElements;

[Serializable]
public struct DetectedTextLine
{
    public string text;
    public Rect boundingBox;
}

/// <summary>
/// Manages the screen HUD for AksharAR.
/// The Scan button is a uGUI Button on a Screen Space - Overlay Canvas
/// so that GraphicRaycaster handles Android touch independently of AR Foundation.
/// UI Toolkit (ARLineOverlayDocument) is kept for purely visual overlay content — no interactivity.
/// </summary>
public class ARLineOverlayController : MonoBehaviour
{
    [Header("uGUI Scan Button (Screen Space Overlay Canvas)")]
    [SerializeField] private Button scanButton; // UnityEngine.UI.Button

    [Header("UI Toolkit (Visual Overlay Only — no touch)")]
    [SerializeField] private UIToolkit.UIDocument uiDocument;

    [Header("Scan Controller Reference")]
    [SerializeField] private ARPageScanController pageScanController;

    // UI Toolkit visual root (read-only, pass-through)
    private UIToolkit.VisualElement rootElement;

    private float lastClickTimestamp = -1f;
    private bool isReadingMode = false;

    private void Start()
    {
        InitializeScanButton();
        InitializeUIToolkitVisual();
    }

    private void OnDestroy()
    {
        if (scanButton != null)
            scanButton.onClick.RemoveListener(OnScanButtonClicked);
    }

    // ─── uGUI Button Setup ────────────────────────────────────────────────────

    private void InitializeScanButton()
    {
        if (scanButton == null)
        {
            // Auto-find by name if not assigned in Inspector
            var btnObj = GameObject.Find("ScanButton_UGUI");
            if (btnObj != null)
                scanButton = btnObj.GetComponent<Button>();
        }

        if (scanButton == null)
        {
            Debug.LogError("[ARLineOverlayController] uGUI ScanButton not found! " +
                           "Assign it in the Inspector or ensure 'ScanButton_UGUI' exists in the scene.");
            return;
        }

        RectTransform btnRect = scanButton.GetComponent<RectTransform>();
        if (btnRect != null)
        {
            btnRect.sizeDelta = new Vector2(Mathf.Max(btnRect.sizeDelta.x, 280f), Mathf.Max(btnRect.sizeDelta.y, 76f));
        }

        var tmp = scanButton.GetComponentInChildren<TMPro.TMP_Text>();
        if (tmp != null)
        {
            tmp.fontSize = Mathf.Max(tmp.fontSize, 26f);
        }
        var legacyText = scanButton.GetComponentInChildren<Text>();
        if (legacyText != null)
        {
            legacyText.fontSize = Mathf.Max(legacyText.fontSize, 24);
        }

        scanButton.onClick.RemoveAllListeners();
        scanButton.onClick.AddListener(OnScanButtonClicked);
        Debug.Log("[ARLineOverlayController] uGUI ScanButton bound via onClick.");
    }

    // ─── UI Toolkit Visual Setup (pass-through, no interaction) ───────────────

    private void InitializeUIToolkitVisual()
    {
        if (uiDocument == null)
        {
            var docObj = GameObject.Find("ARLineOverlayDocument");
            if (docObj != null)
                uiDocument = docObj.GetComponent<UIToolkit.UIDocument>();
        }

        if (uiDocument == null) return;

        rootElement = uiDocument.rootVisualElement;
        if (rootElement == null) return;

        // Entire UI Toolkit tree is pass-through — no interactive elements remain here
        SetPickingModeRecursive(rootElement, UIToolkit.PickingMode.Ignore);
        Debug.Log("[ARLineOverlayController] UI Toolkit visual overlay initialized (pass-through).");
    }

    private void SetPickingModeRecursive(UIToolkit.VisualElement el, UIToolkit.PickingMode mode)
    {
        el.pickingMode = mode;
        foreach (var child in el.Children())
            SetPickingModeRecursive(child, mode);
    }

    // ─── Button Callback ──────────────────────────────────────────────────────

    public void OnScanButtonClicked()
    {
        if (Time.unscaledTime - lastClickTimestamp < 0.6f) return;
        lastClickTimestamp = Time.unscaledTime;

        // If currently in Reading Mode, tapping "🔄 Rescan" restores the viewfinder to frame a new paragraph:
        if (isReadingMode)
        {
            isReadingMode = false;
            var viewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();
            if (viewfinder != null)
                viewfinder.SetVisibility(true);

            SetScanButtonText("📷 Scan Page");
            return;
        }

        // Otherwise, trigger the camera capture & OCR scan
        Debug.Log("[ARLineOverlayController] >>> CAPTURE / SCAN BUTTON CLICKED <<<");

        SetScanButtonText("⏳ Scanning...");
        SetScanButtonInteractable(false);

        // Hide viewfinder while scanning so screen stays clean
        var vf = FindFirstObjectByType<ReadingWindowViewfinder>();
        if (vf != null)
            vf.SetVisibility(false);

        if (pageScanController == null)
            pageScanController = FindAnyObjectByType<ARPageScanController>();

        if (pageScanController != null)
        {
            pageScanController.TriggerManualScan();
        }
        else
        {
            Debug.LogError("[ARLineOverlayController] ARPageScanController not found!");
            ResetScanButton();
        }
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public void ResetScanButton()
    {
        isReadingMode = false;
        SetScanButtonText("📷 Scan Page");
        SetScanButtonInteractable(true);

        var viewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();
        if (viewfinder != null)
            viewfinder.SetVisibility(true);
    }

    public void DisplayDetectedLines(List<DetectedTextLine> detectedLines, Vector2 visionImageSize)
    {
        // OCR text received & rendered onto AR Quad!
        // 1. Completely hide the viewfinder so the reader enjoys an unobstructed view:
        var viewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();
        if (viewfinder != null)
            viewfinder.SetVisibility(false);

        // 2. Transition button to "🔄 Rescan" mode:
        isReadingMode = true;
        SetScanButtonText("🔄 Rescan");
        SetScanButtonInteractable(true);
    }

    public void SetUIVisibility(bool visible)
    {
        if (scanButton != null)
            scanButton.gameObject.SetActive(visible);

        if (rootElement != null)
            rootElement.style.display = visible ? UIToolkit.DisplayStyle.Flex : UIToolkit.DisplayStyle.None;

        var viewfinder = FindFirstObjectByType<ReadingWindowViewfinder>();
        if (viewfinder != null)
        {
            // Do not re-show viewfinder if in reading mode
            if (!visible || !isReadingMode)
                viewfinder.SetVisibility(visible);
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    public void SetScanButtonText(string text)
    {
        if (scanButton == null) return;
        var label = scanButton.GetComponentInChildren<Text>();
        if (label != null) label.text = text;

        // Also support TextMeshPro on the button label if present
        var tmp = scanButton.GetComponentInChildren<TMPro.TMP_Text>();
        if (tmp != null) tmp.text = text;
    }

    public void SetScanButtonInteractable(bool interactable)
    {
        if (scanButton != null)
            scanButton.interactable = interactable;
    }
}

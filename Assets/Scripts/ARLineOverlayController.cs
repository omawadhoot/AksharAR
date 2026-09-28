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
        if (Time.unscaledTime - lastClickTimestamp < 1.5f) return;
        lastClickTimestamp = Time.unscaledTime;

        Debug.Log("[ARLineOverlayController] >>> CAPTURE / SCAN BUTTON CLICKED <<<");

        SetScanButtonText("⏳ Scanning...");
        SetScanButtonInteractable(false);

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
        SetScanButtonText("📷 Scan Page");
        SetScanButtonInteractable(true);
    }

    public void DisplayDetectedLines(List<DetectedTextLine> detectedLines, Vector2 visionImageSize)
    {
        // OCR result received — re-enable the button
        ResetScanButton();
    }

    public void SetUIVisibility(bool visible)
    {
        if (scanButton != null)
            scanButton.gameObject.SetActive(visible);

        if (rootElement != null)
            rootElement.style.display = visible ? UIToolkit.DisplayStyle.Flex : UIToolkit.DisplayStyle.None;
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void SetScanButtonText(string text)
    {
        if (scanButton == null) return;
        var label = scanButton.GetComponentInChildren<Text>();
        if (label != null) label.text = text;

        // Also support TextMeshPro on the button label if present
        var tmp = scanButton.GetComponentInChildren<TMPro.TMP_Text>();
        if (tmp != null) tmp.text = text;
    }

    private void SetScanButtonInteractable(bool interactable)
    {
        if (scanButton != null)
            scanButton.interactable = interactable;
    }
}

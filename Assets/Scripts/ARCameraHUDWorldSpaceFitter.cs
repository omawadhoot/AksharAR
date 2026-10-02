using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.EventSystems;

/// <summary>
/// Attaches a UI Toolkit UIDocument to the AR Main Camera in World Space.
/// Automatically matches the camera view frustum so the UI acts as an edge-to-edge
/// HUD overlay, bypassing the ARCore Android camera background overlay bug in Unity 6.
/// Ensures PhysicsRaycaster is attached to Main Camera for 3D touch interaction.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ARCameraHUDWorldSpaceFitter : MonoBehaviour
{
    [Tooltip("Distance in meters in front of the AR Camera lens")]
    [SerializeField] private float distanceFromCamera = 0.45f;

    private UIDocument uiDocument;
    private Camera targetCamera;
    private int lastWidth = -1;
    private int lastHeight = -1;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        EnsureWorldSpacePanelSettings();
        EnsurePhysicsRaycaster();
        AttachToCamera();
    }

    private void Start()
    {
        UpdateFrustumFit();
    }

    private void LateUpdate()
    {
        if (targetCamera == null)
        {
            AttachToCamera();
        }

        if (Screen.width != lastWidth || Screen.height != lastHeight)
        {
            UpdateFrustumFit();
        }
    }

    private void EnsureWorldSpacePanelSettings()
    {
        if (uiDocument != null && uiDocument.panelSettings != null)
        {
            uiDocument.panelSettings.renderMode = PanelRenderMode.WorldSpace;
        }
    }

    private void EnsurePhysicsRaycaster()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam != null)
        {
            PhysicsRaycaster raycaster = cam.GetComponent<PhysicsRaycaster>();
            if (raycaster == null)
            {
                raycaster = cam.gameObject.AddComponent<PhysicsRaycaster>();
                Debug.Log("[ARCameraHUDWorldSpaceFitter] Added PhysicsRaycaster to Main Camera for World Space UI Toolkit touch interactions.");
            }
        }
    }

    public void AttachToCamera()
    {
        targetCamera = Camera.main;
        if (targetCamera == null) return;

        EnsurePhysicsRaycaster();

        transform.SetParent(targetCamera.transform, false);
        transform.localPosition = new Vector3(0f, 0f, distanceFromCamera);
        transform.localRotation = Quaternion.identity;

        EnsureBoxCollider();
        UpdateFrustumFit();
    }

    private void EnsureBoxCollider()
    {
        BoxCollider boxCol = GetComponent<BoxCollider>();
        if (boxCol == null)
        {
            boxCol = gameObject.AddComponent<BoxCollider>();
            boxCol.isTrigger = true;
        }
    }

    public void UpdateFrustumFit()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null || uiDocument == null) return;

        lastWidth = Screen.width;
        lastHeight = Screen.height;

        // Frustum height & width in meters at distanceFromCamera
        float fovRad = targetCamera.fieldOfView * Mathf.Deg2Rad;
        float frustumHeight = 2.0f * distanceFromCamera * Mathf.Tan(fovRad * 0.5f);
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        float frustumWidth = frustumHeight * aspect;

        // Native UI Document dimensions from PanelSettings reference resolution
        Vector2 refRes = (uiDocument.panelSettings != null)
            ? (Vector2)uiDocument.panelSettings.referenceResolution
            : new Vector2(1080f, 2400f);

        float docWidth = refRes.x > 0 ? refRes.x : 1080f;
        float docHeight = refRes.y > 0 ? refRes.y : 2400f;

        float ppu = (uiDocument.panelSettings != null && uiDocument.panelSettings.referenceSpritePixelsPerUnit > 0)
            ? uiDocument.panelSettings.referenceSpritePixelsPerUnit
            : 100f;

        float nativeWidthMeters = docWidth / ppu;
        float nativeHeightMeters = docHeight / ppu;

        if (nativeWidthMeters > 0.001f && nativeHeightMeters > 0.001f)
        {
            float scaleX = frustumWidth / nativeWidthMeters;
            float scaleY = frustumHeight / nativeHeightMeters;
            transform.localScale = new Vector3(scaleX, scaleY, 1.0f);
        }

        BoxCollider boxCol = GetComponent<BoxCollider>();
        if (boxCol != null)
        {
            boxCol.size = new Vector3(nativeWidthMeters, nativeHeightMeters, 0.02f);
            boxCol.center = Vector3.zero;
        }
    }
}

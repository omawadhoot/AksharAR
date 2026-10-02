using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.EventSystems;

/// <summary>
/// Attaches a UI Toolkit UIDocument to the AR Main Camera in World Space.
/// Automatically matches the camera view frustum so the UI acts as an edge-to-edge
/// HUD overlay, bypassing the ARCore Android camera background overlay bug in Unity 6.
/// Ensures WorldDocumentRaycaster and PanelInputConfiguration are active for input routing,
/// and eliminates conflicting PhysicsRaycasters.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class ARCameraHUDWorldSpaceFitter : MonoBehaviour
{
    [Tooltip("Distance in meters in front of the AR Camera lens (must be > camera near clip 0.10m, but < reading distance 0.30m)")]
    [SerializeField] private float distanceFromCamera = 0.18f;

    private UIDocument uiDocument;
    private Camera targetCamera;
    private int lastWidth = -1;
    private int lastHeight = -1;
    private Matrix4x4 lastProjectionMatrix = Matrix4x4.zero;

    private void Awake()
    {
        uiDocument = GetComponent<UIDocument>();
        EnsureWorldSpacePanelSettings();
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
            return;
        }

        Matrix4x4 currentProj = targetCamera.projectionMatrix;
        if (Screen.width != lastWidth || Screen.height != lastHeight || currentProj != lastProjectionMatrix)
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

    private void EnsureWorldSpaceInput(Camera cam)
    {
        if (cam == null) return;

        // 1. Remove legacy/conflicting PhysicsRaycaster from Camera if present
        PhysicsRaycaster physRaycaster = cam.GetComponent<PhysicsRaycaster>();
        if (physRaycaster != null)
        {
            Destroy(physRaycaster);
            Debug.Log("[ARCameraHUDWorldSpaceFitter] Removed conflicting PhysicsRaycaster from Main Camera.");
        }

        // 2. Ensure PanelInputConfiguration exists on EventSystem with World Space input enabled
        var es = Object.FindFirstObjectByType<EventSystem>();
        if (es != null)
        {
            var pic = es.GetComponent<PanelInputConfiguration>();
            if (pic == null)
            {
                pic = es.gameObject.AddComponent<PanelInputConfiguration>();
            }
            pic.processWorldSpaceInput = true;
            pic.defaultEventCameraIsMainCamera = true;
            pic.autoCreatePanelComponents = true;
        }

        // 3. Ensure WorldDocumentRaycaster is attached to Camera for UI Toolkit World Space input routing
        var wr = cam.GetComponent<WorldDocumentRaycaster>();
        if (wr == null)
        {
            wr = cam.gameObject.AddComponent<WorldDocumentRaycaster>();
            Debug.Log("[ARCameraHUDWorldSpaceFitter] Added WorldDocumentRaycaster to AR Camera for UI Toolkit touch interaction.");
        }
    }

    public void AttachToCamera()
    {
        targetCamera = Camera.main;
        if (targetCamera == null) return;

        EnsureWorldSpaceInput(targetCamera);

        transform.SetParent(targetCamera.transform, false);
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
        lastProjectionMatrix = targetCamera.projectionMatrix;

        // 1. Determine target UI resolution matching screen aspect ratio
        float targetWidth = Screen.width > 0 ? Screen.width : 1080f;
        float targetHeight = Screen.height > 0 ? Screen.height : 2400f;

        // Synchronize UIDocument's World Space dimensions with screen aspect
        uiDocument.worldSpaceSize = new Vector2(targetWidth, targetHeight);

        // 2. Measure actual camera frustum in world space at distanceFromCamera using ViewportToWorldPoint.
        // This accurately takes ARCore's Android device-calibrated projection matrix into account.
        Vector3 centerWorld = targetCamera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, distanceFromCamera));
        Vector3 leftWorld   = targetCamera.ViewportToWorldPoint(new Vector3(0.0f, 0.5f, distanceFromCamera));
        Vector3 rightWorld  = targetCamera.ViewportToWorldPoint(new Vector3(1.0f, 0.5f, distanceFromCamera));
        Vector3 bottomWorld = targetCamera.ViewportToWorldPoint(new Vector3(0.5f, 0.0f, distanceFromCamera));
        Vector3 topWorld    = targetCamera.ViewportToWorldPoint(new Vector3(0.5f, 1.0f, distanceFromCamera));

        float frustumWidth = Vector3.Distance(leftWorld, rightWorld);
        float frustumHeight = Vector3.Distance(bottomWorld, topWorld);

        // Position & align with target camera in local space
        if (transform.parent == targetCamera.transform)
        {
            transform.localPosition = targetCamera.transform.InverseTransformPoint(centerWorld);
            transform.localRotation = Quaternion.identity;
        }
        else
        {
            transform.position = centerWorld;
            transform.rotation = targetCamera.transform.rotation;
        }

        // 3. Scale mesh so the UIDocument fills the camera view frustum edge-to-edge
        float ppu = (uiDocument.panelSettings != null && uiDocument.panelSettings.referenceSpritePixelsPerUnit > 0)
            ? uiDocument.panelSettings.referenceSpritePixelsPerUnit
            : 100f;

        float meshWidthMeters = targetWidth / ppu;
        float meshHeightMeters = targetHeight / ppu;

        if (meshWidthMeters > 0.001f && meshHeightMeters > 0.001f)
        {
            float scaleX = frustumWidth / meshWidthMeters;
            float scaleY = frustumHeight / meshHeightMeters;
            transform.localScale = new Vector3(scaleX, scaleY, 1.0f);
        }

        // 4. Update BoxCollider to match the UI mesh bounding dimensions
        BoxCollider boxCol = GetComponent<BoxCollider>();
        if (boxCol != null)
        {
            boxCol.size = new Vector3(meshWidthMeters, meshHeightMeters, 0.01f);
            boxCol.center = Vector3.zero;
        }
    }
}

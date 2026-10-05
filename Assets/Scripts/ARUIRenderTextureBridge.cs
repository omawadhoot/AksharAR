using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// AOT/IL2CPP-Safe Fast Event Setter Wrapper.
/// Constrained precisely to PointerEventBase<TEvent>.
/// Attempts compiled delegate binding for zero-GC execution speed;
/// falls back to PropertyInfo.SetValue if IL2CPP blocks delegate contravariance.
/// </summary>
public class SyntheticEventSetter<TEvent, TValue> where TEvent : PointerEventBase<TEvent>, new()
{
    private Action<TEvent, TValue> m_FastDelegate;
    private PropertyInfo m_Property;

    public SyntheticEventSetter(string propertyName)
    {
        m_Property = FindPropertyInHierarchy(typeof(TEvent), propertyName);

        if (m_Property != null && m_Property.SetMethod != null)
        {
            try
            {
                m_FastDelegate = (Action<TEvent, TValue>)Delegate.CreateDelegate(
                    typeof(Action<TEvent, TValue>),
                    m_Property.SetMethod,
                    throwOnBindFailure: false
                );
            }
            catch
            {
                m_FastDelegate = null;
            }
        }
        else
        {
            Debug.LogError($"[SyntheticEventSetter] Property '{propertyName}' not found on {typeof(TEvent).Name} hierarchy.");
        }
    }

    public void Set(TEvent evt, TValue value)
    {
        if (m_FastDelegate != null)
        {
            m_FastDelegate(evt, value);
        }
        else if (m_Property != null)
        {
            m_Property.SetValue(evt, value);
        }
    }

    private static PropertyInfo FindPropertyInHierarchy(Type type, string propertyName)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        Type curr = type;
        while (curr != null && curr != typeof(object))
        {
            var prop = curr.GetProperty(propertyName, flags);
            if (prop != null) return prop;
            curr = curr.BaseType;
        }
        return null;
    }
}

/// <summary>
/// Production Render Texture + Local UV Remapping Bridge for UI Toolkit in ARCore.
/// 
/// 1. Uses a fixed 1080x2400 reference RenderTexture (protects mobile GPU fill-rate).
/// 2. Projects UI onto an edge-to-edge 3D Quad tracking the AR camera frustum at 0.18m.
/// 3. Uses BoxCollider on Quad with local vector math (zero PhysX BVH rebuild on AR camera motion).
/// 4. Injects native synthetic events via empty GetPooled() (no legacy IMGUI wrapper bugs).
/// 5. Mirrors evt.position into evt.localPosition via zero-GC compiled delegates.
/// 6. Leaves evt.target = null and sends via panel.visualTree.SendEvent() for top-down picking.
/// 7. Uses panel.Pick() to ensure background taps pass cleanly to AR Foundation.
/// </summary>
public class ARUIRenderTextureBridge : MonoBehaviour
{
    [Header("Canvas Reference Resolution")]
    [SerializeField] private int referenceWidth = 1080;
    [SerializeField] private int referenceHeight = 2400;

    [Header("Camera & Frustum Tracking")]
    [Tooltip("Distance in meters in front of the AR Camera lens (must be > camera near clip 0.10m, but < reading distance 0.30m)")]
    [SerializeField] private float distanceFromCamera = 0.18f;

    [Header("Scene References")]
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private Camera arCamera;
    [SerializeField] private Transform hudQuadTransform;
    [SerializeField] private BoxCollider hudQuadCollider;

    private RenderTexture uiRenderTexture;
    private Material hudMaterial;
    private IPanel uiPanel;
    private bool isPointerActive = false;
    private VisualElement activeCapturedElement = null;
    private Vector2 lastPanelPosition;

    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private Matrix4x4 lastProjMatrix = Matrix4x4.zero;

    // Fast compiled delegates for protected setters (Zero GC, native invocation speed)
    private static SyntheticEventSetter<PointerDownEvent, Vector3> s_SetPosDown;
    private static SyntheticEventSetter<PointerDownEvent, Vector3> s_SetLocalPosDown;
    private static SyntheticEventSetter<PointerDownEvent, int>     s_SetIdDown;

    private static SyntheticEventSetter<PointerMoveEvent, Vector3> s_SetPosMove;
    private static SyntheticEventSetter<PointerMoveEvent, Vector3> s_SetLocalPosMove;
    private static SyntheticEventSetter<PointerMoveEvent, Vector3> s_SetDeltaMove;
    private static SyntheticEventSetter<PointerMoveEvent, int>     s_SetIdMove;

    private static SyntheticEventSetter<PointerUpEvent, Vector3>   s_SetPosUp;
    private static SyntheticEventSetter<PointerUpEvent, Vector3>   s_SetLocalPosUp;
    private static SyntheticEventSetter<PointerUpEvent, int>       s_SetIdUp;

    private void Awake()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();

        EnsureHUDQuad();
        InitializeFastDelegates();
        InitializeRenderTexture();
    }

    private void Start()
    {
        UpdateFrustumFit();
    }

    private void LateUpdate()
    {
        if (arCamera == null)
        {
            arCamera = Camera.main;
            if (arCamera == null) return;
        }

        Matrix4x4 currentProj = arCamera.projectionMatrix;
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight || currentProj != lastProjMatrix)
        {
            UpdateFrustumFit();
        }
    }

    private void EnsureHUDQuad()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (arCamera == null) return;

        if (hudQuadTransform == null)
        {
            Transform existing = arCamera.transform.Find("HUD_Quad");
            if (existing != null)
            {
                hudQuadTransform = existing;
                hudQuadCollider = existing.GetComponent<BoxCollider>();
            }
            else
            {
                GameObject quadGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quadGo.name = "HUD_Quad";
                quadGo.transform.SetParent(arCamera.transform, false);

                var meshCol = quadGo.GetComponent<Collider>();
                if (meshCol != null) SafeDestroy(meshCol);

                hudQuadCollider = quadGo.AddComponent<BoxCollider>();
                hudQuadCollider.size = new Vector3(1f, 1f, 0.02f);
                hudQuadCollider.isTrigger = false;

                int uiLayer = LayerMask.NameToLayer("UI");
                quadGo.layer = uiLayer >= 0 ? uiLayer : 5;

                hudQuadTransform = quadGo.transform;
            }
        }

        if (hudQuadCollider == null && hudQuadTransform != null)
        {
            hudQuadCollider = hudQuadTransform.GetComponent<BoxCollider>();
            if (hudQuadCollider == null)
            {
                var col = hudQuadTransform.GetComponent<Collider>();
                if (col != null) SafeDestroy(col);
                hudQuadCollider = hudQuadTransform.gameObject.AddComponent<BoxCollider>();
                hudQuadCollider.size = new Vector3(1f, 1f, 0.02f);
                hudQuadCollider.isTrigger = false;
            }
        }
    }

    public bool IsLandscape => Screen.width > Screen.height;

    public int CurrentReferenceWidth
    {
        get
        {
            if (Screen.width <= 0 || Screen.height <= 0) return referenceWidth;
            return IsLandscape
                ? Mathf.RoundToInt(1080f * (float)Screen.width / (float)Screen.height)
                : 1080;
        }
    }

    public int CurrentReferenceHeight
    {
        get
        {
            if (Screen.width <= 0 || Screen.height <= 0) return referenceHeight;
            return IsLandscape
                ? 1080
                : Mathf.RoundToInt(1080f * (float)Screen.height / (float)Screen.width);
        }
    }

    public void UpdateFrustumFit()
    {
        if (arCamera == null) arCamera = Camera.main;
        if (hudQuadTransform == null) EnsureHUDQuad();

        EnsureRenderTextureOrientation();

        if (arCamera == null || hudQuadTransform == null) return;

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        lastProjMatrix = arCamera.projectionMatrix;

        // Calculate optical frustum bounds in world space at distanceFromCamera
        Vector3 centerWorld = arCamera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, distanceFromCamera));
        Vector3 leftWorld   = arCamera.ViewportToWorldPoint(new Vector3(0.0f, 0.5f, distanceFromCamera));
        Vector3 rightWorld  = arCamera.ViewportToWorldPoint(new Vector3(1.0f, 0.5f, distanceFromCamera));
        Vector3 bottomWorld = arCamera.ViewportToWorldPoint(new Vector3(0.5f, 0.0f, distanceFromCamera));
        Vector3 topWorld    = arCamera.ViewportToWorldPoint(new Vector3(0.5f, 1.0f, distanceFromCamera));

        float frustumWidth = Vector3.Distance(leftWorld, rightWorld);
        float frustumHeight = Vector3.Distance(bottomWorld, topWorld);

        if (hudQuadTransform.parent == arCamera.transform)
        {
            hudQuadTransform.localPosition = arCamera.transform.InverseTransformPoint(centerWorld);
            hudQuadTransform.localRotation = Quaternion.identity;
        }
        else
        {
            hudQuadTransform.position = centerWorld;
            hudQuadTransform.rotation = arCamera.transform.rotation;
        }

        hudQuadTransform.localScale = new Vector3(frustumWidth, frustumHeight, 1.0f);
    }

    private void EnsureRenderTextureOrientation()
    {
        if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
        if (uiDocument == null) uiDocument = FindFirstObjectByType<UIDocument>();

        int targetW = CurrentReferenceWidth;
        int targetH = CurrentReferenceHeight;

        bool isTextureValid = false;
        try
        {
            isTextureValid = uiRenderTexture != null && uiRenderTexture.IsCreated();
        }
        catch
        {
            isTextureValid = false;
        }

        if (!isTextureValid)
        {
            uiRenderTexture = new RenderTexture(targetW, targetH, 0, RenderTextureFormat.ARGB32)
            {
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                name = "AR_UI_RenderTexture"
            };
            uiRenderTexture.Create();

            if (uiDocument != null && uiDocument.panelSettings != null)
            {
                uiDocument.panelSettings.targetTexture = uiRenderTexture;
                uiDocument.panelSettings.referenceResolution = new Vector2Int(targetW, targetH);
                uiDocument.panelSettings.renderMode = PanelRenderMode.ScreenSpaceOverlay;
                uiDocument.panelSettings.clearColor = true;
                uiDocument.panelSettings.colorClearValue = new Color(0, 0, 0, 0);
            }

            SetupQuadMaterial();
        }
        else if (uiRenderTexture.width != targetW || uiRenderTexture.height != targetH)
        {
            uiRenderTexture.Release();
            uiRenderTexture.width = targetW;
            uiRenderTexture.height = targetH;
            uiRenderTexture.Create();

            if (uiDocument != null && uiDocument.panelSettings != null)
            {
                uiDocument.panelSettings.referenceResolution = new Vector2Int(targetW, targetH);
            }

            Debug.Log($"[ARUIRenderTextureBridge] Resized UI RenderTexture for {(IsLandscape ? "LANDSCAPE" : "PORTRAIT")}: {targetW}x{targetH}");
        }
    }

    private static void InitializeFastDelegates()
    {
        if (s_SetPosDown != null) return; // Already initialized

        // PointerDown
        s_SetPosDown      = new SyntheticEventSetter<PointerDownEvent, Vector3>("position");
        s_SetLocalPosDown = new SyntheticEventSetter<PointerDownEvent, Vector3>("localPosition");
        s_SetIdDown       = new SyntheticEventSetter<PointerDownEvent, int>("pointerId");

        // PointerMove
        s_SetPosMove      = new SyntheticEventSetter<PointerMoveEvent, Vector3>("position");
        s_SetLocalPosMove = new SyntheticEventSetter<PointerMoveEvent, Vector3>("localPosition");
        s_SetDeltaMove    = new SyntheticEventSetter<PointerMoveEvent, Vector3>("deltaPosition");
        s_SetIdMove       = new SyntheticEventSetter<PointerMoveEvent, int>("pointerId");

        // PointerUp
        s_SetPosUp        = new SyntheticEventSetter<PointerUpEvent, Vector3>("position");
        s_SetLocalPosUp   = new SyntheticEventSetter<PointerUpEvent, Vector3>("localPosition");
        s_SetIdUp         = new SyntheticEventSetter<PointerUpEvent, int>("pointerId");
    }

    private void InitializeRenderTexture()
    {
        EnsureRenderTextureOrientation();
    }

    private void SetupQuadMaterial()
    {
        if (hudQuadTransform == null || uiRenderTexture == null) return;

        var meshRenderer = hudQuadTransform.GetComponent<MeshRenderer>();
        if (meshRenderer == null) return;

        Shader shader = Shader.Find("AksharAR/TransparentUnlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        hudMaterial = new Material(shader);
        hudMaterial.mainTexture = uiRenderTexture;
        if (hudMaterial.HasProperty("_BaseMap"))
        {
            hudMaterial.SetTexture("_BaseMap", uiRenderTexture);
        }
        hudMaterial.renderQueue = 3050;

        // If using fallback URP Unlit shader, configure transparent blending
        if (hudMaterial.HasProperty("_Surface")) hudMaterial.SetFloat("_Surface", 1);
        if (hudMaterial.HasProperty("_Blend")) hudMaterial.SetFloat("_Blend", 0);
        hudMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

        meshRenderer.material = hudMaterial;
    }

    private void Update()
    {
        if (uiDocument == null || uiDocument.rootVisualElement == null) return;
        if (uiPanel == null || uiPanel != uiDocument.rootVisualElement.panel) uiPanel = uiDocument.rootVisualElement.panel;
        if (uiPanel == null || uiPanel.visualTree == null) return;

        ProcessTouchInput();
    }

    private void ProcessTouchInput()
    {
        if (uiPanel == null || uiPanel.visualTree == null) return;

        bool hasPointer = false;
        Vector2 screenPos = Vector2.zero;
        bool isDown = false;
        bool isUp = false;
        int pointerId = 0;

#if ENABLE_INPUT_SYSTEM
        var touchscreen = Touchscreen.current;
        if (touchscreen != null && touchscreen.primaryTouch.press.isPressed)
        {
            hasPointer = true;
            screenPos = touchscreen.primaryTouch.position.ReadValue();
            isDown = touchscreen.primaryTouch.press.wasPressedThisFrame;
            isUp = touchscreen.primaryTouch.press.wasReleasedThisFrame;
            pointerId = PointerId.mousePointerId; // Safe 0 index for UI Toolkit pointer table
        }
        else if (touchscreen != null && touchscreen.primaryTouch.press.wasReleasedThisFrame)
        {
            hasPointer = true;
            screenPos = touchscreen.primaryTouch.position.ReadValue();
            isUp = true;
            pointerId = PointerId.mousePointerId; // Safe 0 index
        }
        else
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    hasPointer = true;
                    screenPos = mouse.position.ReadValue();
                    isDown = true;
                    pointerId = PointerId.mousePointerId;
                }
                else if (mouse.leftButton.wasReleasedThisFrame)
                {
                    hasPointer = true;
                    screenPos = mouse.position.ReadValue();
                    isUp = true;
                    pointerId = PointerId.mousePointerId;
                }
                else if (mouse.leftButton.isPressed)
                {
                    hasPointer = true;
                    screenPos = mouse.position.ReadValue();
                    pointerId = PointerId.mousePointerId;
                }
            }
        }
#else
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            hasPointer = true;
            screenPos = touch.position;
            isDown = touch.phase == TouchPhase.Began;
            isUp = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
            pointerId = Mathf.Clamp(touch.fingerId, 0, 9);
        }
        else if (Input.GetMouseButton(0) || Input.GetMouseButtonDown(0) || Input.GetMouseButtonUp(0))
        {
            hasPointer = true;
            screenPos = Input.mousePosition;
            isDown = Input.GetMouseButtonDown(0);
            isUp = Input.GetMouseButtonUp(0);
            pointerId = PointerId.mousePointerId;
        }
#endif

        if (!hasPointer)
        {
            if (isPointerActive)
            {
                if (activeCapturedElement != null)
                {
                    InjectPointerUp(activeCapturedElement, lastPanelPosition, pointerId);
                    activeCapturedElement = null;
                }
                isPointerActive = false;
            }
            return;
        }

        if (Screen.width <= 0 || Screen.height <= 0) return;

        // Exact mapping from screen coordinates to UI Toolkit panel space
        // Since HUD_Quad is fitted edge-to-edge to the camera viewport:
        // u in [0, 1] maps left-to-right, v in [0, 1] maps bottom-to-top
        // Panel X = u * referenceWidth, Panel Y = (1.0 - v) * referenceHeight
        float u = Mathf.Clamp01(screenPos.x / (float)Screen.width);
        float v = Mathf.Clamp01(screenPos.y / (float)Screen.height);

        float panelX = u * CurrentReferenceWidth;
        float panelY = (1.0f - v) * CurrentReferenceHeight;
        Vector2 panelPosition = new Vector2(panelX, panelY);

        if (isDown)
        {
            VisualElement rawHit = uiPanel.Pick(panelPosition);
            VisualElement target = ResolveInteractiveTarget(rawHit);

            if (target != null)
            {
                activeCapturedElement = target;
                isPointerActive = true;
                lastPanelPosition = panelPosition;
                Debug.Log($"[ARUIRenderTextureBridge] PointerDown -> Target: '{target.name}' ({target.GetType().Name}) at {panelPosition}");
                InjectPointerDown(target, panelPosition, pointerId);
            }
        }
        else if (isUp)
        {
            if (isPointerActive)
            {
                VisualElement target = activeCapturedElement ?? ResolveInteractiveTarget(uiPanel.Pick(panelPosition));
                if (target != null)
                {
                    Debug.Log($"[ARUIRenderTextureBridge] PointerUp -> Target: '{target.name}' ({target.GetType().Name}) at {panelPosition}");
                    InjectPointerUp(target, panelPosition, pointerId);
                }
                activeCapturedElement = null;
                isPointerActive = false;
            }
        }
        else // Dragging / moving
        {
            if (isPointerActive && activeCapturedElement != null)
            {
                Vector2 delta = panelPosition - lastPanelPosition;
                InjectPointerMove(activeCapturedElement, panelPosition, delta, pointerId);
                lastPanelPosition = panelPosition;
            }
        }
    }

    private VisualElement ResolveInteractiveTarget(VisualElement hit)
    {
        if (hit == null || hit == uiPanel.visualTree) return null;

        // Walk up from hit to find an interactive element
        VisualElement curr = hit;
        while (curr != null && curr != uiPanel.visualTree)
        {
            if (curr is Button) return curr;
            if (curr.ClassListContains("wizard-option-card")) return curr;
            if (curr.name == "BottomHandle" || curr.name == "RightHandle" || curr.name == "CornerHandle") return curr;
            curr = curr.parent;
        }

        if (hit.pickingMode == PickingMode.Position && hit != uiPanel.visualTree)
        {
            return hit;
        }

        return null;
    }

    private static void EnsureFastDelegates()
    {
        if (s_SetPosDown == null)
        {
            InitializeFastDelegates();
        }
    }

    private void InjectPointerDown(VisualElement target, Vector2 panelPosition, int pointerId)
    {
        EnsureFastDelegates();
        using (var evt = PointerDownEvent.GetPooled())
        {
            Vector3 pos3D = panelPosition;
            s_SetPosDown?.Set(evt, pos3D);
            s_SetLocalPosDown?.Set(evt, pos3D); // Exact mirror
            s_SetIdDown?.Set(evt, pointerId);
            target.SendEvent(evt);
        }
    }

    private void InjectPointerMove(VisualElement target, Vector2 panelPosition, Vector2 delta, int pointerId)
    {
        EnsureFastDelegates();
        using (var evt = PointerMoveEvent.GetPooled())
        {
            Vector3 pos3D = panelPosition;
            s_SetPosMove?.Set(evt, pos3D);
            s_SetLocalPosMove?.Set(evt, pos3D);
            s_SetDeltaMove?.Set(evt, (Vector3)delta);
            s_SetIdMove?.Set(evt, pointerId);
            target.SendEvent(evt);
        }
    }

    private void InjectPointerUp(VisualElement target, Vector2 panelPosition, int pointerId)
    {
        EnsureFastDelegates();
        using (var evt = PointerUpEvent.GetPooled())
        {
            Vector3 pos3D = panelPosition;
            s_SetPosUp?.Set(evt, pos3D);
            s_SetLocalPosUp?.Set(evt, pos3D);
            s_SetIdUp?.Set(evt, pointerId);
            target.SendEvent(evt);
        }

        // Also inject ClickEvent so standard Button.clicked handlers fire
        using (var clickEvt = ClickEvent.GetPooled())
        {
            target.SendEvent(clickEvt);
        }
    }

    private static void SafeDestroy(UnityEngine.Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }

    private void OnDestroy()
    {
        if (uiRenderTexture != null)
        {
            uiRenderTexture.Release();
            SafeDestroy(uiRenderTexture);
        }
        if (hudMaterial != null)
        {
            SafeDestroy(hudMaterial);
        }
    }
}

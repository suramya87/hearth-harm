using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Orthographic 2D camera controller.
/// Replaces FreeTacticsCameraController (which was 3D X/Z based).
///
/// FEATURES
///   - Keyboard / drag pan
///   - Scroll-wheel zoom (zoom-to-cursor)
///   - Room bounds clamping
///   - Snap to player on room transition
///   - Screen-shake
/// </summary>
public class CameraController2D : MonoBehaviour
{
    public static CameraController2D Instance { get; private set; }

    [Header("Camera")]
    [SerializeField] private Camera cam;

    [Header("Pan")]
    [SerializeField] private float panSpeed     = 8f;
    [SerializeField] private float dragPanSpeed = 0.015f;

    [Header("Edge Scrolling")]
    [SerializeField] private bool useEdgeScroll = true;
    [SerializeField] private float edgeSize = 25f;     
    [SerializeField] private float edgePanSpeed = 8f;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 4f;
    [SerializeField] private float orthoMin  = 3f;
    [SerializeField] private float orthoMax  = 16f;

    [Header("Snap")]
    [SerializeField] private float snapSmoothness = 6f;

    [Header("Screen Shake")]
    [SerializeField] private float shakeFrequency = 25f;

    private Vector3 basePos;
    private Vector3 shakeOffset;
    private float   targetOrtho;
    private bool    snapping;
    private Vector2 lastMouse;

    private Bounds  roomBounds;
    private bool    hasBounds;

    private float   shakeTime, shakeDur, shakeAmp;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        if (cam == null) cam = Camera.main;
        targetOrtho = cam != null ? cam.orthographicSize : 8f;
        basePos     = transform.position;
    }

    private void Update()
    {
        if (HasManualInput()) snapping = false;

        if (snapping) DoSnap();
        else          DoPan();

        DoZoom();
        ClampToRoom();
        transform.position = basePos;
    }

    private void LateUpdate()
    {
        DoShake();
        transform.position = basePos + shakeOffset;
    }

    public void SetRoomBounds(Bounds b) { roomBounds = b; hasBounds = true; }
    public void SnapToTarget()          { snapping = true; }

    public void TriggerShake(float amplitude, float duration)
    {
        shakeAmp  = amplitude;
        shakeDur  = duration;
        shakeTime = duration;
    }
    private bool HasManualInput() =>
        Input.GetAxisRaw("Horizontal") != 0 ||
        Input.GetAxisRaw("Vertical")   != 0 ||
        Input.GetMouseButton(1);

    private void DoPan()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        Vector2 edge = GetEdgeScrollInput();

        // Combine inputs (keyboard + edge)
        Vector2 moveInput = new Vector2(h, v) + edge;

        // Normalize so diagonal isn't faster
        if (moveInput.sqrMagnitude > 1f)
            moveInput.Normalize();

        // Apply movement
        float speed = edge != Vector2.zero ? edgePanSpeed : panSpeed;
        basePos += new Vector3(moveInput.x, moveInput.y, 0f) * speed * Time.deltaTime;

        if (Input.GetMouseButtonDown(1)) lastMouse = Input.mousePosition;
        if (Input.GetMouseButton(1))
        {
            Vector2 delta = (Vector2)Input.mousePosition - lastMouse;
            lastMouse = Input.mousePosition;
            basePos -= new Vector3(delta.x, delta.y, 0f) * dragPanSpeed;
        }
    }

    private Vector2 GetEdgeScrollInput()
    {
        if (!useEdgeScroll) return Vector2.zero;

        if (Input.GetMouseButton(1)) return Vector2.zero;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return Vector2.zero;

        Vector2 input = Vector2.zero;
        Vector3 mouse = Input.mousePosition;

        if (mouse.x <= edgeSize) input.x = -1f;
        else if (mouse.x >= Screen.width - edgeSize) input.x = 1f;

        if (mouse.y <= edgeSize) input.y = -1f;
        else if (mouse.y >= Screen.height - edgeSize) input.y = 1f;

        return input;
    }

    private void DoSnap()
    {
        var player = FindLocalPlayer();
        if (player == null) { snapping = false; return; }

        Vector3 target = player.position;
        target.z = basePos.z;
        basePos   = Vector3.Lerp(basePos, target, snapSmoothness * Time.deltaTime);
        if (Vector3.Distance(basePos, target) < 0.02f) { basePos = target; snapping = false; }
    }

    private void DoZoom()
    {
        if (cam == null) return;
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.01f) return;

        Vector3 before = MouseToWorld();
        targetOrtho    = Mathf.Clamp(targetOrtho - scroll * zoomSpeed, orthoMin, orthoMax);
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetOrtho, Time.deltaTime * 10f);
        basePos += before - MouseToWorld();
    }

    private Vector3 MouseToWorld()
    {
        if (cam == null) return Vector3.zero;
        var ray   = cam.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(Vector3.forward, Vector3.zero);
        plane.Raycast(ray, out float d);
        return ray.GetPoint(d);
    }

    private void ClampToRoom()
    {
        if (!hasBounds || cam == null) return;
        float h = cam.orthographicSize;
        float w = h * cam.aspect;
        basePos.x = Mathf.Clamp(basePos.x, roomBounds.min.x + w, roomBounds.max.x - w);
        basePos.y = Mathf.Clamp(basePos.y, roomBounds.min.y + h, roomBounds.max.y - h);
    }

    private void DoShake()
    {
        if (shakeTime <= 0f) { shakeOffset = Vector3.zero; return; }
        shakeTime -= Time.deltaTime;
        float t   = shakeTime / shakeDur;
        float str = shakeAmp * t;
        float nx  = (Mathf.PerlinNoise(Time.time * shakeFrequency, 0f) - 0.5f) * 2f;
        float ny  = (Mathf.PerlinNoise(0f, Time.time * shakeFrequency) - 0.5f) * 2f;
        shakeOffset = new Vector3(nx, ny, 0f) * str;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Transform FindLocalPlayer()
    {
        var pt = PlayerTarget.Instance;
        return pt != null ? pt.transform : null;
    }
}
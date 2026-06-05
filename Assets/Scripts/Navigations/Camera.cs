using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Orthographic 2D camera controller.
///
/// MODES:
///   Room mode   (hasBounds = true)  — camera is clamped within room bounds.
///                                     Player can pan freely within bounds.
///   Hallway mode (hasBounds = false) — camera continuously follows the player.
///                                     No clamping, no manual pan (player is moving anyway).
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
    [SerializeField] private bool  useEdgeScroll = true;
    [SerializeField] private float edgeSize      = 25f;
    [SerializeField] private float edgePanSpeed  = 8f;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 4f;
    [SerializeField] private float orthoMin  = 3f;
    [SerializeField] private float orthoMax  = 16f;

    [Header("Snap / Follow")]
    [SerializeField] private float snapSmoothness   = 6f;
    [SerializeField] private float hallwayFollowSpeed = 10f;

    [Header("Turn Follow")]
    [SerializeField] private bool  followEnemyTurns           = true;
    [SerializeField] private bool  lockCameraDuringEnemyTurns = true;
    [SerializeField] private bool  recenterOnPlayerTurn       = true;
    [SerializeField] private float followSmoothness           = 8f;
    [SerializeField] private float unlockDistance             = 0.05f;

    [Header("Screen Shake")]
    [SerializeField] private float shakeFrequency = 25f;

    // ── State ──────────────────────────────────────────────────────────────

    private Transform followTarget;
    private bool      cameraInputLocked;
    private bool      unlockWhenCentered;

    private Vector3 basePos;
    private Vector3 shakeOffset;
    private float   targetOrtho;
    private bool    snapping;
    private Vector2 lastMouse;

    private Bounds roomBounds;
    private bool   hasBounds;       // false = hallway mode
    private bool   followingPlayer; // true = continuously follow local player

    private float shakeTime, shakeDur, shakeAmp;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance    = this;
        if (cam == null) cam = Camera.main;
        targetOrtho = cam != null ? cam.orthographicSize : 8f;
        basePos     = transform.position;
    }

    private void Start()
    {
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnStarted += HandleEnemyTurnStarted;
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnPlayerTurnBegin += HandlePlayerTurnBegin;
    }

    private void OnDestroy()
    {
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnStarted -= HandleEnemyTurnStarted;
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnPlayerTurnBegin -= HandlePlayerTurnBegin;
    }

    // ── Update ─────────────────────────────────────────────────────────────

    private void Update()
    {
        // ── Hallway follow mode — overrides everything else ────────────────
        if (followingPlayer)
        {
            FollowLocalPlayer();
            DoZoom();
            transform.position = basePos;
            return;
        }

        // ── Normal room mode ───────────────────────────────────────────────
        if (followTarget != null)
        {
            if (!cameraInputLocked && HasManualInput())
            {
                followTarget       = null;
                unlockWhenCentered = false;
            }
            else
            {
                DoFollowTarget();

                if (unlockWhenCentered && IsCenteredOnFollowTarget())
                {
                    followTarget       = null;
                    cameraInputLocked  = false;
                    unlockWhenCentered = false;
                }
            }
        }
        else
        {
            if (!cameraInputLocked && HasManualInput())
                snapping = false;

            if (snapping)
                DoSnap();
            else if (!cameraInputLocked)
                DoPan();
        }

        if (!cameraInputLocked)
            DoZoom();

        if (hasBounds)
            ClampToRoom();

        transform.position = basePos;
    }

    private void LateUpdate()
    {
        DoShake();
        transform.position = basePos + shakeOffset;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Enter room mode: apply bounds and snap once to player.</summary>
    public void SetRoomBounds(Bounds b)
    {
        roomBounds      = b;
        hasBounds       = true;
        followingPlayer = false;
        snapping        = true;
    }

    /// <summary>
    /// Enter hallway mode: clear bounds and continuously follow the player.
    /// </summary>
    public void ClearRoomBounds()
    {
        hasBounds       = false;
        followingPlayer = true;
        snapping        = false;
    }

    /// <summary>One-shot snap toward the player (room mode only).</summary>
    public void SnapToTarget()
    {
        if (followingPlayer) return; // hallway mode already follows continuously
        snapping = true;
    }

    public void TriggerShake(float amplitude, float duration)
    {
        shakeAmp  = amplitude;
        shakeDur  = duration;
        shakeTime = duration;
    }

    public void SoftFocusOn(Transform target)
    {
        if (target == null) return;
        followTarget       = target;
        cameraInputLocked  = false;
        unlockWhenCentered = true;
        snapping           = false;
    }

    // ── Hallway continuous follow ──────────────────────────────────────────

    private void FollowLocalPlayer()
    {
        var player = FindLocalPlayer();
        if (player == null) return;

        Vector3 target = player.position;
        target.z = basePos.z;
        basePos  = Vector3.Lerp(basePos, target, hallwayFollowSpeed * Time.deltaTime);
    }

    // ── Room movement ──────────────────────────────────────────────────────

    private bool HasManualInput() =>
        Input.GetAxisRaw("Horizontal") != 0 ||
        Input.GetAxisRaw("Vertical")   != 0 ||
        Input.GetMouseButton(1);

    private void DoPan()
    {
        float   h     = Input.GetAxisRaw("Horizontal");
        float   v     = Input.GetAxisRaw("Vertical");
        Vector2 edge  = GetEdgeScrollInput();
        Vector2 input = new Vector2(h, v) + edge;
        if (input.sqrMagnitude > 1f) input.Normalize();

        float speed = edge != Vector2.zero ? edgePanSpeed : panSpeed;
        basePos += new Vector3(input.x, input.y, 0f) * speed * Time.deltaTime;

        if (Input.GetMouseButtonDown(1)) lastMouse = Input.mousePosition;
        if (Input.GetMouseButton(1))
        {
            Vector2 delta = (Vector2)Input.mousePosition - lastMouse;
            lastMouse     = Input.mousePosition;
            basePos      -= new Vector3(delta.x, delta.y, 0f) * dragPanSpeed;
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

        if (mouse.x <= edgeSize)                      input.x = -1f;
        else if (mouse.x >= Screen.width - edgeSize)  input.x =  1f;
        if (mouse.y <= edgeSize)                      input.y = -1f;
        else if (mouse.y >= Screen.height - edgeSize) input.y =  1f;

        return input;
    }

    private void DoSnap()
    {
        var player = FindLocalPlayer();
        if (player == null) { snapping = false; return; }

        Vector3 target = player.position;
        target.z = basePos.z;
        basePos  = Vector3.Lerp(basePos, target, snapSmoothness * Time.deltaTime);
        if (Vector3.Distance(basePos, target) < 0.02f) { basePos = target; snapping = false; }
    }

    private void DoFollowTarget()
    {
        if (followTarget == null) return;
        Vector3 target = followTarget.position;
        target.z = basePos.z;
        basePos  = Vector3.Lerp(basePos, target, followSmoothness * Time.deltaTime);
    }

    private bool IsCenteredOnFollowTarget()
    {
        if (followTarget == null) return true;
        return Vector2.Distance(basePos, (Vector2)followTarget.position) <= unlockDistance;
    }

    private void DoZoom()
    {
        if (cam == null) return;
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) < 0.01f) return;

        Vector3 before       = MouseToWorld();
        targetOrtho          = Mathf.Clamp(targetOrtho - scroll * zoomSpeed, orthoMin, orthoMax);
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetOrtho, Time.deltaTime * 10f);
        basePos             += before - MouseToWorld();
    }

    private void ClampToRoom()
    {
        if (cam == null) return;
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

    private Vector3 MouseToWorld()
    {
        if (cam == null) return Vector3.zero;
        var ray   = cam.ScreenPointToRay(Input.mousePosition);
        var plane = new Plane(Vector3.forward, Vector3.zero);
        plane.Raycast(ray, out float d);
        return ray.GetPoint(d);
    }

    // ── Turn events ────────────────────────────────────────────────────────

    private void HandleEnemyTurnStarted(EnemyUnit enemy)
    {
        if (!followEnemyTurns || enemy == null) return;
        followTarget       = enemy.transform;
        cameraInputLocked  = lockCameraDuringEnemyTurns;
        unlockWhenCentered = false;
        snapping           = false;
    }

    private void HandlePlayerTurnBegin()
    {
        cameraInputLocked  = false;
        followTarget       = null;
        unlockWhenCentered = false;
        if (recenterOnPlayerTurn) snapping = true;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Transform FindLocalPlayer()
    {
        var pt = PlayerTarget.Instance;
        return pt != null ? pt.transform : null;
    }
}
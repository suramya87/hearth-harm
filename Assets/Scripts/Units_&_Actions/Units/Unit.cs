using System;
using System.Collections;
using UnityEngine;

public class Unit : MonoBehaviour
{
    [Header("Visual Settings")]
    [Tooltip("Adjust this to center the sprite in the tile (usually 0.5, 0.5)")]
    [SerializeField] private Vector2 visualOffset = new Vector2(0.5f, 0.5f);

    private GridPosition gridPosition;
    private RoomGrid     currentRoomGrid;
    private bool         isInitialized;

    private MoveAction   moveAction;
    private BaseAction[] allActions;
    private PlayerStats  playerStats;

    internal bool IsSyncingFromNetwork { get; set; }

    private void Awake()
    {
        moveAction  = GetComponent<MoveAction>();
        allActions  = GetComponents<BaseAction>();
        playerStats = GetComponent<PlayerStats>();
    }

    private void Start()
    {
        if (GameManager.IsMultiplayer)
            StartCoroutine(SubscribeToNetworkedTurnSystem());
        else if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged += OnTurnChanged;
    }

    private IEnumerator SubscribeToNetworkedTurnSystem()
    {
        while (NetworkedTurnSystem.Instance == null) yield return null;
        NetworkedTurnSystem.Instance.OnTurnChanged += OnTurnChanged;
    }

    private void OnDestroy()
    {
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
        if (NetworkedTurnSystem.Instance != null)
            NetworkedTurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
    }

    // ── Grid placement ─────────────────────────────────────────────────────

    /// <summary>
    /// Waits until the room grid is fully initialized then places the unit.
    /// Use this for initial spawn.
    /// </summary>
    public void PlaceInRoomWhenReady(RoomGrid room, GridPosition pos)
    {
        StartCoroutine(WaitAndPlace(room, pos));
    }

    public void PlaceInRoomNoMove(RoomGrid room, GridPosition newPos)
    {
        if (room == null) return;

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);

        currentRoomGrid = room;
        gridPosition    = newPos;
        isInitialized   = true;

        room.AddUnitAtGridPosition(newPos, this);
    }

    private IEnumerator WaitAndPlace(RoomGrid room, GridPosition pos)
    {
        if (room == null)
        {
            Debug.LogError("[Unit] PlaceInRoomWhenReady called with null room!");
            yield break;
        }

        float timeout = 5f;
        float elapsed = 0f;
        while (!room.IsInitialized())
        {
            elapsed += Time.deltaTime;
            if (elapsed >= timeout)
            {
                Debug.LogError($"[Unit] Timed out waiting for {room.gameObject.name} to initialize!");
                yield break;
            }
            yield return null;
        }

        yield return null;

        PlaceInRoom(room, pos);

        // After placement, register with local-player systems if we are the owner.
        RegisterWithLocalSystems();

        Debug.Log($"[Unit] Placed in {room.gameObject.name} at {pos} after grid ready.");
    }

    /// <summary>
    /// Immediately places the unit in a room. Room must already be initialized.
    /// For initial spawn use PlaceInRoomWhenReady instead.
    /// </summary>
    public void PlaceInRoom(RoomGrid room, GridPosition newPos)
    {
        if (room == null)
        {
            Debug.LogError("[Unit] PlaceInRoom called with null room!");
            return;
        }

        if (!room.IsInitialized())
            Debug.LogWarning($"[Unit] PlaceInRoom called on uninitialized grid {room.gameObject.name}.");

        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);

        currentRoomGrid = room;
        gridPosition    = newPos;
        isInitialized   = true;

        room.AddUnitAtGridPosition(newPos, this);

        if (!IsSyncingFromNetwork)
        {
            Vector3 world = room.GetWorldPosition(newPos);
            transform.position = new Vector3(
                world.x + visualOffset.x,
                world.y + visualOffset.y,
                transform.position.z
            );
        }

        if (GameManager.IsMultiplayer && !IsSyncingFromNetwork)
        {
            var bridge = GetComponent<NetworkedPlayerBridge>();
            if (bridge != null && bridge.IsSpawned && bridge.IsOwner)
                bridge.SyncGridPosition(room, newPos);
        }
    }

    // ── Local systems registration ─────────────────────────────────────────

    /// <summary>
    /// Register this unit with PlayerTarget and UnitActionSystem when it is
    /// the locally owned player unit (or the only unit in singleplayer).
    /// </summary>
    private void RegisterWithLocalSystems()
    {
        bool isLocalOwner;

        if (!GameManager.IsMultiplayer)
        {
            isLocalOwner = true;
        }
        else
        {
            var netObj = GetComponent<Unity.Netcode.NetworkObject>();
            isLocalOwner = netObj != null && netObj.IsOwner;
        }

        if (!isLocalOwner) return;

        // Register with PlayerTarget so enemies and camera find us.
        if (PlayerTarget.Instance != null)
            PlayerTarget.Instance.Register(this);

        // Register with UnitActionSystem so input reaches us.
        if (UnitActionSystem.Instance != null)
            UnitActionSystem.Instance.SetSelectedUnit(this);
    }

    // ── Turn events ────────────────────────────────────────────────────────

    private void OnTurnChanged(object sender, EventArgs e) { }

    // ── Accessors ──────────────────────────────────────────────────────────

    public GridPosition  GetGridPosition()    => isInitialized ? gridPosition : default;
    public RoomGrid      GetCurrentRoomGrid() => currentRoomGrid;
    public bool          IsInitialized()      => isInitialized;
    public MoveAction    GetMoveAction()      => moveAction;
    public BaseAction[]  GetBaseActionArray() => allActions;
    public Vector2       GetVisualOffset()    => visualOffset;
}
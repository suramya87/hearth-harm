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

    private void OnEnable()
    {
        if (GetComponent<PlayerStats>() != null)
            PartyManager.Instance?.RegisterUnit(this);
    }

    private void OnDisable()
    {
        PartyManager.Instance?.UnregisterUnit(this);
    }

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
    /// Use this for initial spawn to avoid timing issues where the grid
    /// isn't ready yet when the level first loads.
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
            // deliberately no transform.position change — MoveAction owns the visual position
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

        // One extra frame so TilemapRoomGrid cell bounds are fully settled
        yield return null;

        PlaceInRoom(room, pos);
        Debug.Log($"[Unit] Placed in {room.gameObject.name} at {pos} after grid ready.");
    }

    /// <summary>
    /// Immediately places the unit in a room. The room must already be initialized.
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
        {
            Debug.LogWarning($"[Unit] PlaceInRoom called on uninitialized grid " +
                             $"{room.gameObject.name}. Use PlaceInRoomWhenReady for initial spawn.");
        }

        // Remove from old cell
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

    // ── Turn events ────────────────────────────────────────────────────────

    private void OnTurnChanged(object sender, EventArgs e)
    {
        
    }

    // ── Accessors ──────────────────────────────────────────────────────────

    public GridPosition  GetGridPosition()    => isInitialized ? gridPosition : default;
    public RoomGrid      GetCurrentRoomGrid() => currentRoomGrid;
    public bool          IsInitialized()      => isInitialized;
    public MoveAction    GetMoveAction()      => moveAction;
    public BaseAction[]  GetBaseActionArray() => allActions;
    public Vector2 GetVisualOffset() => visualOffset;
}
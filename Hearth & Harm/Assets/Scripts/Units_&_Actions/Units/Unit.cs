using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Represents the player unit on the grid.
///
/// SIMPLIFIED — NO SWITCHGRID:
/// Because hallway tiles are now painted into room tilemaps, the unit never
/// needs to switch grids when entering a hallway. WorldRoomTracker calls
/// SetGridState() when the player crosses into a different room's tiles —
/// this is a lightweight update that keeps gridPosition and currentRoomGrid
/// in sync without any occupancy juggling or coordinate remapping.
/// </summary>
public class Unit : MonoBehaviour
{
    [Header("Visual Settings")]
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
    /// Places the unit at a specific grid position, snapping world position.
    /// Used for spawning, room entry via RoomDoor, and end-of-move registration.
    /// </summary>
    public void PlaceInRoom(RoomGrid room, GridPosition newPos)
    {
        if (room == null) { Debug.LogWarning("[Unit] PlaceInRoom: null room."); return; }

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
                transform.position.z);
        }

        if (GameManager.IsMultiplayer && !IsSyncingFromNetwork)
        {
            var bridge = GetComponent<NetworkedPlayerBridge>();
            if (bridge != null && bridge.IsSpawned && bridge.IsOwner)
                bridge.SyncGridPosition(room, newPos);
        }
    }

    /// <summary>
    /// Lightweight grid/position update called by WorldRoomTracker when the
    /// player's world position crosses into a different room's tile set.
    ///
    /// Does NOT snap transform.position — the player walked here physically.
    /// Occupancy is managed by the caller (WorldRoomTracker) so this just
    /// updates the cached references.
    /// </summary>
    public void SetGridState(RoomGrid grid, GridPosition pos)
    {
        currentRoomGrid = grid;
        gridPosition    = pos;
        isInitialized   = true;
    }

    /// <summary>
    /// Reference-only grid update. Does not touch gridPosition or occupancy.
    /// Use SetGridState when you have a valid new position; use this only when
    /// you need to point at a grid without knowing the cell yet.
    /// </summary>
    public void SetCurrentRoomGrid(RoomGrid grid)
    {
        if (grid != null) currentRoomGrid = grid;
    }

    /// <summary>
    /// Compatibility alias — hallways are now part of room grids so this is
    /// identical to PlaceInRoom. Kept so any existing call sites compile.
    /// </summary>
    public void PlaceInHallway(RoomGrid grid) => PlaceInRoom(grid, grid.GetGridPosition(transform.position));

    /// <summary>
    /// Legacy compatibility — calls PlaceInRoom with a world-derived position.
    /// Kept so any existing SwitchGrid call sites compile.
    /// </summary>
    public void SwitchGrid(RoomGrid newGrid)
    {
        if (newGrid == null || !newGrid.IsInitialized()) return;
        var newCell = newGrid.GetGridPosition(transform.position);
        if (!newGrid.IsValidGridPosition(newCell))
            newCell = FindNearestValidCell(newGrid, transform.position);
        if (newGrid.IsValidGridPosition(newCell))
            PlaceInRoom(newGrid, newCell);
    }

    // ── Nearest valid cell ─────────────────────────────────────────────────

    private static GridPosition FindNearestValidCell(RoomGrid grid, Vector3 worldPos)
    {
        var center = grid.GetGridPosition(worldPos);
        for (int r = 1; r <= 5; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
            var c = new GridPosition(center.x + dx, center.y + dy);
            if (grid.IsValidGridPosition(c)) return c;
        }
        return center;
    }

    // ── Turn events ────────────────────────────────────────────────────────

    private void OnTurnChanged(object sender, EventArgs e)
    {
        if (playerStats != null)
            playerStats.SetCurrentStaminaPoints(playerStats.GetMaxStaminaPoints());
    }

    // ── Accessors ──────────────────────────────────────────────────────────

    public GridPosition  GetGridPosition()    => isInitialized ? gridPosition : default;
    public RoomGrid      GetCurrentRoomGrid() => currentRoomGrid;
    public bool          IsInitialized()      => isInitialized;
    public MoveAction    GetMoveAction()      => moveAction;
    public BaseAction[]  GetBaseActionArray() => allActions;
}
using System;
using UnityEngine;

/// <summary>
/// Represents any player-controlled character on the grid.
///
/// 2D CHANGES
///   - PlaceInRoom sets transform.position in XY (Z unchanged for sorting).
///   - GetGridPosition reads from TilemapRoomGrid directly.
/// </summary>
public class Unit : MonoBehaviour
{
    private GridPosition  gridPosition;
    private RoomGrid      currentRoomGrid;
    private bool          isInitialized;

    private MoveAction    moveAction;
    private BaseAction[]  allActions;
    private PlayerStats   playerStats;

    private void Awake()
    {
        moveAction  = GetComponent<MoveAction>();
        allActions  = GetComponents<BaseAction>();
        playerStats = GetComponent<PlayerStats>();
    }

    private void Start()
    {
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged += OnTurnChanged;
    }

    private void OnDestroy()
    {
        if (TurnSystem.Instance != null)
            TurnSystem.Instance.OnTurnChanged -= OnTurnChanged;
    }

    // ── Grid placement ─────────────────────────────────────────────────────

    /// <summary>Move this unit to a grid position inside a room.</summary>
    public void PlaceInRoom(RoomGrid room, GridPosition newPos)
    {
        if (currentRoomGrid != null && isInitialized)
            currentRoomGrid.RemoveUnitAtGridPosition(gridPosition, this);

        currentRoomGrid = room;
        gridPosition    = newPos;

        Vector3 world = room.GetWorldPosition(newPos);
        // Preserve Z so sprite sorting layers stay intact
        transform.position = new Vector3(world.x, world.y, transform.position.z);

        room.AddUnitAtGridPosition(newPos, this);
        isInitialized = true;
    }

    // ── Turn events ────────────────────────────────────────────────────────

    private void OnTurnChanged(object sender, EventArgs e)
    {
        if (playerStats != null)
            playerStats.SetCurrentStaminaPoints(playerStats.GetMaxStaminaPoints());
    }

    // ── Accessors ──────────────────────────────────────────────────────────

    public GridPosition  GetGridPosition()   => isInitialized ? gridPosition : default;
    public RoomGrid      GetCurrentRoomGrid() => currentRoomGrid;
    public bool          IsInitialized()      => isInitialized;
    public MoveAction    GetMoveAction()      => moveAction;
    public BaseAction[]  GetBaseActionArray() => allActions;
}
using UnityEngine;

/// <summary>
/// Tracks which room or hallway the player is currently in.
///
/// UNIFIED API:
/// HallwayRoomBridge now calls SetCurrentRoom() for BOTH rooms and hallways,
/// passing either a real PlacedRoom or a hallway's AsPlacedRoom() descriptor.
/// SetCurrentRoom detects IsHallway and routes internally so the rest of the
/// codebase (TilemapHighlighter, MoveAction) never needs to know the difference.
///
/// GetCurrentRoomGrid() always returns the correct active grid regardless of
/// whether the player is in a room or hallway.
/// </summary>
public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    // Fired whenever the player crosses into any new grid (room or hallway).
    // The argument is the PlacedRoom descriptor — check IsHallway if you need
    // to distinguish. Null is only passed by ClearCurrentRoom().
    public static event System.Action<LevelGenerator.PlacedRoom> OnAnyRoomChanged;

    private LevelGenerator.PlacedRoom currentRoom;
    private RoomGrid                  currentHallwayGrid;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Unified entry point ────────────────────────────────────────────────

    /// <summary>
    /// Set the player's current location. Works for both real rooms and hallway
    /// PlacedRoom descriptors (IsHallway == true).
    ///
    /// Called by HallwayRoomBridge every time the active grid changes.
    /// </summary>
    public void SetCurrentRoom(LevelGenerator.PlacedRoom placed)
    {
        if (placed == null)
        {
            ClearCurrentRoom();
            return;
        }

        if (placed.IsHallway)
        {
            // Hallway descriptor — store only the grid override
            currentHallwayGrid = placed.roomGrid;
            // Keep currentRoom as-is so returning to a room restores it
            OnAnyRoomChanged?.Invoke(placed);
        }
        else
        {
            // Real room — clear hallway override
            currentRoom        = placed;
            currentHallwayGrid = null;
            OnAnyRoomChanged?.Invoke(placed);
        }
    }

    /// <summary>
    /// Legacy explicit hallway setter kept for any code that still calls it.
    /// Prefer SetCurrentRoom(hallway.AsPlacedRoom()).
    /// </summary>
    public void SetCurrentHallway(RoomGrid hallwayGrid)
    {
        currentHallwayGrid = hallwayGrid;
        OnAnyRoomChanged?.Invoke(null);
    }

    /// <summary>
    /// Returns the currently active RoomGrid.
    /// Hallway grid takes priority over room grid when both are set.
    /// </summary>
    public RoomGrid GetCurrentRoomGrid()
    {
        if (currentHallwayGrid != null) return currentHallwayGrid;
        return currentRoom?.roomGrid;
    }

    // ── Existing API ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current PlacedRoom (null if in a hallway with no backing room).
    /// </summary>
    public LevelGenerator.PlacedRoom GetCurrentRoom() => currentRoom;

    public void ClearCurrentRoom()
    {
        currentRoom        = null;
        currentHallwayGrid = null;
        OnAnyRoomChanged?.Invoke(null);
    }

    public TilemapRoomGrid GetCurrentTilemapRoomGrid() =>
        GetCurrentRoomGrid()?.GetTilemapRoomGrid();
}
using System;
using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    private LevelGenerator.PlacedRoom currentRoom;
    private bool inHallway;

    public event Action<LevelGenerator.PlacedRoom> OnRoomChanged;
    public static event Action<LevelGenerator.PlacedRoom> OnAnyRoomChanged;

    /// <summary>
    /// Fired when a room's enemies are fully cleared.
    /// MinimapUI subscribes to this to mark rooms as cleared.
    /// </summary>
    public static event Action<LevelGenerator.PlacedRoom> OnRoomCleared;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Set / Clear ────────────────────────────────────────────────────────

    // public void SetCurrentRoom(LevelGenerator.PlacedRoom room)
    // {
    //     currentRoom = room;
    //     inHallway   = false;

    //     // Fire events so all listeners (minimap, highlighter, etc.) update.
    //     OnRoomChanged?.Invoke(room);
    //     OnAnyRoomChanged?.Invoke(room);

    //     if (room != null)
    //     {
    //         ApplyRoomCamera(room);
    //         Debug.Log($"[RoomManager] Entered: {room.roomInstance?.name ?? "unknown"}");
    //     }
    // }

    // /// <summary>
    // /// Call this after combat ends to tell the minimap the room is cleared.
    // /// Separate from SetCurrentRoom so enemy count is accurate at call time.
    // /// </summary>
    // public void NotifyRoomCleared(LevelGenerator.PlacedRoom room)
    // {
    //     if (room == null) return;
    //     OnRoomCleared?.Invoke(room);
    //     Debug.Log($"[RoomManager] Room cleared: {room.roomInstance?.name ?? "unknown"}");
    // }

    // public void SetInHallway()
    // {
    //     currentRoom = null;
    //     inHallway   = true;
    //     OnRoomChanged?.Invoke(null);
    //     Debug.Log("<color=cyan>[RoomManager] State Switched: In Hallway</color>");
    // }


    public void SetCurrentRoom(LevelGenerator.PlacedRoom room)
    {
        currentRoom = room;
        inHallway = false;

        // Fire events so all listeners (minimap, highlighter, etc.) update.
        OnRoomChanged?.Invoke(room);
        OnAnyRoomChanged?.Invoke(room);

        if (room != null)
        {
            ApplyRoomCamera(room);
            
            Debug.Log($"<color=#B800FF>[RoomManager -> Tracker]</color> Player entered " +
                    $"<color=#E066FF>{room.prefabData.roomType}</color> Room " +
                    $"at layout grid: <color=#E066FF>({room.gridPosition.x}, {room.gridPosition.y})</color> " +
                    $"({room.roomInstance?.name ?? "unknown"})");

            if (CurrentRoomHasEnemies())
                CameraController2D.Instance?.SetCombatState(true);
            else
                CameraController2D.Instance?.SetCombatState(false);

            Debug.Log($"[RoomManager] Entered: {room.roomInstance?.name ?? "unknown"}");
        }
    }

    public void SetInHallway()
    {
        currentRoom = null;
        inHallway = true;
        OnRoomChanged?.Invoke(null);

        // PURPLE LOG: Tracks when the state switches completely out of a room and into a hallway map
        Debug.Log("<color=#B800FF>[RoomManager -> Tracker]</color> Player stepped into a <color=#E066FF>HALLWAY</color>.");
    }

    public void NotifyRoomCleared(LevelGenerator.PlacedRoom room)
    {
        if (room == null) return;
        OnRoomCleared?.Invoke(room);

        // PURPLE LOG: Tracks when a room is cleared, useful for validating Minimap state synchronization
        Debug.Log($"<color=#B800FF>[RoomManager -> Minimap Sync]</color> Room cleared notification sent for " +
                $"<color=#E066FF>{room.roomInstance?.name ?? "unknown"}</color>.");
    }

    public void ClearCurrentRoom()
    {
        currentRoom = null;
        inHallway   = false;
        OnRoomChanged?.Invoke(null);
        OnAnyRoomChanged?.Invoke(null);
        CameraController2D.Instance?.ClearRoomBounds();
    }

    public LevelGenerator.PlacedRoom GetCurrentRoom()     => currentRoom;
    public RoomGrid                  GetCurrentRoomGrid() => currentRoom?.roomGrid;
    public bool                      IsInHallway()        => inHallway;

    public bool CurrentRoomHasEnemies()
    {
        if (currentRoom?.roomGrid == null || EnemyManager.Instance == null) return false;
        return EnemyManager.Instance.GetEnemiesInRoom(currentRoom.roomGrid).Count > 0;
    }

    private static void ApplyRoomCamera(LevelGenerator.PlacedRoom room)
    {
        var cam = CameraController2D.Instance;
        if (cam == null || room?.roomInstance == null) return;

        var bounds = room.roomInstance.GetComponentInChildren<CameraRoomBounds>();
        if (bounds != null)
            cam.SetRoomBounds(bounds.GetBounds());
        else
            cam.ClearRoomBounds();

        cam.SnapToTarget();
    }
}
using System;
using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    private LevelGenerator.PlacedRoom currentRoom;
    private bool inHallway;
    private bool transitionLocked;

    public event Action<LevelGenerator.PlacedRoom> OnRoomChanged;
    public static event Action<LevelGenerator.PlacedRoom> OnAnyRoomChanged;
    public static event Action<LevelGenerator.PlacedRoom> OnRoomCleared;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Transition lock ────────────────────────────────────────────────────

    public void SetTransitionLocked(bool locked)
    {
        transitionLocked = locked;
        Debug.Log($"<color=#B800FF>[RoomManager]</color> Transition lock: {locked}");
    }

    // ── Set / Clear ────────────────────────────────────────────────────────

    public void SetCurrentRoom(LevelGenerator.PlacedRoom room)
    {
        currentRoom      = room;
        inHallway        = false;
        transitionLocked = false; // entering a room always clears the lock

        OnRoomChanged?.Invoke(room);
        OnAnyRoomChanged?.Invoke(room);

        if (room != null)
        {
            ApplyRoomCamera(room);

            Debug.Log($"<color=#B800FF>[RoomManager -> Tracker]</color> Player entered " +
                      $"<color=#E066FF>{room.prefabData.roomType}</color> Room " +
                      $"at layout grid: <color=#E066FF>" +
                      $"({room.gridPosition.x}, {room.gridPosition.y})</color> " +
                      $"({room.roomInstance?.name ?? "unknown"})");

            // Set combat camera state AFTER enemies are spawned — called
            // again from HallwayEntryTrigger once enemy count is known.
            if (CurrentRoomHasEnemies())
                CameraController2D.Instance?.SetCombatState(true);
            else
                CameraController2D.Instance?.SetCombatState(false);
        }
    }

    public void SetInHallway()
    {
        // If a room transition is in progress, ignore this completely.
        // HallwayWalkTrigger fires OnTriggerStay2D every frame and would
        // overwrite SetCurrentRoom if we don't guard here.
        if (transitionLocked)
        {
            Debug.Log("<color=#B800FF>[RoomManager]</color> SetInHallway suppressed " +
                      "(transition in progress).");
            return;
        }

        currentRoom = null;
        inHallway   = true;
        OnRoomChanged?.Invoke(null);

        Debug.Log("<color=#B800FF>[RoomManager -> Tracker]</color> Player stepped into " +
                  "a <color=#E066FF>HALLWAY</color>.");
    }

    public void NotifyRoomCleared(LevelGenerator.PlacedRoom room)
    {
        if (room == null) return;
        OnRoomCleared?.Invoke(room);

        Debug.Log($"<color=#B800FF>[RoomManager -> Minimap Sync]</color> Room cleared: " +
                  $"<color=#E066FF>{room.roomInstance?.name ?? "unknown"}</color>.");
    }

    public void ClearCurrentRoom()
    {
        currentRoom      = null;
        inHallway        = false;
        transitionLocked = false;
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
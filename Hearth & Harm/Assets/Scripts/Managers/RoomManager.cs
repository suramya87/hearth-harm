using System;
using UnityEngine;

/// <summary>
/// Tracks which room the local player is currently in.
/// Fires events so camera, highlighter, enemy lock etc. can react.
///
/// NO LevelGrid dependency — use RoomManager.Instance.GetCurrentRoomGrid()
/// wherever old code called LevelGrid.Instance.
/// </summary>
public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    private LevelGenerator.PlacedRoom currentRoom;
    private bool inHallway;

    /// <summary>Fired when the local player moves to a new room.</summary>
    public event Action<LevelGenerator.PlacedRoom> OnRoomChanged;

    /// <summary>Static version — any subscriber can listen without a RoomManager reference.</summary>
    public static event Action<LevelGenerator.PlacedRoom> OnAnyRoomChanged;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Set / Clear ────────────────────────────────────────────────────────

    public void SetCurrentRoom(LevelGenerator.PlacedRoom room)
    {
        currentRoom = room;
        inHallway   = false;
        OnRoomChanged?.Invoke(room);
        OnAnyRoomChanged?.Invoke(room);

        if (room != null)
        {
            ApplyRoomCamera(room);
            Debug.Log($"[RoomManager] Entered: {room.roomInstance?.name ?? "unknown"}");
        }
    }

    public void SetInHallway()
    {
        currentRoom = null; 
        inHallway = true;   
        OnRoomChanged?.Invoke(null);
        Debug.Log("<color=cyan>[RoomManager] State Switched: In Hallway</color>");
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
        if (currentRoom?.roomGrid == null) return false;
        if (EnemyManager.Instance == null) return false;
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
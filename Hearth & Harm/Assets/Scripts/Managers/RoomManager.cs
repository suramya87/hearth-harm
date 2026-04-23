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
        OnRoomChanged?.Invoke(room);
        OnAnyRoomChanged?.Invoke(room);
        UpdateCamera(room);
        Debug.Log($"[Highlighter] RoomManager={RoomManager.Instance != null} " +
          $"currentRoom={RoomManager.Instance?.GetCurrentRoom() != null} " +
          $"roomGrid={RoomManager.Instance?.GetCurrentRoom()?.roomGrid != null} " +
          $"tilemap={RoomManager.Instance?.GetCurrentRoomGrid()?.GetFloorTilemap() != null}");
    }

    public void ClearCurrentRoom()
    {
        currentRoom = null;
        Debug.Log("[RoomManager] Room cleared.");
    }

    // ── Getters ────────────────────────────────────────────────────────────

    public LevelGenerator.PlacedRoom GetCurrentRoom()     => currentRoom;
    public RoomGrid                  GetCurrentRoomGrid() => currentRoom?.roomGrid;

    // ── Camera ─────────────────────────────────────────────────────────────

    private static void UpdateCamera(LevelGenerator.PlacedRoom room)
    {
        var cam = CameraController2D.Instance;
        if (cam == null || room?.roomInstance == null) return;

        var bounds = room.roomInstance.GetComponentInChildren<CameraRoomBounds>();
        if (bounds != null) cam.SetRoomBounds(bounds.GetBounds());
        cam.SnapToTarget();
    }
}
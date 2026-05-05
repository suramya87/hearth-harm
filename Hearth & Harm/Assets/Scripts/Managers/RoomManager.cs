using System;
using UnityEngine;

public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    private LevelGenerator.PlacedRoom currentRoom;

    public event Action<LevelGenerator.PlacedRoom> OnRoomChanged;
    public static event Action<LevelGenerator.PlacedRoom> OnAnyRoomChanged;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void SetCurrentRoom(LevelGenerator.PlacedRoom room)
    {
        currentRoom = room;
        OnRoomChanged?.Invoke(room);
        OnAnyRoomChanged?.Invoke(room);

        if (room != null)
        {
            UpdateCamera(room);
            Debug.Log($"[RoomManager] Now in: {room.roomInstance?.name ?? "unknown"} " +
                      $"| grid={room.roomGrid != null} " +
                      $"| tilemap={room.roomGrid?.GetFloorTilemap() != null}");
        }
        else
        {
            Debug.Log("[RoomManager] In transit (hallway) — no current room.");
        }
    }

    public void ClearCurrentRoom() => SetCurrentRoom(null);

    public LevelGenerator.PlacedRoom GetCurrentRoom()     => currentRoom;
    public RoomGrid                  GetCurrentRoomGrid() => currentRoom?.roomGrid;

    private static void UpdateCamera(LevelGenerator.PlacedRoom room)
    {
        var cam = CameraController2D.Instance;
        if (cam == null || room?.roomInstance == null) return;

        var bounds = room.roomInstance.GetComponentInChildren<CameraRoomBounds>();
        if (bounds != null) cam.SetRoomBounds(bounds.GetBounds());
        cam.SnapToTarget();
    }
}
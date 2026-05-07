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

    // ── Room entry ─────────────────────────────────────────────────────────

    /// <summary>
    /// Call when the player enters a real room.
    /// Applies room bounds to the camera and snaps to the player.
    /// </summary>
    public void SetCurrentRoom(LevelGenerator.PlacedRoom room)
    {
        currentRoom = room;
        OnRoomChanged?.Invoke(room);
        OnAnyRoomChanged?.Invoke(room);

        if (room != null)
        {
            ApplyRoomCamera(room);
            Debug.Log($"[RoomManager] Entered: {room.roomInstance?.name ?? "unknown"}");
        }
    }

    // ── Hallway entry ──────────────────────────────────────────────────────

    /// <summary>
    /// Call when the player enters a hallway.
    /// Clears room bounds so the camera follows the player freely.
    ///
    /// ENEMY GUARD: if the current room still has live enemies the transition
    /// is silently blocked — the walk trigger's own lock should have prevented
    /// this, but this is a second line of defence so nothing slips through.
    /// </summary>
    public void SetInHallway()
    {
        // ── Guard: refuse if current room has live enemies ─────────────────
        if (CurrentRoomHasEnemies())
        {
            Debug.LogWarning("[RoomManager] SetInHallway blocked — enemies still alive in current room.");
            return;
        }

        // Don't clear currentRoom — we still want GetCurrentRoom() to return
        // the last room for systems that need it (e.g. TilemapHighlighter).
        OnRoomChanged?.Invoke(null);
        OnAnyRoomChanged?.Invoke(null);

        CameraController2D.Instance?.ClearRoomBounds();

        Debug.Log("[RoomManager] In hallway — camera following player freely.");
    }

    // ── Clear ──────────────────────────────────────────────────────────────

    public void ClearCurrentRoom()
    {
        currentRoom = null;
        OnRoomChanged?.Invoke(null);
        OnAnyRoomChanged?.Invoke(null);
        CameraController2D.Instance?.ClearRoomBounds();
    }

    // ── Accessors ──────────────────────────────────────────────────────────

    public LevelGenerator.PlacedRoom GetCurrentRoom()     => currentRoom;
    public RoomGrid                  GetCurrentRoomGrid() => currentRoom?.roomGrid;

    /// <summary>
    /// Returns true if there are any live enemies in the room the player is
    /// currently standing in. Used to block hallway entry mid-combat.
    /// </summary>
    public bool CurrentRoomHasEnemies()
    {
        if (currentRoom?.roomGrid == null) return false;
        if (EnemyManager.Instance == null) return false;
        return EnemyManager.Instance.GetEnemiesInRoom(currentRoom.roomGrid).Count > 0;
    }

    // ── Camera helper ──────────────────────────────────────────────────────

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
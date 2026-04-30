using UnityEngine;

/// <summary>
/// Placed at each end of a hallway (one trigger near room A's door,
/// one near room B's door).
///
/// BEHAVIOUR
///   • The player walks freely through the hallway — no teleportation.
///   • When the player reaches the far-end trigger, RoomManager transitions
///     to the destination room, EnemySpawner spawns that room's enemies,
///     and doors lock until all enemies are dead.
///   • On room clear, doors re-open and fast-travel becomes available.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class HallwayEntryTrigger : MonoBehaviour
{
    // ── Configuration (set by HallwayBuilder) ─────────────────────────────

    public HallwayGrid               Hallway         { get; private set; }
    public LevelGenerator.PlacedRoom DestinationRoom { get; private set; }
    public LevelGenerator.Direction  EntryDirection  { get; private set; }

    private bool triggered;

    // ── Init ───────────────────────────────────────────────────────────────

    public void Initialize(
        HallwayGrid                  hallway,
        LevelGenerator.PlacedRoom    destinationRoom,
        LevelGenerator.Direction     entryDirection)
    {
        Hallway         = hallway;
        DestinationRoom = destinationRoom;
        EntryDirection  = entryDirection;

        GetComponent<Collider2D>().isTrigger = true;
        triggered = false;
    }

    public void ResetTrigger() => triggered = false;

    // ── Trigger ────────────────────────────────────────────────────────────

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (triggered) return;
        if (!other.CompareTag("Player")) return;
        if (DestinationRoom?.roomGrid == null) return;

        triggered = true;
        TransitionToDestination(other.gameObject);
    }

    // ── Transition ─────────────────────────────────────────────────────────

    private void TransitionToDestination(GameObject playerGo)
    {
        var roomManager = RoomManager.Instance;
        if (roomManager == null) return;

        GridPosition spawnPos = GetDestinationSpawnPos();

        if (GameManager.IsMultiplayer)
        {
            foreach (var bridge in FindObjectsByType<NetworkedPlayerBridge>(FindObjectsSortMode.None))
            {
                if (bridge.IsOwner)
                {
                    bridge.TransitionToRoom(DestinationRoom.roomGrid, spawnPos);
                    break;
                }
            }
        }
        else
        {
            var unit = playerGo.GetComponent<Unit>()
                    ?? playerGo.GetComponentInParent<Unit>();
            if (unit == null)
            {
                Debug.LogWarning("[HallwayEntryTrigger] Could not find Unit on player.");
                return;
            }

            roomManager.SetCurrentRoom(DestinationRoom);
            unit.PlaceInRoom(DestinationRoom.roomGrid, spawnPos);
            CameraController2D.Instance?.SnapToTarget();
        }

        LockRoomAndSpawnEnemies(DestinationRoom);

        Debug.Log($"[HallwayEntryTrigger] Player entered {DestinationRoom.roomInstance.name} " +
                  $"from {EntryDirection}.");
    }

    private GridPosition GetDestinationSpawnPos()
    {
        var reader = DestinationRoom.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader != null && reader.HasSpawnPoint(EntryDirection))
            return reader.GetSpawnPosition(EntryDirection, DestinationRoom.roomGrid);

        return new GridPosition(
            DestinationRoom.roomGrid.GetWidth()  / 2,
            DestinationRoom.roomGrid.GetHeight() / 2);
    }

    // ── Lock + spawn ───────────────────────────────────────────────────────

    private static void LockRoomAndSpawnEnemies(LevelGenerator.PlacedRoom room)
    {
        if (room == null || room.connector == null) return;

        // Start room never gets enemies or locked doors
        if (room.prefabData.roomType == LevelGenerator.RoomType.Start) return;

        // If living enemies are already present this room was previously entered —
        // keep doors locked but don't spawn again
        bool alreadyActive = EnemyManager.Instance != null &&
                             EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0;
        if (alreadyActive) return;

        // Lock all connected doors immediately
        room.connector.CloseAllDoors();

        // Spawn enemies via the scene-level EnemySpawner
        var spawner = FindAnyObjectByType<EnemySpawner>();
        if (spawner != null)
            spawner.SpawnForRoom(room);
        else
            Debug.LogWarning("[HallwayEntryTrigger] No EnemySpawner in scene — enemies won't spawn.");

        // Subscribe to re-open doors once every enemy in this room is dead
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared += OnRoomCleared;
    }

    // ── Room cleared ───────────────────────────────────────────────────────

    private static void OnRoomCleared(RoomGrid clearedRoom)
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return;

        foreach (var placed in gen.GetAllRooms())
        {
            if (placed.roomGrid != clearedRoom) continue;

            // Re-open every door that has a connection
            foreach (LevelGenerator.Direction dir in
                System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            {
                if (gen.GetConnectedRoom(placed, dir) != null)
                    placed.connector.SetDoorOpen(dir, true);
            }
            break;
        }

        // One-shot — unsubscribe immediately after firing
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= OnRoomCleared;
    }
}
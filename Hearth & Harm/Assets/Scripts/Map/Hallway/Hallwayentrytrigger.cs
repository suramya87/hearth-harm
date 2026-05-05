using System.Collections;
using UnityEngine;

/// <summary>
/// Placed at each end of a hallway. When the player reaches this trigger
/// they are transitioned into the destination room, enemies spawn, and
/// doors lock until all are dead.
///
/// DRAG-BACK FIX:
///   Sets HallwayWalkTrigger.EntryTransitionInProgress = true before
///   transitioning the player. This tells any overlapping WalkTrigger
///   at the same mouth not to pull the player back into the hallway
///   immediately after they arrive in the room.
///   The flag is cleared after a short hold (2 seconds) which is longer
///   than the WalkTrigger's internal cooldown window.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class HallwayEntryTrigger : MonoBehaviour
{
    public HallwayGrid               Hallway         { get; private set; }
    public LevelGenerator.PlacedRoom DestinationRoom { get; private set; }
    public LevelGenerator.Direction  EntryDirection  { get; private set; }

    private bool cooling;

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
        cooling = false;
    }

    public void ResetTrigger()
    {
        StopAllCoroutines();
        cooling = false;
    }

    // ── Trigger ────────────────────────────────────────────────────────────

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (cooling) return;
        if (!other.CompareTag("Player")) return;
        if (DestinationRoom?.roomGrid == null) return;

        var unit = other.GetComponent<Unit>()
                ?? other.GetComponentInParent<Unit>();
        if (unit == null) return;

        if (GameManager.IsMultiplayer)
        {
            var bridge = unit.GetComponent<NetworkedPlayerBridge>();
            if (bridge == null || !bridge.IsOwner) return;
        }

        // Already in the destination room — nothing to do
        if (IsOnDestinationGrid(unit)) return;

        StartCoroutine(TransitionAfterMove(unit));
    }

    // ── Transition ─────────────────────────────────────────────────────────

    private IEnumerator TransitionAfterMove(Unit unit)
    {
        cooling = true;

        // Wait for any in-progress move to finish
        var move = unit.GetMoveAction();
        if (move != null)
            while (move.IsActive) yield return null;

        yield return new WaitForSeconds(0.05f);

        // Re-check after waiting
        if (IsOnDestinationGrid(unit))
        {
            cooling = false;
            yield break;
        }

        // ── Signal walk triggers to stand down ────────────────────────────
        // This must be set BEFORE PlaceInRoom so that when the physics engine
        // processes the new position and the walk trigger's OnTriggerEnter2D
        // fires (because the collider is still overlapping), it bails out.
        HallwayWalkTrigger.EntryTransitionInProgress = true;

        GridPosition spawnPos = GetDestinationSpawnPos();

        if (GameManager.IsMultiplayer)
        {
            foreach (var bridge in FindObjectsByType<NetworkedPlayerBridge>(FindObjectsSortMode.None))
            {
                if (!bridge.IsOwner) continue;
                bridge.TransitionToRoom(DestinationRoom.roomGrid, spawnPos);
                break;
            }
        }
        else
        {
            RoomManager.Instance?.SetCurrentRoom(DestinationRoom);
            unit.PlaceInRoom(DestinationRoom.roomGrid, spawnPos);
            CameraController2D.Instance?.SnapToTarget();
        }

        LockRoomAndSpawnEnemies(DestinationRoom);

        Debug.Log($"[HallwayEntryTrigger] {unit.name} entered " +
                  $"{DestinationRoom.roomInstance.name} from {EntryDirection}.");

        // Hold the flag long enough for the walk trigger's internal cooldown
        // to expire and for any physics callbacks to settle.
        yield return new WaitForSeconds(2f);
        HallwayWalkTrigger.EntryTransitionInProgress = false;

        // Keep our own cooldown active a bit longer so this trigger can't
        // double-fire if the player lingers at the threshold.
        yield return new WaitForSeconds(0.5f);
        cooling = false;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private bool IsOnDestinationGrid(Unit unit)
    {
        var current = unit.GetCurrentRoomGrid();
        if (current == null) return false;
        if (current == DestinationRoom.roomGrid) return true;
        // Name fallback for multiplayer where references may differ
        return current.gameObject.name == DestinationRoom.roomGrid.gameObject.name;
    }

    private GridPosition GetDestinationSpawnPos()
    {
        var reader = DestinationRoom.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader != null && reader.HasSpawnPoint(EntryDirection))
            return reader.GetSpawnPosition(EntryDirection, DestinationRoom.roomGrid);

        Debug.LogWarning($"[HallwayEntryTrigger] No spawn point for {EntryDirection} " +
                         $"in {DestinationRoom.roomInstance.name}. Using centre.");
        return new GridPosition(
            DestinationRoom.roomGrid.GetWidth()  / 2,
            DestinationRoom.roomGrid.GetHeight() / 2);
    }

    // ── Lock + spawn ───────────────────────────────────────────────────────

    private static void LockRoomAndSpawnEnemies(LevelGenerator.PlacedRoom room)
    {
        if (room?.connector == null) return;
        if (room.prefabData.roomType == LevelGenerator.RoomType.Start) return;

        bool alreadyActive = EnemyManager.Instance != null &&
                             EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0;
        if (alreadyActive) return;

        room.connector.CloseAllDoors();

        var spawner = FindAnyObjectByType<EnemySpawner>();
        if (spawner != null)
            spawner.SpawnForRoom(room);
        else
            Debug.LogWarning("[HallwayEntryTrigger] No EnemySpawner found.");

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
            foreach (LevelGenerator.Direction dir in
                System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            {
                if (gen.GetConnectedRoom(placed, dir) != null)
                    placed.connector.SetDoorOpen(dir, true);
            }
            break;
        }

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= OnRoomCleared;
    }
}
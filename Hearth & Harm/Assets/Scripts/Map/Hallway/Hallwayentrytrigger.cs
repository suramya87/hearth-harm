using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Placed at each end of a hallway. When the player reaches this trigger
/// they are transitioned into the destination room, enemies spawn, and
/// doors + walk triggers lock until all enemies are dead.
///
/// BUGS FIXED IN THIS VERSION
/// ──────────────────────────
/// 1. alreadyActive trap: the old code returned early on a second room entry
///    without re-subscribing HandleRoomCleared, so the player was permanently
///    stuck once enemies were already alive. Now we always subscribe the
///    callback when enemies are present, regardless of whether we spawned them.
///
/// 2. Duplicate subscription guard: subscribing the same delegate twice to
///    an event means it fires twice. We unsubscribe before re-subscribing so
///    HandleRoomCleared fires exactly once per room-clear.
///
/// 3. roomBorderTriggers scope: the list is populated on whichever entry
///    trigger the player walked through. On unlock we iterate the list and
///    call SetLocked(false) on every walk trigger (which also hides its strip).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class HallwayEntryTrigger : MonoBehaviour
{
    public HallwayGrid               Hallway         { get; private set; }
    public LevelGenerator.PlacedRoom DestinationRoom { get; private set; }
    public LevelGenerator.Direction  EntryDirection  { get; private set; }

    // The WalkTrigger at the same mouth as this EntryTrigger.
    // Set by HallwayBuilder after both triggers are created.
    private HallwayWalkTrigger pairedWalkTrigger;

    // All WalkTriggers bordering the destination room, locked during combat.
    private List<HallwayWalkTrigger> roomBorderTriggers;

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

    public void SetPairedWalkTrigger(HallwayWalkTrigger wt) => pairedWalkTrigger = wt;

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

        if (IsOnDestinationGrid(unit)) return;

        StartCoroutine(TransitionAfterMove(unit));
    }

    // ── Transition ─────────────────────────────────────────────────────────

    private IEnumerator TransitionAfterMove(Unit unit)
    {
        cooling = true;

        var move = unit.GetMoveAction();
        if (move != null)
            while (move.IsActive) yield return null;

        yield return new WaitForSeconds(0.05f);

        if (IsOnDestinationGrid(unit)) { cooling = false; yield break; }

        // Block walk triggers from rubber-banding the player back into the hallway
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

        bool hadEnemies = LockRoomAndSpawnEnemies(DestinationRoom, this);

        if (hadEnemies)
            LockAllRoomExits(DestinationRoom);

        Debug.Log($"[HallwayEntryTrigger] {unit.name} entered " +
                  $"{DestinationRoom.roomInstance.name} from {EntryDirection}.");

        yield return new WaitForSeconds(2f);
        HallwayWalkTrigger.EntryTransitionInProgress = false;

        yield return new WaitForSeconds(0.5f);
        cooling = false;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private bool IsOnDestinationGrid(Unit unit)
    {
        var current = unit.GetCurrentRoomGrid();
        if (current == null) return false;
        if (current == DestinationRoom.roomGrid) return true;
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

    /// <summary>
    /// Closes room connector doors, spawns enemies if not already present,
    /// and subscribes the room-cleared callback.
    /// Returns true if enemies are present so the caller knows to lock exits.
    ///
    /// KEY FIX: always subscribes HandleRoomCleared when enemies are present.
    /// The previous version returned early on alreadyActive without subscribing,
    /// so the player could never be released on a second entry to the same room.
    /// We unsubscribe before subscribing to avoid duplicate firings.
    /// </summary>
    private static bool LockRoomAndSpawnEnemies(
        LevelGenerator.PlacedRoom room,
        HallwayEntryTrigger       triggerForCallback)
    {
        if (room?.connector == null) return false;
        if (room.prefabData.roomType == LevelGenerator.RoomType.Start) return false;
        if (EnemyManager.Instance == null) return false;

        bool alreadyActive = EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0;

        if (!alreadyActive)
        {
            // First visit to this room — close doors and spawn
            room.connector.CloseAllDoors();

            var spawner = FindAnyObjectByType<EnemySpawner>();
            if (spawner == null)
            {
                Debug.LogWarning("[HallwayEntryTrigger] No EnemySpawner found.");
                return false;
            }

            spawner.SpawnForRoom(room);
        }

        // Check again — did we actually end up with live enemies?
        bool hasEnemies = EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0;

        if (hasEnemies)
        {
            // Unsubscribe first to guarantee exactly one subscription at a time
            EnemyManager.Instance.OnRoomCleared -= triggerForCallback.HandleRoomCleared;
            EnemyManager.Instance.OnRoomCleared += triggerForCallback.HandleRoomCleared;
        }

        return hasEnemies;
    }

    // ── Hallway-exit locking ───────────────────────────────────────────────

    /// <summary>
    /// Locks every HallwayWalkTrigger that borders the destination room and
    /// attaches the matching door-strip object so it becomes visible.
    /// Only called when enemies are actually present.
    /// </summary>
    private void LockAllRoomExits(LevelGenerator.PlacedRoom room)
    {
        roomBorderTriggers = new List<HallwayWalkTrigger>();

        foreach (var et in FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None))
        {
            if (et.DestinationRoom?.roomGrid == null) continue;

            bool sameRoom = et.DestinationRoom.roomGrid == room.roomGrid ||
                            et.DestinationRoom.roomGrid.gameObject.name ==
                            room.roomGrid.gameObject.name;
            if (!sameRoom) continue;

            if (et.pairedWalkTrigger == null) continue;

            // Wire up the door strip the first time we see this walk trigger.
            // EntryDirection is which wall of the room the hallway connects to,
            // so the strip on that same wall is the correct visual barrier.
            if (et.pairedWalkTrigger.DoorStripObject == null && room.connector != null)
            {
                GameObject strip = et.EntryDirection switch
                {
                    LevelGenerator.Direction.North => room.connector.northDoorStrip,
                    LevelGenerator.Direction.South => room.connector.southDoorStrip,
                    LevelGenerator.Direction.East  => room.connector.eastDoorStrip,
                    LevelGenerator.Direction.West  => room.connector.westDoorStrip,
                    _                              => null
                };
                if (strip != null)
                    et.pairedWalkTrigger.DoorStripObject = strip;
            }

            et.pairedWalkTrigger.SetLocked(true);   // shows strip + blocks player
            roomBorderTriggers.Add(et.pairedWalkTrigger);
        }

        Debug.Log($"[HallwayEntryTrigger] Locked {roomBorderTriggers.Count} " +
                  $"exit(s) for {room.roomInstance.name}.");
    }

    // ── Room cleared ───────────────────────────────────────────────────────

    /// <summary>
    /// Fires when EnemyManager reports a room is cleared.
    /// Unlocks all walk triggers (hiding their door strips) and re-opens
    /// the room's connector door strips.
    /// </summary>
    private void HandleRoomCleared(RoomGrid clearedRoom)
    {
        if (clearedRoom == null) return;

        bool isOurRoom = clearedRoom == DestinationRoom?.roomGrid ||
                         clearedRoom.gameObject.name == DestinationRoom?.roomGrid?.gameObject.name;
        if (!isOurRoom) return;

        // Unsubscribe first — fire exactly once
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= HandleRoomCleared;

        // Re-open the room's own connector door strips
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen != null)
        {
            foreach (var placed in gen.GetAllRooms())
            {
                bool match = placed.roomGrid == clearedRoom ||
                             placed.roomGrid?.gameObject.name == clearedRoom.gameObject.name;
                if (!match) continue;

                foreach (LevelGenerator.Direction dir in
                    System.Enum.GetValues(typeof(LevelGenerator.Direction)))
                {
                    if (gen.GetConnectedRoom(placed, dir) != null)
                        placed.connector.SetDoorOpen(dir, true);
                }
                break;
            }
        }

        // Unlock every hallway exit — SetLocked(false) also hides the door strip
        if (roomBorderTriggers != null)
        {
            foreach (var wt in roomBorderTriggers)
                wt?.SetLocked(false);
            roomBorderTriggers.Clear();
        }

        Debug.Log($"[HallwayEntryTrigger] {DestinationRoom?.roomInstance?.name} cleared — " +
                  $"all exits unlocked.");
    }
}
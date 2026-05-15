using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class HallwayEntryTrigger : MonoBehaviour
{
    public HallwayGrid               Hallway         { get; private set; }
    public LevelGenerator.PlacedRoom DestinationRoom { get; private set; }
    public LevelGenerator.Direction  EntryDirection  { get; private set; }

    private HallwayWalkTrigger           pairedWalkTrigger;
    private List<HallwayWalkTrigger>     roomBorderTriggers;
    private bool                         cooling;

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

        // Already in the destination — nothing to do.
        if (IsOnDestinationGrid(unit)) return;

        // FIX: removed the hard "must be on hallway grid" guard.
        //
        // Old behaviour: if the walk trigger's async HandOffAfterMove hadn't
        // finished yet the unit was still registered on the source room grid,
        // IsOnHallwayGrid() returned false and we silently returned — the
        // player walked through the mouth and nothing happened.
        //
        // New behaviour: we allow the transition as long as the player is NOT
        // already in the destination room. If they happen to still be on the
        // source room grid (walk trigger mid-handoff) that's fine — we just
        // move them into the destination anyway, which is the correct outcome.
        //
        // We do still reject players that are on a *different* hallway to
        // prevent cross-hallway ghost transitions.
        if (!IsOnHallwayOrAdjacentGrid(unit)) return;

        StartCoroutine(TransitionAfterMove(unit));
    }

    private IEnumerator TransitionAfterMove(Unit unit)
    {
        cooling = true;

        // Wait for an in-progress move to finish so we don't snap the player
        // mid-animation. Cap at 2s to avoid a permanent lock if the path stalls.
        var move = unit.GetMoveAction();
        if (move != null)
        {
            float timeout = 2f;
            float elapsed = 0f;
            while (move.IsActive)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= timeout)
                {
                    Debug.LogWarning($"[HallwayEntryTrigger] Move timeout — forcing transition.");
                    break;
                }
                yield return null;
            }
        }

        yield return new WaitForSeconds(0.05f);

        // Re-check: another trigger may have handled this while we waited.
        if (IsOnDestinationGrid(unit)) { cooling = false; yield break; }

        GridPosition spawnPos = GetDestinationSpawnPos();

        // Briefly disable all walk triggers so the position change doesn't
        // immediately rubber-band the player back into the hallway.
        HallwayWalkTrigger[] allWalkTriggers =
            FindObjectsByType<HallwayWalkTrigger>(FindObjectsSortMode.None);
        foreach (var wt in allWalkTriggers)
            wt.DisableTemporarily(1.5f);

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

        Debug.Log($"[HallwayEntryTrigger] {unit.name} entered {DestinationRoom.roomInstance.name}.");

        yield return new WaitForSeconds(0.5f);
        cooling = false;
    }

    // ── Grid identity helpers ──────────────────────────────────────────────

    private bool IsOnDestinationGrid(Unit unit)
    {
        var current = unit.GetCurrentRoomGrid();
        if (current == null) return false;
        if (current == DestinationRoom.roomGrid) return true;
        return current.gameObject.name == DestinationRoom.roomGrid.gameObject.name;
    }

    // FIX: replaces the old strict IsOnHallwayGrid check.
    // Returns true when the unit is on:
    //   • this hallway's grid (the normal case), OR
    //   • the room on the OTHER side of this hallway (walk trigger mid-handoff), OR
    //   • null (unit hasn't been placed yet — let it through).
    // Returns false only when the unit is already on a completely unrelated grid
    // (prevents ghost transitions through walls or into wrong hallways).
    private bool IsOnHallwayOrAdjacentGrid(Unit unit)
    {
        var current = unit.GetCurrentRoomGrid();

        // No grid yet — allow through (handles spawn edge cases).
        if (current == null) return true;

        // On this hallway — normal path.
        if (Hallway?.RoomGrid != null)
        {
            if (current == Hallway.RoomGrid) return true;
            if (current.gameObject.name == Hallway.RoomGrid.gameObject.name) return true;
        }

        // Still on RoomA or RoomB of this hallway (walk trigger mid-handoff).
        // We identify those rooms via the HallwayGrid's stored references.
        if (Hallway != null)
        {
            var roomA = Hallway.RoomA?.roomGrid;
            var roomB = Hallway.RoomB?.roomGrid;
            if (roomA != null && (current == roomA || current.gameObject.name == roomA.gameObject.name))
                return true;
            if (roomB != null && (current == roomB || current.gameObject.name == roomB.gameObject.name))
                return true;
        }

        return false;
    }

    // ── Spawn position ─────────────────────────────────────────────────────

    private GridPosition GetDestinationSpawnPos()
    {
        var reader = DestinationRoom.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader != null && reader.HasSpawnPoint(EntryDirection))
            return reader.GetSpawnPosition(EntryDirection, DestinationRoom.roomGrid);

        return new GridPosition(
            DestinationRoom.roomGrid.GetWidth()  / 2,
            DestinationRoom.roomGrid.GetHeight() / 2);
    }

    // ── Room locking / enemy spawning ──────────────────────────────────────

    private static bool LockRoomAndSpawnEnemies(
        LevelGenerator.PlacedRoom room,
        HallwayEntryTrigger triggerForCallback)
    {
        if (room?.connector == null) return false;

        if (room.prefabData.roomType == LevelGenerator.RoomType.Start) return false;

        if (EnemyManager.Instance == null) return false;

        bool alreadyHasEnemies =
            EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0;

        if (!alreadyHasEnemies)
        {
            var spawner = FindAnyObjectByType<EnemySpawner>();
            spawner?.SpawnForRoom(room);
        }

        int  enemyCount = EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count;
        bool hasEnemies = enemyCount > 0;

        if (hasEnemies)
        {
            room.connector.CloseAllDoors();

            EnemyManager.Instance.OnRoomCleared -= triggerForCallback.HandleRoomCleared;
            EnemyManager.Instance.OnRoomCleared += triggerForCallback.HandleRoomCleared;

            Debug.Log($"[RoomLock] {room.roomGrid.name} locked with {enemyCount} enemies.");
        }
        else
        {
            foreach (LevelGenerator.Direction dir in
                System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            {
                bool shouldBeOpen = room.roomGrid.GetDoorState(dir);
                room.connector.SetDoorOpen(dir, shouldBeOpen);
            }

            Debug.Log($"[RoomLock] {room.roomGrid.name} empty — restoring door states.");
        }

        return hasEnemies;
    }

    private void LockAllRoomExits(LevelGenerator.PlacedRoom room)
    {
        if (!RoomManager.Instance.CurrentRoomHasEnemies()) return;

        roomBorderTriggers = new List<HallwayWalkTrigger>();

        foreach (var et in FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None))
        {
            if (et.DestinationRoom?.roomGrid == null) continue;

            bool isThisRoom =
                et.DestinationRoom.roomGrid == room.roomGrid ||
                et.DestinationRoom.roomGrid.gameObject.name == room.roomGrid.gameObject.name;

            if (!isThisRoom) continue;
            if (et.pairedWalkTrigger == null) continue;

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
                et.pairedWalkTrigger.DoorStripObject = strip;
            }

            et.pairedWalkTrigger.SetLocked(true);
            roomBorderTriggers.Add(et.pairedWalkTrigger);
        }
    }

    private void HandleRoomCleared(RoomGrid clearedRoom)
    {
        if (clearedRoom == null) return;

        bool isOurRoom =
            clearedRoom == DestinationRoom?.roomGrid ||
            clearedRoom.gameObject.name == DestinationRoom?.roomGrid?.gameObject.name;
        if (!isOurRoom) return;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= HandleRoomCleared;

        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen != null)
        {
            foreach (var placed in gen.GetAllRooms())
            {
                bool match =
                    placed.roomGrid == clearedRoom ||
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

        if (roomBorderTriggers != null)
        {
            foreach (var wt in roomBorderTriggers)
                wt?.SetLocked(false);
            roomBorderTriggers.Clear();
        }
    }
}
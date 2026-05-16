using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class HallwayEntryTrigger : MonoBehaviour
{
    public HallwayGrid               Hallway         { get; private set; }
    public LevelGenerator.PlacedRoom DestinationRoom { get; private set; }
    public LevelGenerator.Direction  EntryDirection  { get; private set; }

    private HallwayWalkTrigger       pairedWalkTrigger;
    private List<HallwayWalkTrigger> roomBorderTriggers;
    private bool                     cooling;

    public void Initialize(
        HallwayGrid               hallway,
        LevelGenerator.PlacedRoom destinationRoom,
        LevelGenerator.Direction  entryDirection)
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
        RoomManager.Instance?.SetTransitionLocked(false);
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

        if (IsOnDestinationGrid(unit)) return;
        if (!IsOnHallwayOrAdjacentGrid(unit)) return;

        RoomManager.Instance?.SetTransitionLocked(true);
        StartCoroutine(TransitionAfterMove(unit));
    }

    private IEnumerator TransitionAfterMove(Unit unit)
    {
        cooling = true;

        var move = unit.GetMoveAction();
        if (move != null)
        {
            float timeout = 2f;
            float elapsed = 0f;
            while (move.IsActive)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= timeout) break;
                yield return null;
            }
        }

        yield return null;

        if (IsOnDestinationGrid(unit))
        {
            RoomManager.Instance?.SetTransitionLocked(false);
            cooling = false;
            yield break;
        }

        GridPosition spawnPos = GetDestinationSpawnPos();

        if (GameManager.IsMultiplayer)
        {
            foreach (var bridge in
                FindObjectsByType<NetworkedPlayerBridge>(FindObjectsSortMode.None))
            {
                if (!bridge.IsOwner) continue;
                bridge.TransitionToRoom(DestinationRoom.roomGrid, spawnPos);
                break;
            }
            RoomManager.Instance?.SetTransitionLocked(false);
        }
        else
        {
            unit.IsSyncingFromNetwork = true;
            unit.PlaceInRoom(DestinationRoom.roomGrid, spawnPos);
            unit.IsSyncingFromNetwork = false;

            // SetCurrentRoom clears transitionLocked and fires OnAnyRoomChanged
            // (which updates minimap, highlighter, camera).
            RoomManager.Instance?.SetCurrentRoom(DestinationRoom);
            CameraController2D.Instance?.SnapToTarget();
        }

        foreach (var wt in FindObjectsByType<HallwayWalkTrigger>(FindObjectsSortMode.None))
            wt.DisableTemporarily(1.5f);

        SpawnEnemiesForRoom(DestinationRoom);

        bool hadEnemies = RoomHasEnemies(DestinationRoom);
        if (hadEnemies)
        {
            LockRoomDoors(DestinationRoom);
            LockAllRoomExits(DestinationRoom);

            EnemyManager.Instance.OnRoomCleared -= HandleRoomCleared;
            EnemyManager.Instance.OnRoomCleared += HandleRoomCleared;

            CameraController2D.Instance?.SetCombatState(true);

            Debug.Log($"[RoomLock] {DestinationRoom.roomGrid.name} locked — " +
                      $"{EnemyManager.Instance.GetEnemiesInRoom(DestinationRoom.roomGrid).Count} enemies.");
        }
        else
        {
            RestoreDoorStates(DestinationRoom);
            CameraController2D.Instance?.SetCombatState(false);
        }

        move?.InvalidateCache();

        Debug.Log($"[HallwayEntryTrigger] {unit.name} → {DestinationRoom.roomInstance.name}");

        yield return new WaitForSeconds(0.5f);
        cooling = false;
    }

    // ── Grid identity ──────────────────────────────────────────────────────

    private bool IsOnDestinationGrid(Unit unit)
    {
        var current = unit.GetCurrentRoomGrid();
        if (current == null) return false;
        if (current == DestinationRoom.roomGrid) return true;
        return current.gameObject.name == DestinationRoom.roomGrid.gameObject.name;
    }

    private bool IsOnHallwayOrAdjacentGrid(Unit unit)
    {
        var current = unit.GetCurrentRoomGrid();
        if (current == null) return true;

        if (Hallway?.RoomGrid != null)
        {
            if (current == Hallway.RoomGrid) return true;
            if (current.gameObject.name == Hallway.RoomGrid.gameObject.name) return true;
        }

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

    // ── Enemy helpers ──────────────────────────────────────────────────────

    private static void SpawnEnemiesForRoom(LevelGenerator.PlacedRoom room)
    {
        if (room == null) return;
        if (room.prefabData.roomType == LevelGenerator.RoomType.Start) return;
        if (room.roomGrid.HasBeenCleared) return;
        if (EnemyManager.Instance == null) return;
        if (EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0) return;

        var spawner = FindAnyObjectByType<EnemySpawner>();
        spawner?.SpawnForRoom(room);
    }

    private static bool RoomHasEnemies(LevelGenerator.PlacedRoom room)
    {
        if (room?.roomGrid == null || EnemyManager.Instance == null) return false;
        return EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0;
    }

    // ── Door helpers ───────────────────────────────────────────────────────

    private static void LockRoomDoors(LevelGenerator.PlacedRoom room)
    {
        room.connector?.CloseAllDoors();
    }

    private static void RestoreDoorStates(LevelGenerator.PlacedRoom room)
    {
        if (room?.connector == null || room.roomGrid == null) return;
        foreach (LevelGenerator.Direction dir in
            System.Enum.GetValues(typeof(LevelGenerator.Direction)))
        {
            bool shouldBeOpen = room.roomGrid.GetDoorState(dir);
            room.connector.SetDoorOpen(dir, shouldBeOpen);
        }
    }

    // ── Walk trigger locking ───────────────────────────────────────────────

    private void LockAllRoomExits(LevelGenerator.PlacedRoom room)
    {
        roomBorderTriggers = new List<HallwayWalkTrigger>();

        foreach (var et in FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None))
        {
            if (et.pairedWalkTrigger == null) continue;

            bool destinationIsOurRoom =
                et.DestinationRoom?.roomGrid != null &&
                (et.DestinationRoom.roomGrid == room.roomGrid ||
                 et.DestinationRoom.roomGrid.gameObject.name == room.roomGrid.gameObject.name);

            bool hallwayTouchesOurRoom = false;
            if (et.Hallway != null)
            {
                var roomA = et.Hallway.RoomA?.roomGrid;
                var roomB = et.Hallway.RoomB?.roomGrid;
                bool aIsOurs = roomA != null && (roomA == room.roomGrid ||
                    roomA.gameObject.name == room.roomGrid.gameObject.name);
                bool bIsOurs = roomB != null && (roomB == room.roomGrid ||
                    roomB.gameObject.name == room.roomGrid.gameObject.name);
                hallwayTouchesOurRoom = aIsOurs || bIsOurs;
            }

            if (!destinationIsOurRoom && !hallwayTouchesOurRoom) continue;

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

        Debug.Log($"[RoomLock] Locked {roomBorderTriggers.Count} walk triggers " +
                  $"around {room.roomGrid.name}.");
    }

    // ── Room cleared ───────────────────────────────────────────────────────

    private void HandleRoomCleared(RoomGrid clearedRoom)
    {
        if (clearedRoom == null) return;

        bool isOurRoom =
            clearedRoom == DestinationRoom?.roomGrid ||
            clearedRoom.gameObject.name == DestinationRoom?.roomGrid?.gameObject.name;
        if (!isOurRoom) return;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= HandleRoomCleared;

        // ── 1. Record door states as open and physically open them ─────────
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
                    {
                        // Record the open state so future visits restore correctly.
                        placed.roomGrid.SetDoorState(dir, true);
                        // Physically open the door strip and unregister its wall cells.
                        placed.connector.SetDoorOpen(dir, true);
                    }
                }
                break;
            }
        }

        // ── 2. Unlock walk triggers so player can walk into hallways ───────
        if (roomBorderTriggers != null)
        {
            foreach (var wt in roomBorderTriggers)
                wt?.SetLocked(false);
            roomBorderTriggers.Clear();
        }

        // ── 3. Camera back to normal ───────────────────────────────────────
        CameraController2D.Instance?.SetCombatState(false);

        // ── 4. Invalidate MoveAction cache so the BFS re-runs ─────────────
        // The door strips just deactivated, which calls DoorStripBlocker.OnDisable
        // which calls UnregisterWallCell — but the player's cached reachable
        // positions were built when those walls existed.  Force a rebuild.
        var unit = FindLocalUnit();
        unit?.GetMoveAction()?.InvalidateCache();

        // ── 5. Mark room cleared on RoomGrid so re-entry doesn't respawn ──
        clearedRoom.MarkCleared();

        // ── 6. Tell RoomManager → MinimapUI the room is cleared ───────────
        // MinimapUI subscribes to RoomManager.OnRoomCleared and adds the
        // room to its cleared set, updating the dot tint.
        RoomManager.Instance?.NotifyRoomCleared(DestinationRoom);

        Debug.Log($"[HallwayEntryTrigger] Room cleared — " +
                  $"doors opened, cache invalidated, minimap notified.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Unit FindLocalUnit()
    {
        if (!GameManager.IsMultiplayer) return FindAnyObjectByType<Unit>();
        foreach (var u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var net = u.GetComponent<Unity.Netcode.NetworkObject>();
            if (net != null && net.IsOwner) return u;
        }
        return null;
    }
}
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
        transitionCompleted = false;  
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

        if (transitionCompleted) return;

        if (!IsOnHallwayOrAdjacentGrid(unit)) return;

        RoomManager.Instance?.SetTransitionLocked(true);
        StartCoroutine(TransitionAfterMove(unit));
    }

    private bool transitionCompleted = false;

    private IEnumerator TransitionAfterMove(Unit unit)
    {
        cooling = true;
        transitionCompleted = false;

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

        if (transitionCompleted)
        {
            RoomManager.Instance?.SetTransitionLocked(false);
            cooling = false;
            yield break;
        }

        transitionCompleted = true;

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

            RoomManager.Instance?.SetCurrentRoom(DestinationRoom);
            CameraController2D.Instance?.SnapToTarget();

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
        }

        foreach (var wt in FindObjectsByType<HallwayWalkTrigger>(FindObjectsSortMode.None))
            wt.DisableTemporarily(1.5f);

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
        LevelGenerator.PlacedRoom placedRoom = null;

        if (gen != null)
        {
            foreach (var placed in gen.GetAllRooms())
            {
                bool match =
                    placed.roomGrid == clearedRoom ||
                    placed.roomGrid?.gameObject.name == clearedRoom.gameObject.name;
                if (!match) continue;
                placedRoom = placed;

                var connectedDirs = new System.Collections.Generic.List<LevelGenerator.Direction>();
                foreach (LevelGenerator.Direction dir in
                    System.Enum.GetValues(typeof(LevelGenerator.Direction)))
                {
                    if (gen.GetConnectedRoom(placed, dir) != null)
                    {
                        connectedDirs.Add(dir);
                        placed.roomGrid.SetDoorState(dir, true);
                    }
                }

                // Only open strips that have hallways — dead-end walls stay
                placed.connector?.OpenConnectedDoors(connectedDirs);
                break;
            }
        }

        // Unlock walk triggers
        if (roomBorderTriggers != null)
        {
            foreach (var wt in roomBorderTriggers)
                wt?.SetLocked(false);
            roomBorderTriggers.Clear();
        }

        // Reset transitionCompleted on ALL HallwayEntryTriggers whose hallway
        // touches this room — this is what actually lets the player walk out.
        // Without this the entry triggers stay permanently dead after first use.
        foreach (var et in FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None))
        {
            if (et == null) continue;

            bool hallwayTouchesRoom = false;
            if (et.Hallway != null)
            {
                var roomA = et.Hallway.RoomA?.roomGrid;
                var roomB = et.Hallway.RoomB?.roomGrid;
                bool aMatch = roomA != null && (roomA == clearedRoom ||
                    roomA.gameObject.name == clearedRoom.gameObject.name);
                bool bMatch = roomB != null && (roomB == clearedRoom ||
                    roomB.gameObject.name == clearedRoom.gameObject.name);
                hallwayTouchesRoom = aMatch || bMatch;
            }

            bool destIsRoom = et.DestinationRoom?.roomGrid == clearedRoom ||
                et.DestinationRoom?.roomGrid?.gameObject.name == clearedRoom.gameObject.name;

            if (hallwayTouchesRoom || destIsRoom)
            {
                et.ResetTrigger();
            }
        }

        CameraController2D.Instance?.SetCombatState(false);

        StartCoroutine(InvalidateCacheAfterDoorsOpen());

        clearedRoom.MarkCleared();

        RoomManager.Instance?.NotifyRoomCleared(DestinationRoom);

        Debug.Log($"[HallwayEntryTrigger] Room cleared — doors opened on connected sides only, " +
                  $"entry triggers reset, cache invalidated.");
    }

    private IEnumerator InvalidateCacheAfterDoorsOpen()
    {
        yield return null;
        yield return null;

        var unit = FindLocalUnit();
        unit?.GetMoveAction()?.InvalidateCache();

        Debug.Log("[HallwayEntryTrigger] Move cache invalidated after doors opened.");
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

    // ── Door helpers ───────────────────────────────────────────────────────────

private static void LockRoomDoors(LevelGenerator.PlacedRoom room)
{
    if (room?.connector == null) return;

    var gen = FindAnyObjectByType<LevelGenerator>();
    if (gen == null) { room.connector.CloseAllDoors(); return; }

    var connectedDirs = new System.Collections.Generic.List<LevelGenerator.Direction>();
    foreach (LevelGenerator.Direction dir in
        System.Enum.GetValues(typeof(LevelGenerator.Direction)))
    {
        if (gen.GetConnectedRoom(room, dir) != null)
            connectedDirs.Add(dir);
    }

    room.connector.CloseConnectedDoors(connectedDirs);
}

private static void RestoreDoorStates(LevelGenerator.PlacedRoom room)
{
    if (room?.connector == null || room.roomGrid == null) return;

    var gen = FindAnyObjectByType<LevelGenerator>();

    foreach (LevelGenerator.Direction dir in
        System.Enum.GetValues(typeof(LevelGenerator.Direction)))
    {
        // Skip directions with no hallway — those wall strips must never change
        if (gen != null && gen.GetConnectedRoom(room, dir) == null) continue;

        bool shouldBeOpen = room.roomGrid.GetDoorState(dir);
        room.connector.SetDoorOpen(dir, shouldBeOpen);
    }
}

private void LockAllRoomExits(LevelGenerator.PlacedRoom room)
{
    roomBorderTriggers = new List<HallwayWalkTrigger>();

    var gen = FindAnyObjectByType<LevelGenerator>();

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

        if (gen != null && room.connector != null)
        {
            bool dirHasConnection = gen.GetConnectedRoom(room, et.EntryDirection) != null;
            if (!dirHasConnection) continue;

            if (et.pairedWalkTrigger.DoorStripObject == null)
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
        }

        et.pairedWalkTrigger.SetLocked(true);
        roomBorderTriggers.Add(et.pairedWalkTrigger);
    }

    Debug.Log($"[RoomLock] Locked {roomBorderTriggers.Count} walk triggers " +
              $"around {room.roomGrid.name}.");
}

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
}
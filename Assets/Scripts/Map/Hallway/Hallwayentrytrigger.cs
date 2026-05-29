using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class HallwayEntryTrigger : MonoBehaviour
{
    public HallwayGrid               Hallway         { get; private set; }
    public LevelGenerator.PlacedRoom DestinationRoom { get; private set; }
    public LevelGenerator.Direction  EntryDirection  { get; private set; }

    public HallwayWalkTrigger pairedWalkTrigger { get; private set; }
    private List<HallwayWalkTrigger> roomBorderTriggers;

    private readonly HashSet<Unit> coolingUnits   = new();
    private readonly HashSet<Unit> completedUnits = new();

    private bool exitLocked = false;

    public void Initialize(
        HallwayGrid               hallway,
        LevelGenerator.PlacedRoom destinationRoom,
        LevelGenerator.Direction  entryDirection)
    {
        Hallway         = hallway;
        DestinationRoom = destinationRoom;
        EntryDirection  = entryDirection;
        GetComponent<Collider2D>().isTrigger = true;
    }

    public void SetPairedWalkTrigger(HallwayWalkTrigger wt) => pairedWalkTrigger = wt;

    public void ResetTrigger()
    {
        coolingUnits.Clear();
        completedUnits.Clear();
        exitLocked = false;
        RoomManager.Instance?.SetTransitionLocked(false);
    }

    public void SetExitLocked(bool locked)
    {
        exitLocked = locked;
        if (pairedWalkTrigger != null)
            pairedWalkTrigger.SetLocked(locked);
    }

    // ── Trigger ────────────────────────────────────────────────────────────

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        if (DestinationRoom?.roomGrid == null) return;

        var unit = other.GetComponent<Unit>() ?? other.GetComponentInParent<Unit>();
        if (unit == null) return;
        if (!IsLocalPlayerUnit(unit)) return;

        if (exitLocked && IsUnitLeavingLockedRoom(unit))
        {
            Debug.Log("[HallwayEntryTrigger] Exit blocked — enemies in current room.");
            return;
        }

        if (coolingUnits.Contains(unit)) return;
        if (!IsOnHallwayOrAdjacentGrid(unit)) return;
        if (!IsMovingTowardDestination(unit)) return;

        StartCoroutine(TransitionAfterMove(unit));
    }

    // ── Ownership ──────────────────────────────────────────────────────────

    private static bool IsLocalPlayerUnit(Unit unit)
    {
        if (!GameManager.IsMultiplayer) return true;
        var netObj = unit.GetComponent<Unity.Netcode.NetworkObject>();
        return netObj != null && netObj.IsOwner;
    }

    // ── Guards ─────────────────────────────────────────────────────────────

    private bool IsMovingTowardDestination(Unit unit)
    {
        var current = unit.GetCurrentRoomGrid();
        if (current == null) return true;
        if (current == DestinationRoom.roomGrid) return false;
        if (current.gameObject.name == DestinationRoom.roomGrid.gameObject.name) return false;
        return true;
    }

    private bool IsUnitLeavingLockedRoom(Unit unit)
    {
        var currentRoom = unit.GetCurrentRoomGrid();
        if (currentRoom == null) return false;
        if (EnemyManager.Instance == null) return false;
        return EnemyManager.Instance.GetEnemiesInRoom(currentRoom).Count > 0;
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
            if (roomA != null && (current == roomA ||
                current.gameObject.name == roomA.gameObject.name)) return true;
            if (roomB != null && (current == roomB ||
                current.gameObject.name == roomB.gameObject.name)) return true;
        }

        return false;
    }

    // ── Transition ─────────────────────────────────────────────────────────

    private IEnumerator TransitionAfterMove(Unit unit)
    {
        coolingUnits.Add(unit);

        var move = unit.GetMoveAction();
        if (move != null)
        {
            float timeout = 2f, elapsed = 0f;
            while (move.IsActive)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= timeout) break;
                yield return null;
            }
        }

        yield return null;

        if (completedUnits.Contains(unit))
        {
            coolingUnits.Remove(unit);
            yield break;
        }
        completedUnits.Add(unit);

        Vector3      cellCentre = GetUnitCellCentreWorld(unit);
        GridPosition spawnPos   = GetDestinationGridPositionFromWorld(cellCentre);

        if (GameManager.IsMultiplayer)
        {
            var bridge = unit.GetComponent<NetworkedPlayerBridge>();
            if (bridge != null && bridge.IsOwner)
                bridge.TransitionToRoom(DestinationRoom.roomGrid, spawnPos);
        }
        else
        {
            unit.PlaceInRoom(DestinationRoom.roomGrid, spawnPos);

            RoomManager.Instance?.SetCurrentRoom(DestinationRoom);
            CameraController2D.Instance?.SnapToTarget();

            SpawnEnemiesForRoom(DestinationRoom);

            bool hadEnemies = RoomHasEnemies(DestinationRoom);
            if (hadEnemies)
            {
                LockRoomExitsForEnemies(DestinationRoom);
                EnemyManager.Instance.OnRoomCleared -= HandleRoomCleared;
                EnemyManager.Instance.OnRoomCleared += HandleRoomCleared;
                CameraController2D.Instance?.SetCombatState(true);
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
        coolingUnits.Remove(unit);
        completedUnits.Remove(unit);
    }

    // ── Spawn position ─────────────────────────────────────────────────────

    private static Vector3 GetUnitCellCentreWorld(Unit unit)
    {
        var grid = unit.GetCurrentRoomGrid();
        if (grid != null) return grid.GetWorldPosition(unit.GetGridPosition());
        Vector2 vo = unit.GetVisualOffset();
        return new Vector3(
            unit.transform.position.x - vo.x,
            unit.transform.position.y - vo.y, 0f);
    }

    private GridPosition GetDestinationGridPositionFromWorld(Vector3 cellCentre)
    {
        var destGrid = DestinationRoom.roomGrid;
        if (destGrid == null) return GetDestinationSpawnPos();

        GridPosition gp = destGrid.GetGridPosition(cellCentre);
        if (destGrid.IsValidGridPosition(gp) && destGrid.IsWalkableIgnoreOccupancy(gp))
            return gp;

        return FindNearestWalkable(cellCentre, destGrid);
    }

    private static GridPosition FindNearestWalkable(Vector3 worldPos, RoomGrid grid)
    {
        GridPosition best     = new(grid.GetWidth() / 2, grid.GetHeight() / 2);
        float        bestDist = float.MaxValue;

        for (int x = 0; x < grid.GetWidth(); x++)
        for (int y = 0; y < grid.GetHeight(); y++)
        {
            var gp = new GridPosition(x, y);
            if (!grid.IsWalkableIgnoreOccupancy(gp)) continue;
            Vector3 gpWorld = grid.GetWorldPosition(gp);
            float   d       = Vector2.Distance(
                new Vector2(worldPos.x, worldPos.y),
                new Vector2(gpWorld.x,  gpWorld.y));
            if (d < bestDist) { bestDist = d; best = gp; }
        }
        return best;
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

    // ── Singleplayer room cleared ──────────────────────────────────────────

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
                bool match = placed.roomGrid == clearedRoom ||
                             placed.roomGrid?.gameObject.name == clearedRoom.gameObject.name;
                if (!match) continue;

                var connectedDirs = new List<LevelGenerator.Direction>();
                foreach (LevelGenerator.Direction dir in
                    System.Enum.GetValues(typeof(LevelGenerator.Direction)))
                {
                    if (gen.GetConnectedRoom(placed, dir) != null)
                    {
                        connectedDirs.Add(dir);
                        placed.roomGrid.SetDoorState(dir, true);
                    }
                }
                placed.connector?.OpenConnectedDoors(connectedDirs);
                break;
            }
        }

        foreach (var et in FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None))
        {
            bool touches = false;
            if (et.Hallway != null)
            {
                var roomA = et.Hallway.RoomA?.roomGrid;
                var roomB = et.Hallway.RoomB?.roomGrid;
                bool aMatch = roomA != null && (roomA == clearedRoom ||
                    roomA.gameObject.name == clearedRoom.gameObject.name);
                bool bMatch = roomB != null && (roomB == clearedRoom ||
                    roomB.gameObject.name == clearedRoom.gameObject.name);
                touches = aMatch || bMatch;
            }
            bool destIsRoom = et.DestinationRoom?.roomGrid == clearedRoom ||
                et.DestinationRoom?.roomGrid?.gameObject.name == clearedRoom.gameObject.name;

            if (touches || destIsRoom)
            {
                et.SetExitLocked(false);
                et.ResetTrigger();
            }
        }

        CameraController2D.Instance?.SetCombatState(false);
        StartCoroutine(InvalidateCacheAfterDoorsOpen());
        RoomManager.Instance?.NotifyRoomCleared(
            FindPlacedRoomForGrid(clearedRoom));
    }

    private IEnumerator InvalidateCacheAfterDoorsOpen()
    {
        yield return null;
        yield return null;
        FindLocalUnit()?.GetMoveAction()?.InvalidateCache();
    }

    // ── Singleplayer door helpers ───────────────────────────────────────────

    private void LockRoomExitsForEnemies(LevelGenerator.PlacedRoom room)
    {
        roomBorderTriggers = new List<HallwayWalkTrigger>();

        foreach (var et in FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None))
        {
            if (et.pairedWalkTrigger == null) continue;

            bool touches = false;
            if (et.Hallway != null)
            {
                var roomA = et.Hallway.RoomA?.roomGrid;
                var roomB = et.Hallway.RoomB?.roomGrid;
                bool aIsOurs = roomA != null && (roomA == room.roomGrid ||
                    roomA.gameObject.name == room.roomGrid.gameObject.name);
                bool bIsOurs = roomB != null && (roomB == room.roomGrid ||
                    roomB.gameObject.name == room.roomGrid.gameObject.name);
                touches = aIsOurs || bIsOurs;
            }
            if (!touches) continue;

            et.SetExitLocked(true);
            roomBorderTriggers.Add(et.pairedWalkTrigger);
        }

        LockRoomDoors(room);
    }

    private static void LockRoomDoors(LevelGenerator.PlacedRoom room)
    {
        if (room?.connector == null) return;
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) { room.connector.CloseAllDoors(); return; }

        var connectedDirs = new List<LevelGenerator.Direction>();
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
            if (gen != null && gen.GetConnectedRoom(room, dir) == null) continue;
            bool shouldBeOpen = room.roomGrid.GetDoorState(dir);
            room.connector.SetDoorOpen(dir, shouldBeOpen);
        }
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

    private static LevelGenerator.PlacedRoom FindPlacedRoomForGrid(RoomGrid grid)
    {
        if (grid == null) return null;
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return null;
        foreach (var placed in gen.GetAllRooms())
            if (placed.roomGrid == grid ||
                placed.roomGrid?.gameObject.name == grid.gameObject.name)
                return placed;
        return null;
    }
}
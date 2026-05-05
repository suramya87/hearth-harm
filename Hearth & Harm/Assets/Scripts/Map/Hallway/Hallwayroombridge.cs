using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives ALL room/hallway grid transitions for the local player.
///
/// FIX — HALLWAY MOVEMENT BROKEN:
/// The previous version called unit.SetCurrentRoomGrid() which only swapped
/// the RoomGrid reference. unit.GetGridPosition() still returned the old
/// room-space cell, which is meaningless in the hallway's coordinate space.
/// Pathfinder.FindPath(oldRoomCell, target) found nothing so GetValidTargets()
/// returned empty — no tiles highlighted, no movement possible.
///
/// Now we call unit.SwitchGrid(newGrid) which:
///   1. Removes occupancy from the old grid at the old cell.
///   2. Derives the new cell from the unit's current world position.
///   3. Adds occupancy on the new grid at the new cell.
///   4. Updates both currentRoomGrid and gridPosition atomically.
///
/// This means GetValidTargets() always starts pathfinding from a valid cell
/// in the current grid, whether that's a room or a hallway.
///
/// PRIORITY SCORING:
/// When the player is at a seam (world pos inside both a hallway and a room),
/// rooms win via a +1000 bonus so exit into roomB is detected correctly.
/// </summary>
public class HallwayRoomBridge : MonoBehaviour
{
    private LevelGenerator levelGen;
    private Unit           unit;

    private RoomGrid lastGrid;

    private readonly HashSet<RoomGrid> activatedRooms = new();

    // ── Static factory ─────────────────────────────────────────────────────

    public static void Attach(Unit u, LevelGenerator gen)
    {
        if (u == null || gen == null) return;

        var bridge = u.GetComponent<HallwayRoomBridge>();
        if (bridge == null) bridge = u.gameObject.AddComponent<HallwayRoomBridge>();
        bridge.levelGen       = gen;
        bridge.unit           = u;
        bridge.lastGrid       = null;
        bridge.activatedRooms.Clear();
    }

    // ── Per-frame detection ────────────────────────────────────────────────

    private void Update()
    {
        if (levelGen == null || unit == null) return;

        if (GameManager.IsMultiplayer)
        {
            var netObj = GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null && !netObj.IsOwner) return;
        }

        Vector3 worldPos = transform.position;

        RoomGrid                      bestGrid      = null;
        float                         bestScore     = float.MinValue;
        LevelGenerator.PlacedRoom     bestPlaced    = null;
        bool                          bestIsHallway = false;

        // ── Score rooms (priority bonus so they beat hallways at seams) ────
        foreach (var placed in levelGen.GetAllRooms())
        {
            var rg = placed.roomGrid;
            if (rg == null || !rg.IsInitialized()) continue;
            if (!rg.IsPositionInRoom(worldPos)) continue;

            float score = PenetrationScore(rg, worldPos) + 1000f;
            if (score > bestScore)
            {
                bestScore     = score;
                bestGrid      = rg;
                bestPlaced    = placed;
                bestIsHallway = false;
            }
        }

        // ── Score hallways ─────────────────────────────────────────────────
        foreach (var hallway in levelGen.GetAllHallways())
        {
            if (!hallway.IsReady) continue;
            var rg = hallway.RoomGrid;
            if (!rg.IsPositionInRoom(worldPos)) continue;

            float score = PenetrationScore(rg, worldPos);
            if (score > bestScore)
            {
                bestScore     = score;
                bestGrid      = rg;
                bestPlaced    = hallway.AsPlacedRoom();
                bestIsHallway = true;
            }
        }

        if (bestGrid == null || bestGrid == lastGrid) return;

        // ── Grid changed — switch atomically ───────────────────────────────
        lastGrid = bestGrid;

        // SwitchGrid updates BOTH currentRoomGrid AND gridPosition in one call,
        // derived from the unit's current world position in the new grid's space.
        // This is the critical fix — without this, gridPosition stays in the old
        // grid's coordinate space and Pathfinder finds no valid start node.
        unit.SwitchGrid(bestGrid);

        RoomManager.Instance?.SetCurrentRoom(bestPlaced);

        Debug.Log($"[HallwayRoomBridge] → '{bestPlaced?.roomInstance?.name}' " +
                  $"isHallway={bestIsHallway}  " +
                  $"unitGridPos={unit.GetGridPosition()}");

        if (!bestIsHallway)
            TryActivateRoom(bestPlaced);
    }

    // ── Penetration depth score ────────────────────────────────────────────

    private static float PenetrationScore(RoomGrid grid, Vector3 worldPos)
    {
        var floor = grid.GetFloorTilemap();
        if (floor == null) return 0f;

        var bounds = floor.localBounds;
        if (bounds.size.x <= 0f || bounds.size.y <= 0f) return 0f;

        Vector3 centre = floor.transform.TransformPoint(bounds.center);
        float nx = Mathf.Abs(worldPos.x - centre.x) / (bounds.size.x * 0.5f);
        float ny = Mathf.Abs(worldPos.y - centre.y) / (bounds.size.y * 0.5f);

        return 1f - Mathf.Max(nx, ny);
    }

    // ── Room activation ────────────────────────────────────────────────────

    private void TryActivateRoom(LevelGenerator.PlacedRoom placed)
    {
        if (placed == null || placed.roomGrid == null) return;
        if (placed.prefabData?.roomType == LevelGenerator.RoomType.Start) return;
        if (activatedRooms.Contains(placed.roomGrid)) return;

        bool alreadyActive = EnemyManager.Instance != null &&
                             EnemyManager.Instance.GetEnemiesInRoom(placed.roomGrid).Count > 0;
        if (alreadyActive) { activatedRooms.Add(placed.roomGrid); return; }

        activatedRooms.Add(placed.roomGrid);
        placed.connector?.CloseAllDoors();

        var spawner = FindAnyObjectByType<EnemySpawner>();
        if (spawner != null) spawner.SpawnForRoom(placed);
        else Debug.LogWarning("[HallwayRoomBridge] No EnemySpawner in scene.");

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared += OnRoomCleared;

        Debug.Log($"[HallwayRoomBridge] Activated '{placed.roomInstance.name}'.");
    }

    private void OnRoomCleared(RoomGrid clearedRoom)
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return;

        foreach (var placed in gen.GetAllRooms())
        {
            if (placed.roomGrid != clearedRoom) continue;
            foreach (LevelGenerator.Direction dir in
                System.Enum.GetValues(typeof(LevelGenerator.Direction)))
                if (gen.GetConnectedRoom(placed, dir) != null)
                    placed.connector?.SetDoorOpen(dir, true);
            break;
        }

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= OnRoomCleared;
    }
}
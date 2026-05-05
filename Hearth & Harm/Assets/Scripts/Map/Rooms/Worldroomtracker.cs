using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Detects which room the player is in by checking HasTile on each room's
/// floor tilemap every frame. Fires room activation on first entry.
///
/// SEAMLESS HALLWAY MOVEMENT:
///   Because hallway tiles are painted directly into room tilemaps, the player
///   naturally crosses from roomA's tile set into roomB's tile set when they
///   walk far enough through the hallway. No grid switching is needed.
///
/// BIAS FIX — FIRST-MATCH REMOVED:
///   The original version broke on `first hit` which meant whichever room
///   appeared first in placedRooms always won at seams. This version scores
///   every candidate by how deeply the player's world position penetrates that
///   room's floor tilemap bounds, then picks the deepest. This ensures the
///   player is always attributed to the room whose tiles most "surround" them.
///
/// UNIT GRID STATE:
///   When the room changes, SetGridState updates the unit's RoomGrid reference
///   and derives a new GridPosition from world position. Occupancy is transferred
///   cleanly from the old grid to the new one.
/// </summary>
public class WorldRoomTracker : MonoBehaviour
{
    private LevelGenerator             levelGen;
    private LevelGenerator.PlacedRoom  currentRoom;
    private readonly HashSet<RoomGrid> activatedRooms = new();

    // ── Factory ────────────────────────────────────────────────────────────

    public static WorldRoomTracker Attach(GameObject player, LevelGenerator gen)
    {
        if (player == null || gen == null) return null;
        var t = player.GetComponent<WorldRoomTracker>()
             ?? player.AddComponent<WorldRoomTracker>();
        t.levelGen       = gen;
        t.currentRoom    = null;
        t.activatedRooms.Clear();
        return t;
    }

    // ── Per-frame detection ────────────────────────────────────────────────

    private void Update()
    {
        if (levelGen == null) return;

        if (GameManager.IsMultiplayer)
        {
            var netObj = GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null && !netObj.IsOwner) return;
        }

        Vector3 pos = transform.position;

        // Score every room whose floor tilemap contains this world position.
        // Penetration score = how far inside the tilemap bounds the position is,
        // normalized 0–1. The room with the highest score wins, removing the
        // first-match bias that caused wrong attribution at seams.
        LevelGenerator.PlacedRoom bestRoom  = null;
        float                     bestScore = float.MinValue;

        foreach (var placed in levelGen.GetAllRooms())
        {
            var floor = placed.roomGrid?.GetFloorTilemap();
            if (floor == null) continue;

            // Fast rejection: must have a tile at this cell
            if (!floor.HasTile(floor.WorldToCell(pos))) continue;

            float score = PenetrationScore(floor, pos);
            if (score > bestScore)
            {
                bestScore = score;
                bestRoom  = placed;
            }
        }

        if (bestRoom == null || bestRoom == currentRoom) return;

        var prev    = currentRoom;
        currentRoom = bestRoom;

        RoomManager.Instance?.SetCurrentRoom(bestRoom);

        // Transfer unit grid state to the new room's grid.
        // SetGridState updates both the RoomGrid reference and GridPosition
        // from the unit's current world position — no coordinate remapping needed
        // because all tiles share the same world-space coordinate system.
        var unit = GetComponent<Unit>();
        if (unit != null)
        {
            var newGrid = bestRoom.roomGrid;
            var newCell = newGrid.GetGridPosition(transform.position);

            if (newGrid.IsValidGridPosition(newCell))
            {
                var oldGrid = unit.GetCurrentRoomGrid();
                var oldCell = unit.GetGridPosition();

                // Remove occupancy from the old grid
                if (oldGrid != null && oldGrid != newGrid)
                    oldGrid.RemoveUnitAtGridPosition(oldCell, unit);

                // Update reference + position atomically
                unit.SetGridState(newGrid, newCell);

                // Register occupancy on the new grid (guard against double-add)
                if (!newGrid.HasAnyUnitOnGridPosition(newCell))
                    newGrid.AddUnitAtGridPosition(newCell, unit);
            }
        }

        Debug.Log($"[WorldRoomTracker] Room: '{prev?.roomInstance?.name ?? "none"}' " +
                  $"→ '{bestRoom.roomInstance.name}'  score={bestScore:F3}");

        TryActivateRoom(bestRoom);
    }

    // ── Penetration score ──────────────────────────────────────────────────

    /// <summary>
    /// Returns a 0–1 score representing how far inside the tilemap's local bounds
    /// the world position sits. 0 = exactly on the edge, 1 = dead centre.
    /// Used to break ties when the player is standing on tiles that belong to
    /// both roomA and roomB (e.g. at the midpoint of a hallway).
    /// </summary>
    private static float PenetrationScore(Tilemap floor, Vector3 worldPos)
    {
        // Transform world position into the tilemap's local space so we can
        // compare against localBounds directly.
        Vector3 local  = floor.transform.InverseTransformPoint(worldPos);
        var     bounds = floor.localBounds;

        if (bounds.size.x <= 0f || bounds.size.y <= 0f) return 0f;

        // How far from the edge, normalised by half-extent
        float nx = 1f - Mathf.Abs(local.x - bounds.center.x) / (bounds.extents.x);
        float ny = 1f - Mathf.Abs(local.y - bounds.center.y) / (bounds.extents.y);

        // Minimum of the two axes — this is highest when the point is well
        // inside BOTH axes, which is what we want.
        return Mathf.Min(nx, ny);
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
        else Debug.LogWarning("[WorldRoomTracker] No EnemySpawner in scene.");

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared += OnRoomCleared;

        Debug.Log($"[WorldRoomTracker] Activated '{placed.roomInstance.name}'.");
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

    public LevelGenerator.PlacedRoom GetCurrentRoom() => currentRoom;
}
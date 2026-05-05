using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Grid-based player movement.
///
/// Because hallway tiles are part of room tilemaps, GetValidTargets() works
/// identically in rooms and hallways — one Pathfinder call covers everything.
///
/// CROSS-ROOM MOVE FIX:
/// When the player walks from a room into a hallway (or from a hallway into
/// the next room), the target GridPosition belongs to a different RoomGrid
/// than the one the move started on. MoveAction now detects this at the end
/// of the move by asking the LevelGenerator which room's floor tilemap
/// contains the final world position, then calls PlaceInRoom on that grid.
///
/// This means WorldRoomTracker and MoveAction are always in sync — both use
/// the same "which tilemap has a tile here" logic to determine the current room.
/// </summary>
public class MoveAction : BaseAction
{
    [SerializeField] private int   maxMoveDistance = 4;
    [SerializeField] private float moveSpeed       = 8f;

    private int MoveDistance => playerStats != null
        ? Mathf.Max(0, playerStats.currentStamina)
        : maxMoveDistance;

    public bool CanMove() => MoveDistance > 0;

    public override string GetActionName() => "Move";

    public void Move(GridPosition target, Action onComplete)
    {
        if (!CanMove()) { onComplete?.Invoke(); return; }

        var room = unit.GetCurrentRoomGrid();
        if (room == null) { onComplete?.Invoke(); return; }

        var path = new Pathfinder(room).FindPath(unit.GetGridPosition(), target);
        if (path.Count == 0) { onComplete?.Invoke(); return; }

        int steps    = Mathf.Min(path.Count, MoveDistance);
        var usedPath = path.GetRange(0, steps);
        var finalPos = usedPath[^1];

        room.RemoveUnitAtGridPosition(unit.GetGridPosition(), unit);
        room.AddUnitAtGridPosition(finalPos, unit);

        if (playerStats != null)
            playerStats.currentStamina = Mathf.Max(0, playerStats.currentStamina - steps);

        var waypoints = new List<Vector3>();
        foreach (var gp in usedPath) waypoints.Add(room.GetWorldPosition(gp));

        SetFacingToward(usedPath[0]);
        unitAnimator?.SetMoving(true);
        isActive = true;

        StartCoroutine(MoveAlongPath(waypoints, usedPath, finalPos, room, onComplete));
    }

    private IEnumerator MoveAlongPath(List<Vector3> waypoints,
                                       List<GridPosition> gridPath,
                                       GridPosition finalGP,
                                       RoomGrid startGrid,
                                       Action onComplete)
    {
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (i < gridPath.Count) SetFacingToward(gridPath[i]);

            var target = new Vector3(waypoints[i].x, waypoints[i].y, transform.position.z);
            while (Vector2.Distance(transform.position, target) > 0.05f)
            {
                transform.position = Vector3.MoveTowards(transform.position, target,
                                                          moveSpeed * Time.deltaTime);
                yield return null;
            }
            transform.position = target;
        }

        unitAnimator?.SetMoving(false);
        playerAnimator?.RefreshStaminaState();
        isActive = false;

        // ── Detect which room the player actually landed in ────────────────
        // The target cell may belong to a different room's tilemap than where
        // the move started (e.g. walked from roomA into roomB's half of the
        // hallway). We resolve this by world position, the same way
        // WorldRoomTracker does, so both are always in sync.
        var finalGrid = ResolveGridAtWorldPos(transform.position) ?? unit.GetCurrentRoomGrid();

        // Remove occupancy from start grid if we crossed into a new one
        if (finalGrid != startGrid)
        {
            startGrid.RemoveUnitAtGridPosition(finalGP, unit);
            var finalCell = finalGrid.GetGridPosition(transform.position);
            if (finalGrid.IsValidGridPosition(finalCell))
                finalGP = finalCell;

            // Update RoomManager and unit grid state to match
            var placed = ResolvedPlacedRoom(finalGrid);
            if (placed != null)
                RoomManager.Instance?.SetCurrentRoom(placed);
        }

        if (GameManager.IsMultiplayer)
        {
            var bridge = unit.GetComponent<NetworkedPlayerBridge>();
            if (bridge != null && bridge.IsOwner)
                bridge.SyncGridPosition(finalGrid, finalGP);
        }
        else
        {
            unit.PlaceInRoom(finalGrid, finalGP);
        }

        onComplete?.Invoke();
    }

    // ── Grid resolution helpers ────────────────────────────────────────────

    /// <summary>
    /// Finds the RoomGrid whose floor tilemap contains worldPos.
    /// Uses the same logic as WorldRoomTracker so they're always in sync.
    /// Returns null if no room contains the position.
    /// </summary>
    private RoomGrid ResolveGridAtWorldPos(Vector3 worldPos)
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return null;

        RoomGrid best      = null;
        float    bestScore = float.MinValue;

        foreach (var placed in gen.GetAllRooms())
        {
            var floor = placed.roomGrid?.GetFloorTilemap();
            if (floor == null) continue;
            if (!floor.HasTile(floor.WorldToCell(worldPos))) continue;

            // Penetration score — same formula as WorldRoomTracker
            Vector3 local  = floor.transform.InverseTransformPoint(worldPos);
            var     bounds = floor.localBounds;
            if (bounds.size.x <= 0f || bounds.size.y <= 0f) continue;

            float nx = 1f - Mathf.Abs(local.x - bounds.center.x) / bounds.extents.x;
            float ny = 1f - Mathf.Abs(local.y - bounds.center.y) / bounds.extents.y;
            float score = Mathf.Min(nx, ny);

            if (score > bestScore)
            {
                bestScore = score;
                best      = placed.roomGrid;
            }
        }

        return best;
    }

    /// <summary>
    /// Returns the PlacedRoom that owns a given RoomGrid.
    /// </summary>
    private LevelGenerator.PlacedRoom ResolvedPlacedRoom(RoomGrid grid)
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return null;
        foreach (var placed in gen.GetAllRooms())
            if (placed.roomGrid == grid) return placed;
        return null;
    }

    // ── Valid targets ──────────────────────────────────────────────────────

    public bool IsValidTarget(GridPosition gp) => GetValidTargets().Contains(gp);
    public bool isValidActionGridPosition(GridPosition gp) => IsValidTarget(gp);
    public List<GridPosition> GetValidActionGridPositionList() => GetValidTargets();

    private List<GridPosition> GetValidTargets()
    {
        var list = new List<GridPosition>();
        if (!CanMove()) return list;

        var room = unit.GetCurrentRoomGrid();
        if (room == null) return list;

        var unitPos = unit.GetGridPosition();
        int dist    = MoveDistance;

        if (!room.IsValidGridPosition(unitPos)) return list;

        var pf = new Pathfinder(room);

        for (int dx = -dist; dx <= dist; dx++)
        for (int dy = -dist; dy <= dist; dy++)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dy) > dist) continue;
            if (dx == 0 && dy == 0) continue;

            var test = new GridPosition(unitPos.x + dx, unitPos.y + dy);
            if (!room.IsValidGridPosition(test))       continue;
            if (!room.IsWalkableIgnoreOccupancy(test)) continue;
            if (room.HasAnyUnitOnGridPosition(test))   continue;

            var path = pf.FindPath(unitPos, test);
            if (path.Count > 0 && path.Count <= dist) list.Add(test);
        }

        return list;
    }

    public int GetMoveCost(GridPosition target)
    {
        var room = unit?.GetCurrentRoomGrid();
        if (room == null) return -1;
        var path = new Pathfinder(room).FindPath(unit.GetGridPosition(), target);
        return path.Count == 0 ? -1 : path.Count;
    }

    private void SetFacingToward(GridPosition next)
    {
        var cur = unit.GetGridPosition();
        int dx = next.x - cur.x, dy = next.y - cur.y;
        unitAnimator?.SetFacing(new Vector2Int(
            dx == 0 ? 0 : (int)Mathf.Sign(dx),
            dy == 0 ? 0 : (int)Mathf.Sign(dy)));
    }
}
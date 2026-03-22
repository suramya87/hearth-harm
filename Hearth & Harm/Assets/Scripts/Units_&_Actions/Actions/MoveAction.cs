using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Moves the unit along a path on the tilemap grid.
/// Stamina = move distance. Each tile costs 1 stamina.
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

    // ── Execution ──────────────────────────────────────────────────────────

    public void Move(GridPosition target, Action onComplete)
    {
        if (!CanMove()) { onComplete?.Invoke(); return; }

        var room = unit.GetCurrentRoomGrid();
        if (room == null) { onComplete?.Invoke(); return; }

        var path = new Pathfinder(room).FindPath(unit.GetGridPosition(), target);
        if (path.Count == 0) { onComplete?.Invoke(); return; }

        int steps     = Mathf.Min(path.Count, MoveDistance);
        var usedPath  = path.GetRange(0, steps);
        var finalPos  = usedPath[^1];

        // Update occupancy
        room.RemoveUnitAtGridPosition(unit.GetGridPosition(), unit);
        room.AddUnitAtGridPosition(finalPos, unit);

        // Deduct stamina
        if (playerStats != null)
            playerStats.currentStamina = Mathf.Max(0, playerStats.currentStamina - steps);

        // Build world waypoints
        var waypoints = new List<Vector3>();
        foreach (var gp in usedPath) waypoints.Add(room.GetWorldPosition(gp));

        isActive = true;
        StartCoroutine(MoveAlongPath(waypoints, finalPos, onComplete));
    }

    private IEnumerator MoveAlongPath(List<Vector3> waypoints, GridPosition finalGP,
                                      Action onComplete)
    {
        foreach (var wp in waypoints)
        {
            // Keep Z for sorting
            var target = new Vector3(wp.x, wp.y, transform.position.z);
            while (Vector2.Distance(transform.position, target) > 0.05f)
            {
                transform.position = Vector3.MoveTowards(transform.position, target,
                                                          moveSpeed * Time.deltaTime);
                yield return null;
            }
            transform.position = target;
        }

        // Sync grid position via PlaceInRoom (updates occupancy + position correctly)
        unit.PlaceInRoom(unit.GetCurrentRoomGrid(), finalGP);

        isActive = false;
        onComplete?.Invoke();
    }

    // ── Validity ───────────────────────────────────────────────────────────

    public bool IsValidTarget(GridPosition gp) =>
        GetValidTargets().Contains(gp);

    // Back-compat name used by old UI
    public bool isValidActionGridPosition(GridPosition gp) => IsValidTarget(gp);

    public List<GridPosition> GetValidActionGridPositionList() => GetValidTargets();

    private List<GridPosition> GetValidTargets()
    {
        var list = new List<GridPosition>();
        if (!CanMove()) return list;

        var room = unit.GetCurrentRoomGrid();
        if (room == null) return list;

        var unitPos  = unit.GetGridPosition();
        int dist     = MoveDistance;
        var pf       = new Pathfinder(room);

        for (int dx = -dist; dx <= dist; dx++)
        for (int dy = -dist; dy <= dist; dy++)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dy) > dist) continue;
            if (dx == 0 && dy == 0) continue;

            var test = new GridPosition(unitPos.x + dx, unitPos.y + dy);
            if (!room.IsValidGridPosition(test)) continue;
            if (!room.IsWalkableIgnoreOccupancy(test)) continue;
            if (room.HasAnyUnitOnGridPosition(test))   continue;

            var path = pf.FindPath(unitPos, test);
            if (path.Count > 0 && path.Count <= dist) list.Add(test);
        }

        return list;
    }

    /// <summary>Returns the tile cost to reach a specific position (number of steps).</summary>
    public int GetMoveCost(GridPosition target)
    {
        var room = unit?.GetCurrentRoomGrid();
        if (room == null) return -1;
        var path = new Pathfinder(room).FindPath(unit.GetGridPosition(), target);
        return path.Count == 0 ? -1 : path.Count;
    }
}
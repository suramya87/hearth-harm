using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveAction : BaseAction
{
    [SerializeField] private int   maxMoveDistance = 4;
    [SerializeField] private float moveSpeed       = 8f;

    /// <summary>True while a move animation is in progress.</summary>
    public bool IsActive => isActive;

    private int MoveDistance => playerStats != null
        ? Mathf.Max(0, playerStats.currentStamina)
        : maxMoveDistance;

    public bool CanMove() => MoveDistance > 0;

    public override string GetActionName() => "Move";

    // ── Move ───────────────────────────────────────────────────────────────

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

        // Update grid occupancy immediately so pathfinding elsewhere is
        // correct before the visual animation completes.
        room.RemoveUnitAtGridPosition(unit.GetGridPosition(), unit);
        room.AddUnitAtGridPosition(finalPos, unit);

        // Snapshot the grid we started on. If a HallwayWalkTrigger hands the
        // unit to a different grid during the animation, we detect that at the
        // end and skip the PlaceInRoom snap (the trigger handles placement).
        var startingGrid = room;

        if (playerStats != null)
            playerStats.currentStamina = Mathf.Max(0, playerStats.currentStamina - steps);

        var waypoints = new List<Vector3>();
        foreach (var gp in usedPath)
            waypoints.Add(room.GetWorldPosition(gp));

        SetFacingToward(usedPath[0]);
        unitAnimator?.SetMoving(true);

        isActive = true;
        StartCoroutine(MoveAlongPath(waypoints, usedPath, finalPos, startingGrid, onComplete));
    }

    // ── Move coroutine ─────────────────────────────────────────────────────

    private IEnumerator MoveAlongPath(
        List<Vector3>      waypoints,
        List<GridPosition> gridPath,
        GridPosition       finalGP,
        RoomGrid           startingGrid,
        Action             onComplete)
    {
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (i < gridPath.Count)
                SetFacingToward(gridPath[i]);

            var target = new Vector3(waypoints[i].x, waypoints[i].y, transform.position.z);

            while (Vector2.Distance(transform.position, target) > 0.05f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position, target, moveSpeed * Time.deltaTime);
                yield return null;
            }
            transform.position = target;
        }

        unitAnimator?.SetMoving(false);
        playerAnimator?.RefreshStaminaState();
        isActive = false;

        onComplete?.Invoke();

        // ── Post-move grid sync ────────────────────────────────────────────
        // Only call PlaceInRoom if the unit is STILL on the same grid it
        // started the move on. If a trigger handed the unit to a different
        // grid during animation (hallway or room), skip — that trigger owns
        // the placement and calling PlaceInRoom here would create a ghost
        // by double-registering the unit on the old grid position.
        var currentGrid = unit.GetCurrentRoomGrid();
        if (currentGrid != startingGrid)
        {
            Debug.Log($"[MoveAction] Grid changed mid-move " +
                      $"({startingGrid?.gameObject.name} → {currentGrid?.gameObject.name}), " +
                      $"skipping end-of-move PlaceInRoom.");
            yield break;
        }

        if (GameManager.IsMultiplayer)
        {
            var bridge = unit.GetComponent<NetworkedPlayerBridge>();
            if (bridge != null && bridge.IsOwner)
                bridge.SyncGridPosition(currentGrid, finalGP);
        }
        else
        {
            unit.PlaceInRoom(currentGrid, finalGP);
        }
    }

    // ── Facing ─────────────────────────────────────────────────────────────

    private void SetFacingToward(GridPosition next)
    {
        var current = unit.GetGridPosition();
        int dx = next.x - current.x;
        int dy = next.y - current.y;

        unitAnimator?.SetFacing(new Vector2Int(
            dx == 0 ? 0 : (int)Mathf.Sign(dx),
            dy == 0 ? 0 : (int)Mathf.Sign(dy)
        ));
    }

    // ── Valid targets ──────────────────────────────────────────────────────

    public bool IsValidTarget(GridPosition gp)                 => GetValidTargets().Contains(gp);
    public bool isValidActionGridPosition(GridPosition gp)     => IsValidTarget(gp);
    public List<GridPosition> GetValidActionGridPositionList() => GetValidTargets();

    private List<GridPosition> GetValidTargets()
    {
        var list = new List<GridPosition>();
        if (!CanMove()) return list;

        var room = unit.GetCurrentRoomGrid();
        if (room == null)
        {
            Debug.LogWarning("[MoveAction] GetValidTargets: currentRoomGrid is null!");
            return list;
        }

        var unitPos = unit.GetGridPosition();
        int dist    = MoveDistance;
        var pf      = new Pathfinder(room);

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

    // ── Move cost ──────────────────────────────────────────────────────────

    public int GetMoveCost(GridPosition target)
    {
        var room = unit?.GetCurrentRoomGrid();
        if (room == null) return -1;
        var path = new Pathfinder(room).FindPath(unit.GetGridPosition(), target);
        return path.Count == 0 ? -1 : path.Count;
    }
}
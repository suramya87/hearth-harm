using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MoveAction : BaseAction
{
    [SerializeField] private int   maxMoveDistance = 4;
    [SerializeField] private float moveSpeed       = 8f;

    // Exposed so HallwayWalkTrigger and HallwayEntryTrigger can wait on it
    public bool IsActive => isActive;

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

        // Update grid occupancy immediately (before the visual move)
        room.RemoveUnitAtGridPosition(unit.GetGridPosition(), unit);
        room.AddUnitAtGridPosition(finalPos, unit);

        if (playerStats != null)
            playerStats.currentStamina = Mathf.Max(0, playerStats.currentStamina - steps);

        var waypoints = new List<Vector3>();
        foreach (var gp in usedPath) waypoints.Add(room.GetWorldPosition(gp));

        SetFacingToward(usedPath[0]);
        unitAnimator?.SetMoving(true);

        isActive = true;
        StartCoroutine(MoveAlongPath(waypoints, usedPath, finalPos, onComplete));
    }

    private IEnumerator MoveAlongPath(List<Vector3> waypoints, List<GridPosition> gridPath,
                                      GridPosition finalGP, Action onComplete)
    {
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (i < gridPath.Count)
                SetFacingToward(gridPath[i]);

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

        onComplete?.Invoke();

        // Skip PlaceInRoom snap if the unit is on a hallway grid —
        // HallwayEntryTrigger handles the transition into the destination room.
        bool onHallway = unit.GetCurrentRoomGrid() is { } currentGrid
                      && currentGrid.GetComponent<HallwayGrid>() != null;

        if (onHallway) yield break;

        if (GameManager.IsMultiplayer)
        {
            var bridge = unit.GetComponent<NetworkedPlayerBridge>();
            if (bridge != null && bridge.IsOwner)
                bridge.SyncGridPosition(unit.GetCurrentRoomGrid(), finalGP);
        }
        else
        {
            unit.PlaceInRoom(unit.GetCurrentRoomGrid(), finalGP);
        }
    }

    private void SetFacingToward(GridPosition next)
    {
        var current = unit.GetGridPosition();
        int dx = next.x - current.x;
        int dy = next.y - current.y;

        var dir = new Vector2Int(
            dx == 0 ? 0 : (int)Mathf.Sign(dx),
            dy == 0 ? 0 : (int)Mathf.Sign(dy)
        );

        unitAnimator?.SetFacing(dir);
    }

    public bool IsValidTarget(GridPosition gp) => GetValidTargets().Contains(gp);
    public bool isValidActionGridPosition(GridPosition gp) => IsValidTarget(gp);
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

        Debug.Log($"[MoveAction] GetValidTargets | grid={room.gameObject.name} " +
                  $"| unitPos={unitPos} " +
                  $"| validPos={room.IsValidGridPosition(unitPos)} " +
                  $"| walkable={room.IsWalkableIgnoreOccupancy(unitPos)} " +
                  $"| dist={dist} " +
                  $"| gridW={room.GetWidth()} gridH={room.GetHeight()}");

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

        Debug.Log($"[MoveAction] Found {list.Count} valid targets in {room.gameObject.name}");

        return list;
    }

    public int GetMoveCost(GridPosition target)
    {
        var room = unit?.GetCurrentRoomGrid();
        if (room == null) return -1;
        var path = new Pathfinder(room).FindPath(unit.GetGridPosition(), target);
        return path.Count == 0 ? -1 : path.Count;
    }
}
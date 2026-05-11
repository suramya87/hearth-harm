using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class MoveAction : BaseAction
{
    [SerializeField] private int   maxMoveDistance = 4;
    [SerializeField] private float moveSpeed       = 8f;

    public bool IsActive => isActive;

    private int MoveDistance => playerStats != null
    ? Mathf.Max(0, playerStats.currentStamina)
    : maxMoveDistance;

    private bool IsInCombatRoom()
    {
        RoomGrid room = unit != null ? unit.GetCurrentRoomGrid() : null;

        if (room == null || EnemyManager.Instance == null)
            return false;

        return EnemyManager.Instance.GetEnemiesInRoom(room).Count > 0;
    }
    public override string GetActionName() => "Move";

    /// <summary>
    /// Call this from UnitActionSystem.cs to handle clicks specifically for moving.
    /// Includes the debugging and UI blocking logic from the PCGTDST branch.
    /// </summary>
    public void HandleActionInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                Debug.Log("<color=yellow>[MoveAction Input] Click ignored: Pointer is over UI.</color>");
                return;
            }

            Vector3 mouseWorldPosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            var room = unit.GetCurrentRoomGrid();
            
            if (room == null)
            {
                Debug.LogWarning("[MoveAction Input] Click ignored: Unit has no current RoomGrid.");
                return;
            }

            GridPosition targetGP = room.GetGridPosition(mouseWorldPosition);
            bool isValid = IsValidTarget(targetGP);
            string color = isValid ? "lime" : "red";

            Debug.Log($"<color={color}>[MoveAction Input] Clicked GP: {targetGP} | Valid: {isValid} | Room: {room.name}</color>");

            if (isValid)
            {
                Move(targetGP, () => Debug.Log("[MoveAction] Move Complete."));
            }
            else
            {
                if (!room.IsValidGridPosition(targetGP)) Debug.Log(" -> Reason: Outside Grid Bounds.");
                else if (room.IsWall(targetGP)) Debug.Log(" -> Reason: It's a Wall.");
                else if (GetMoveCost(targetGP) > MoveDistance) Debug.Log(" -> Reason: Too far / No Path.");
            }
        }
    }

    public void RefreshValidTargets()
    {
        if (UnitActionSystem.Instance != null)
            UnitActionSystem.Instance.SetSelectedAction(this);
    }

    public void ForceSyncGridPosition(RoomGrid newGrid, GridPosition newPos)
    {
        if (newGrid == null) return;

        RoomGrid oldGrid = unit.GetCurrentRoomGrid();
        GridPosition oldPos = unit.GetGridPosition();

        if (oldGrid != null)
        {
            Debug.Log($"[MoveAction] CLEANUP: Removing {unit.name} from {oldGrid.name} at {oldPos}");
            oldGrid.RemoveUnitAtGridPosition(oldPos, unit);
        }

        unit.PlaceInRoom(newGrid, newPos);
        newGrid.AddUnitAtGridPosition(newPos, unit);
    }

    public void Move(GridPosition target, Action onComplete)
    {
        var room = unit.GetCurrentRoomGrid();
        if (room == null || isActive) { onComplete?.Invoke(); return; }

        GridPosition startPos = unit.GetGridPosition();
        
        var path = new Pathfinder(room).FindPath(startPos, target);
        if (path == null || path.Count == 0) 
        { 
            onComplete?.Invoke(); 
            return; 
        }

        int steps = Mathf.Min(path.Count, MoveDistance);
        var usedPath = path.GetRange(0, steps);
        var finalPos = usedPath[^1];

        // Update grid occupancy immediately
        room.RemoveUnitAtGridPosition(startPos, unit);
        room.AddUnitAtGridPosition(finalPos, unit);

        if (playerStats != null && IsInCombatRoom())
            playerStats.SpendStamina(steps);

        var waypoints = new List<Vector3>();
        foreach (var gp in usedPath) waypoints.Add(room.GetWorldPosition(gp));

        SetFacingToward(usedPath[0]);
        unitAnimator?.SetMoving(true);

        isActive = true;
        // PCGTDST logic: Pass room reference to ensure we don't snap back to wrong room if we transition mid-move
        StartCoroutine(MoveAlongPath(waypoints, usedPath, finalPos, room, onComplete));
    }

    private IEnumerator MoveAlongPath(List<Vector3> waypoints, List<GridPosition> gridPath, GridPosition finalGP, RoomGrid startingGrid, Action onComplete)
    {
        for (int i = 0; i < waypoints.Count; i++)
        {
            var target = new Vector3(waypoints[i].x, waypoints[i].y, transform.position.z);
            while (Vector2.Distance(transform.position, target) > 0.01f)
            {
                transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
                yield return null;
            }
            transform.position = target;
        }

        unitAnimator?.SetMoving(false);
        isActive = false;

        // MULTIPLAYER & ROOM PLACEMENT SYNC
        // Ensure we only place the unit if the room hasn't changed via a trigger during the walk
        if (unit.GetCurrentRoomGrid() == startingGrid)
        {
            if (GameManager.IsMultiplayer)
            {
                var bridge = unit.GetComponent<NetworkedPlayerBridge>();
                if (bridge != null && bridge.IsOwner)
                    bridge.SyncGridPosition(startingGrid, finalGP);
            }
            else
            {
                unit.PlaceInRoom(startingGrid, finalGP);
            }
        }

        onComplete?.Invoke();
    }

    public void SetFacingToward(GridPosition next)
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

    public int GetMoveCost(GridPosition target)
    {
        var room = unit?.GetCurrentRoomGrid();
        if (room == null) return -1;
        var path = new Pathfinder(room).FindPath(unit.GetGridPosition(), target);
        return (path == null || path.Count == 0) ? -1 : path.Count;
    }

    private List<GridPosition> GetValidTargets()
    {
        var list = new List<GridPosition>();
        var room = unit.GetCurrentRoomGrid();
        if (room == null) return list;

        var unitPos = unit.GetGridPosition();
        int dist    = MoveDistance;
        var pf      = new Pathfinder(room);

        for (int dx = -dist; dx <= dist; dx++)
        for (int dy = -dist; dy <= dist; dy++)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dy) > dist || (dx == 0 && dy == 0)) continue;

            var test = new GridPosition(unitPos.x + dx, unitPos.y + dy);
            if (!room.IsValidGridPosition(test) || !room.IsWalkableIgnoreOccupancy(test)) continue;
            
            var path = pf.FindPath(unitPos, test);
            if (path != null && path.Count > 0 && path.Count <= dist) list.Add(test);
        }
        return list;
    }
}
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

    private List<Vector3>      cachedReachableWorld;
    private List<GridPosition> cachedReachableGP;
    private bool               cacheDirty = true;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void OnEnable()
    {
        cacheDirty = true;
        LevelGenerator.OnLevelReady  += OnLevelReady;
        RoomManager.OnAnyRoomChanged += OnRoomChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady  -= OnLevelReady;
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;
        InvalidateCache();
    }

    private void OnLevelReady()                              => InvalidateCache();
    private void OnRoomChanged(LevelGenerator.PlacedRoom _) => InvalidateCache();

    public void InvalidateCache()
    {
        cachedReachableWorld = null;
        cachedReachableGP    = null;
        cacheDirty           = true;
    }

    private bool IsInCombatRoom()
    {
        RoomGrid room = unit != null ? unit.GetCurrentRoomGrid() : null;
        if (room == null || EnemyManager.Instance == null) return false;
        return EnemyManager.Instance.GetEnemiesInRoom(room).Count > 0;
    }

    public override string GetActionName() => "Move";

    // ── Input ──────────────────────────────────────────────────────────────

    public void HandleActionInput()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Vector3 raw        = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector3 mouseWorld = new Vector3(raw.x, raw.y, 0f);

        if (UnifiedWorldGrid.Instance != null)
        {
            var reachable = GetValidActionWorldPositions();

            if (reachable.Count == 0)
            {
                Debug.LogWarning("[MoveAction] Reachable list is empty.");
                return;
            }

            // Reachable world positions are raw cell centres (no visual offset).
            // The click comes in at raw world coords too, so comparison is direct.
            Vector3 best     = default;
            float   bestDist = float.MaxValue;
            foreach (var w in reachable)
            {
                float d = Vector2.Distance(new Vector2(w.x, w.y),
                                           new Vector2(mouseWorld.x, mouseWorld.y));
                if (d < bestDist) { bestDist = d; best = w; }
            }

            // Accept clicks within one full tile (cellSize is typically 1).
            if (bestDist < 1.0f)
            {
                Move(best, () => Debug.Log("[MoveAction] Move Complete."));
                return;
            }

            Debug.Log($"<color=red>[MoveAction] No reachable cell near {mouseWorld} " +
                      $"(closest {bestDist:F2} away)</color>");
            return;
        }

        // Legacy fallback.
        var room = unit.GetCurrentRoomGrid();
        if (room == null) return;
        GridPosition gp = room.GetGridPosition(mouseWorld);
        if (IsValidTarget(gp))
            Move(gp, () => Debug.Log("[MoveAction] Move Complete."));
    }

    public void RefreshValidTargets()
    {
        InvalidateCache();
        if (UnitActionSystem.Instance != null)
            UnitActionSystem.Instance.SetSelectedAction(this);
    }

    public void ForceSyncGridPosition(RoomGrid newGrid, GridPosition newPos)
    {
        if (newGrid == null) return;
        var oldGrid = unit.GetCurrentRoomGrid();
        var oldPos  = unit.GetGridPosition();
        if (oldGrid != null) oldGrid.RemoveUnitAtGridPosition(oldPos, unit);
        unit.PlaceInRoom(newGrid, newPos);
        newGrid.AddUnitAtGridPosition(newPos, unit);
    }

    // ── Move: world-position entry point (unified) ─────────────────────────

    public void Move(Vector3 targetWorld, Action onComplete)
    {
        if (isActive) { onComplete?.Invoke(); return; }

        if (UnifiedWorldGrid.Instance == null)
        {
            var r = unit.GetCurrentRoomGrid();
            if (r == null) { onComplete?.Invoke(); return; }
            Move(r.GetGridPosition(targetWorld), onComplete);
            return;
        }

        // targetWorld is a raw cell centre — find the path from the unit's
        // current raw cell centre (no visual offset involved).
        Vector3 startWorld = GetUnitCellCentreWorld();
        var worldPath = UnifiedPathfinder.FindWorldPath(startWorld, targetWorld);
        if (worldPath == null || worldPath.Count == 0) { onComplete?.Invoke(); return; }

        int steps    = Mathf.Min(worldPath.Count, MoveDistance);
        var usedPath = worldPath.GetRange(0, steps);

        InvalidateCache();
        StartCoroutine(MoveAlongWorldPath(usedPath, onComplete));
    }

    // ── Move: GridPosition entry point (legacy / AI) ───────────────────────

    public void Move(GridPosition target, Action onComplete)
    {
        var room = unit.GetCurrentRoomGrid();
        if (room == null || isActive) { onComplete?.Invoke(); return; }

        if (UnifiedWorldGrid.Instance != null)
        {
            // Convert to raw cell centre — no visual offset.
            Move(room.GetWorldPosition(target), onComplete);
            return;
        }

        // Pure local path.
        GridPosition startPos = unit.GetGridPosition();
        var path = new Pathfinder(room).FindPath(startPos, target);
        if (path == null || path.Count == 0) { onComplete?.Invoke(); return; }

        int steps    = Mathf.Min(path.Count, MoveDistance);
        var usedPath = path.GetRange(0, steps);
        var finalPos = usedPath[^1];

        room.RemoveUnitAtGridPosition(startPos, unit);
        room.AddUnitAtGridPosition(finalPos, unit);

        if (playerStats != null && IsInCombatRoom())
            playerStats.SpendStamina(steps);

        // Build waypoints WITH visual offset for the legacy animator.
        Vector2 visualOff = unit.GetVisualOffset();
        var waypoints = new List<Vector3>();
        foreach (var gp in usedPath)
        {
            var wp = room.GetWorldPosition(gp);
            waypoints.Add(new Vector3(
                wp.x + visualOff.x,
                wp.y + visualOff.y,
                transform.position.z));
        }

        SetFacingToward(startPos, usedPath[0]);
        unitAnimator?.SetMoving(true);
        isActive = true;
        InvalidateCache();
        StartCoroutine(MoveAlongLocalPath(waypoints, usedPath, finalPos, room, onComplete));
    }



    private IEnumerator MoveAlongWorldPath(
        List<UnifiedPathfinder.WorldStep> path, Action onComplete)
    {
        isActive = true;
        unitAnimator?.SetMoving(true);

        Vector2 visualOff = unit.GetVisualOffset();
        int     stamSpent = 0;

        for (int i = 0; i < path.Count; i++)
        {
            var step     = path[i];
            var stepGrid = step.OwnerGrid ?? unit.GetCurrentRoomGrid();
            var stepGP   = stepGrid?.GetGridPosition(step.WorldPos) ?? unit.GetGridPosition();

            // Facing.
            if (i == 0)
                SetFacingToward(unit.GetGridPosition(), stepGP);
            else
            {
                var prev     = path[i - 1];
                var prevGrid = prev.OwnerGrid ?? unit.GetCurrentRoomGrid();
                SetFacingToward(prevGrid.GetGridPosition(prev.WorldPos), stepGP);
            }

            // Grid registration when crossing into a different grid.
            if (stepGrid != null && stepGrid != unit.GetCurrentRoomGrid())
            {
                var oldGrid = unit.GetCurrentRoomGrid();
                var oldGP   = unit.GetGridPosition();
                oldGrid?.RemoveUnitAtGridPosition(oldGP, unit);

                // Use PlaceInRoom so the unit's internal state is fully
                // consistent, but suppress the transform snap — we are
                // controlling the visual position ourselves in this coroutine.
                unit.IsSyncingFromNetwork = true;
                unit.PlaceInRoom(stepGrid, stepGP);
                unit.IsSyncingFromNetwork = false;

                stepGrid.AddUnitAtGridPosition(stepGP, unit);
            }

            // Animate the SPRITE to cell centre + visual offset.
            var visualTarget = new Vector3(
                step.WorldPos.x + visualOff.x,
                step.WorldPos.y + visualOff.y,
                transform.position.z);

            while (Vector2.Distance(transform.position, visualTarget) > 0.01f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position, visualTarget, moveSpeed * Time.deltaTime);
                yield return null;
            }
            transform.position = visualTarget;
            stamSpent++;
        }

        // Final placement — uses PlaceInRoom normally (it will set transform
        // to rawWorld + visualOffset, which matches where we just animated to).
        var finalStep = path[^1];
        var finalGrid = finalStep.OwnerGrid ?? unit.GetCurrentRoomGrid();
        var finalGP   = finalGrid?.GetGridPosition(finalStep.WorldPos) ?? unit.GetGridPosition();

        if (playerStats != null && IsInCombatRoom())
            playerStats.SpendStamina(stamSpent);

        unitAnimator?.SetMoving(false);
        isActive = false;

        if (!GameManager.IsMultiplayer)
        {
            // PlaceInRoom snaps transform to rawWorld + visualOffset.
            // That matches our last animated position, so there's no visible pop.
            unit.PlaceInRoom(finalGrid, finalGP);
        }
        else
        {
            var bridge = unit.GetComponent<NetworkedPlayerBridge>();
            if (bridge != null && bridge.IsOwner)
                bridge.SyncGridPosition(finalGrid, finalGP);
        }

        onComplete?.Invoke();
    }

    // ── Coroutine: local-path movement (legacy, unchanged logic) ──────────

    private IEnumerator MoveAlongLocalPath(
        List<Vector3> waypoints, List<GridPosition> gridPath,
        GridPosition finalGP, RoomGrid startingGrid, Action onComplete)
    {
        for (int i = 0; i < waypoints.Count; i++)
        {
            if (i > 0) SetFacingToward(gridPath[i - 1], gridPath[i]);
            var target = new Vector3(waypoints[i].x, waypoints[i].y, transform.position.z);
            while (Vector2.Distance(transform.position, target) > 0.01f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position, target, moveSpeed * Time.deltaTime);
                yield return null;
            }
            transform.position = target;
        }

        unitAnimator?.SetMoving(false);
        isActive = false;

        if (unit.GetCurrentRoomGrid() == startingGrid)
        {
            if (GameManager.IsMultiplayer)
            {
                var bridge = unit.GetComponent<NetworkedPlayerBridge>();
                if (bridge != null && bridge.IsOwner)
                    bridge.SyncGridPosition(startingGrid, finalGP);
            }
            else
                unit.PlaceInRoomNoMove(startingGrid, finalGP);
        }

        onComplete?.Invoke();
    }

    // ── Reachable-cell cache (BFS on UnifiedWorldGrid) ─────────────────────

    private void RebuildReachableCache()
    {
        cachedReachableWorld = new List<Vector3>();
        cachedReachableGP    = new List<GridPosition>();
        cacheDirty           = false;

        var unified = UnifiedWorldGrid.Instance;

        if (unified == null || unified.AllCells.Count == 0)
        {
            LocalBFS();
            return;
        }

        var startRoom = unit.GetCurrentRoomGrid();
        if (startRoom == null) return;

        Vector3    unitWorld = startRoom.GetWorldPosition(unit.GetGridPosition());
        Vector3Int startKey  = UnifiedWorldGrid.WorldKey(unitWorld);

        if (!unified.AllCells.ContainsKey(startKey))
        {
            Vector3 tpos = unit.transform.position;
            Vector2 vo = unit.GetVisualOffset();
            unitWorld = new Vector3(tpos.x - vo.x, tpos.y - vo.y, 0f);
            startKey  = UnifiedWorldGrid.WorldKey(unitWorld);

            if (!unified.AllCells.ContainsKey(startKey))
            {
                // Nearest-key fallback.
                float best = float.MaxValue; Vector3Int bestKey = default;
                foreach (var k in unified.AllCells.Keys)
                {
                    float d = Vector3Int.Distance(k, startKey);
                    if (d < best) { best = d; bestKey = k; }
                }
                if (best > 4f) { LocalBFS(); return; }
                startKey = bestKey;
            }
        }

        int moveRange = MoveDistance;
        var visited   = new Dictionary<Vector3Int, int> { [startKey] = 0 };
        var queue     = new Queue<(Vector3Int key, int cost)>();
        queue.Enqueue((startKey, 0));

        while (queue.Count > 0)
        {
            var (current, cost) = queue.Dequeue();
            if (cost >= moveRange) continue;
            foreach (var nKey in unified.GetWalkableNeighbours(current, ignoreOccupancy: false))
            {
                if (visited.ContainsKey(nKey)) continue;
                visited[nKey] = cost + 1;
                queue.Enqueue((nKey, cost + 1));
            }
        }

        foreach (var kvp in visited)
        {
            if (kvp.Key == startKey) continue;
            var cellData = unified.GetCellByKey(kvp.Key);
            if (cellData == null || cellData.OwnerGrid == null) continue;
            cachedReachableWorld.Add(cellData.WorldCentre);
            cachedReachableGP.Add(cellData.OwnerGrid.GetGridPosition(cellData.WorldCentre));
        }

        Debug.Log($"[MoveAction] BFS: {cachedReachableWorld.Count} reachable cells.");
    }

    private void LocalBFS()
    {
        var room = unit.GetCurrentRoomGrid();
        if (room == null) return;
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
            if (path != null && path.Count > 0 && path.Count <= dist)
            {
                cachedReachableGP.Add(test);
                cachedReachableWorld.Add(room.GetWorldPosition(test));
            }
        }
    }

    // ── Public query API ───────────────────────────────────────────────────

    public List<Vector3> GetValidActionWorldPositions()
    {
        if (cacheDirty || cachedReachableWorld == null) RebuildReachableCache();
        return cachedReachableWorld;
    }

    public List<GridPosition> GetValidActionGridPositionList()
    {
        if (cacheDirty || cachedReachableGP == null) RebuildReachableCache();
        return cachedReachableGP;
    }

    public bool IsValidTarget(GridPosition gp)
    {
        if (cacheDirty || cachedReachableGP == null) RebuildReachableCache();
        return cachedReachableGP.Contains(gp);
    }

    public bool isValidActionGridPosition(GridPosition gp) => IsValidTarget(gp);

    public int GetMoveCost(GridPosition target)
    {
        var room = unit?.GetCurrentRoomGrid();
        if (room == null) return -1;
        var path = new Pathfinder(room).FindPath(unit.GetGridPosition(), target);
        return (path == null || path.Count == 0) ? -1 : path.Count;
    }

    // ── Facing ────────────────────────────────────────────────────────────

    public void SetFacingToward(GridPosition from, GridPosition to)
    {
        int dx = to.x - from.x;
        int dy = to.y - from.y;
        unitAnimator?.SetFacing(new Vector2Int(
            dx == 0 ? 0 : (int)Mathf.Sign(dx),
            dy == 0 ? 0 : (int)Mathf.Sign(dy)));
    }

    public void SetFacingToward(GridPosition next) =>
        SetFacingToward(unit.GetGridPosition(), next);

    // ── Helpers ────────────────────────────────────────────────────────────


    private Vector3 GetUnitCellCentreWorld()
    {
        var room = unit.GetCurrentRoomGrid();
        if (room != null) return room.GetWorldPosition(unit.GetGridPosition());

        Vector2 vo = unit.GetVisualOffset();
        return new Vector3(
            transform.position.x - vo.x,
            transform.position.y - vo.y,
            0f);
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PartyFollowManager : MonoBehaviour
{
    [SerializeField] private float followerMoveSpeed = 10f;

    private void OnEnable()
    {
        StartCoroutine(SubscribeWhenReady());
    }

    private IEnumerator SubscribeWhenReady()
    {
        while (PartyManager.Instance == null)
            yield return null;

        PartyManager.Instance.OnPartyChanged += HookParty;
        PartyManager.Instance.OnSelectedUnitChanged += HandleSelectedUnitChanged;
        yield return null;
        HookParty();

        yield return null;
        HookParty();
    }

    private void OnDisable()
    {
        if (PartyManager.Instance != null)
            PartyManager.Instance.OnPartyChanged -= HookParty;
        PartyManager.Instance.OnSelectedUnitChanged -= HandleSelectedUnitChanged;
        UnhookAll();
    }
    private void HandleSelectedUnitChanged(Unit unit)
    {
        ClearFollowerQueue();
    }
    private void HookParty()
    {
        UnhookAll();

        foreach (Unit unit in PartyManager.Instance.PartyUnits)
        {
            MoveAction move = unit.GetMoveAction();
            if (move != null)
                move.OnWorldStepCompleted += HandleWorldStepCompleted;
        }
    }

    private void UnhookAll()
    {
        if (PartyManager.Instance == null)
            return;

        foreach (Unit unit in PartyManager.Instance.PartyUnits)
        {
            if (unit == null) continue;

            MoveAction move = unit.GetMoveAction();
            if (move != null)
                move.OnWorldStepCompleted -= HandleWorldStepCompleted;
        }
    }

    private IEnumerator MoveFollowerToWorldCell(Unit follower, Vector3 cellWorld)
    {
        Vector2 visualOffset = follower.GetVisualOffset();

        Vector3 target = new Vector3(
            cellWorld.x + visualOffset.x,
            cellWorld.y + visualOffset.y,
            follower.transform.position.z
        );

        while (Vector2.Distance(follower.transform.position, target) > 0.01f)
        {
            follower.transform.position = Vector3.MoveTowards(
                follower.transform.position,
                target,
                followerMoveSpeed * Time.deltaTime
            );

            yield return null;
        }

        follower.transform.position = target;

        SyncFollowerGridToWorld(follower, cellWorld);
    }

    private void SyncFollowerGridToWorld(Unit follower, Vector3 cellWorld)
    {
        RoomGrid grid = UnifiedWorldGrid.Instance != null
            ? UnifiedWorldGrid.Instance.GetOwnerAt(cellWorld)
            : follower.GetCurrentRoomGrid();

        if (grid == null)
            return;

        GridPosition gp = grid.GetGridPosition(cellWorld);
        follower.PlaceInRoomNoMove(grid, gp);
    }

    private bool IsInCombatRoom(Unit unit)
    {
        RoomGrid room = unit.GetCurrentRoomGrid();

        if (room == null || EnemyManager.Instance == null)
            return false;

        return EnemyManager.Instance.GetEnemiesInRoom(room).Count > 0;
    }

    public void SnapPartyNearLeader()
    {
        if (PartyManager.Instance == null)
            return;

        Unit leader = PartyManager.Instance.SelectedUnit;
        if (leader == null)
            return;

        RoomGrid room = leader.GetCurrentRoomGrid();
        if (room == null)
            return;

        GridPosition leaderPos = leader.GetGridPosition();

        int placed = 0;

        foreach (Unit unit in PartyManager.Instance.PartyUnits)
        {
            if (unit == null || unit == leader)
                continue;

            GridPosition spot = FindNearbyOpenTile(room, leaderPos, placed + 1);

            unit.PlaceInRoom(room, spot);
            placed++;
        }
    }

    private GridPosition FindNearbyOpenTile(RoomGrid room, GridPosition center, int preferredDistance)
    {
        for (int radius = 1; radius <= 4; radius++)
        {
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) != radius)
                        continue;

                    GridPosition test = new GridPosition(center.x + dx, center.y + dy);

                    if (!room.IsValidGridPosition(test))
                        continue;

                    if (!room.IsWalkableIgnoreOccupancy(test))
                        continue;

                    if (room.HasAnyUnitOnGridPosition(test))
                        continue;

                    return test;
                }
        }

        return center;
    }

    private readonly Queue<Vector3> followerStepQueue = new();
    private bool followerIsMoving;

    private void HandleWorldStepCompleted(Unit leader, Vector3 leaderStepWorld)
    {
        if (GameManager.IsMultiplayer)
            return;

        if (leader == null)
            return;

        if (PartyManager.Instance == null)
            return;

        if (leader != PartyManager.Instance.SelectedUnit)
            return;

        if (IsInCombatRoom(leader))
            return;

        followerStepQueue.Enqueue(leaderStepWorld);

        if (!followerIsMoving)
            StartCoroutine(ProcessFollowerStepQueue(leader));
    }

    private IEnumerator ProcessFollowerStepQueue(Unit leader)
    {
        followerIsMoving = true;

        while (followerStepQueue.Count > 1)
        {
            Vector3 nextCell = followerStepQueue.Dequeue();

            foreach (Unit follower in PartyManager.Instance.PartyUnits)
            {
                if (follower == null || follower == leader)
                    continue;

                yield return MoveFollowerToWorldCell(follower, nextCell);
            }
        }

        followerIsMoving = false;
    }

    public void SnapFollowersToEntrance(RoomGrid room, LevelGenerator.Direction entranceDir)
    {
        ClearFollowerQueue();
        if (PartyManager.Instance == null)
            return;

        Unit leader = PartyManager.Instance.SelectedUnit;

        if (leader == null || room == null)
            return;

        GridPosition leaderPos = leader.GetGridPosition();

        int placed = 0;

        foreach (Unit follower in PartyManager.Instance.PartyUnits)
        {
            if (follower == null || follower == leader)
                continue;

            GridPosition spot = FindEntranceSideTile(room, leaderPos, entranceDir, placed);

            follower.PlaceInRoom(room, spot);
            placed++;
        }
    }

    private GridPosition FindEntranceSideTile(
        RoomGrid room,
        GridPosition leaderPos,
        LevelGenerator.Direction entranceDir,
        int followerIndex)
    {
        List<GridPosition> candidates = new();

        switch (entranceDir)
        {
            case LevelGenerator.Direction.North:
            case LevelGenerator.Direction.South:
                // Door is vertical movement, so line followers horizontally beside leader.
                candidates.Add(new GridPosition(leaderPos.x - 1 - followerIndex, leaderPos.y));
                candidates.Add(new GridPosition(leaderPos.x + 1 + followerIndex, leaderPos.y));
                candidates.Add(new GridPosition(leaderPos.x, leaderPos.y - 1));
                candidates.Add(new GridPosition(leaderPos.x, leaderPos.y + 1));
                break;

            case LevelGenerator.Direction.East:
            case LevelGenerator.Direction.West:
                // Door is horizontal movement, so line followers vertically beside leader.
                candidates.Add(new GridPosition(leaderPos.x, leaderPos.y - 1 - followerIndex));
                candidates.Add(new GridPosition(leaderPos.x, leaderPos.y + 1 + followerIndex));
                candidates.Add(new GridPosition(leaderPos.x - 1, leaderPos.y));
                candidates.Add(new GridPosition(leaderPos.x + 1, leaderPos.y));
                break;
        }

        foreach (GridPosition test in candidates)
        {
            if (!room.IsValidGridPosition(test))
                continue;

            if (!room.IsWalkableIgnoreOccupancy(test))
                continue;

            if (room.HasAnyUnitOnGridPosition(test))
                continue;

            return test;
        }

        return FindNearbyOpenTile(room, leaderPos, followerIndex + 1);
    }

    private void ClearFollowerQueue()
    {
        followerStepQueue.Clear();
        followerIsMoving = false;
    }
}
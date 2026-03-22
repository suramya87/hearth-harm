using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Melee enemy AI. Moves toward the nearest player and attacks when in range.
/// </summary>
[RequireComponent(typeof(EnemyUnit))]
public class EnemyAI : MonoBehaviour
{
    [SerializeField, Min(0f)] private float stepDelay = 0.2f;

    private EnemyUnit unit;
    private void Awake() => unit = GetComponent<EnemyUnit>();

    public void TakeTurn(Action onComplete)
    {
        if (!unit.CanActThisTurn() || unit.IsDead) { onComplete?.Invoke(); return; }

        var target = FindPlayerInRoom();
        if (target == null) { onComplete?.Invoke(); return; }

        StartCoroutine(TurnRoutine(target, onComplete));
    }

    private IEnumerator TurnRoutine(Unit player, Action onComplete)
    {
        var stats     = unit.Stats;
        var room      = unit.CurrentRoomGrid;
        if (stats == null || room == null) { onComplete?.Invoke(); yield break; }

        var myPos     = unit.GridPosition;
        var playerPos = player.GetGridPosition();
        int dist      = myPos.ManhattanDistance(playerPos);

        // Move phase
        if (dist > stats.attackRange)
        {
            var path  = new Pathfinder(room).FindPathToRange(myPos, playerPos, stats.attackRange);
            int steps = Mathf.Min(path.Count, stats.moveRange);
            for (int i = 0; i < steps; i++)
            {
                if (unit.IsDead) { onComplete?.Invoke(); yield break; }
                if (IsTileOccupied(path[i], room)) break;
                unit.MoveToPosition(path[i]);
                yield return new WaitForSeconds(stepDelay);
            }
            myPos = unit.GridPosition;
            dist  = myPos.ManhattanDistance(playerPos);
        }

        yield return new WaitForSeconds(stepDelay);

        // Attack phase
        if (!unit.IsDead && dist <= stats.attackRange)
        {
            PerformAttack(player);
            yield return new WaitForSeconds(stepDelay);
        }

        onComplete?.Invoke();
    }

    private void PerformAttack(Unit player)
    {
        var stats = unit.Stats;
        if (stats.attackData == null) { Debug.LogWarning($"[EnemyAI] {stats?.enemyName} has no attackData."); return; }

        var health = player.GetComponent<HealthComponent>();
        if (health == null) return;

        int dmg = stats.attackData.CalculateDamage();
        AttackSpritePopup.Show(stats.attackData, player.transform.position);

        if (stats.attackData.attackPattern != null)
        {
            var facing   = Facing(unit.GridPosition, player.GetGridPosition());
            var hitTiles = stats.attackData.attackPattern.GetAffectedPositions(unit.GridPosition, facing);
            bool hit = false;
            foreach (var t in hitTiles) if (t == player.GetGridPosition()) { health.TakeDamage(dmg); hit = true; break; }
            if (!hit) health.TakeDamage(dmg);
        }
        else health.TakeDamage(dmg);

        Debug.Log($"[EnemyAI] {stats.enemyName} hit player for {dmg}.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private bool IsTileOccupied(GridPosition pos, RoomGrid room)
    {
        var enemies = EnemyManager.Instance?.GetEnemiesInRoom(room);
        if (enemies != null)
            foreach (var e in enemies)
            { if (e == unit || e == null || e.IsDead) continue; if (e.GridPosition == pos) return true; }

        var pt = PlayerTarget.Instance;
        if (pt != null)
        {
            var pu = pt.GetUnit();
            var ph = pu?.GetComponent<HealthComponent>();
            if (ph != null && !ph.IsDead && pu.GetGridPosition() == pos) return true;
        }
        return false;
    }

    private Unit FindPlayerInRoom()
    {
        var pt = PlayerTarget.Instance;
        if (pt == null) return null;
        return pt.IsInRoom(unit.CurrentRoomGrid) ? pt.GetUnit() : null;
    }

    private static Vector2Int Facing(GridPosition from, GridPosition to)
    {
        int dx = to.x - from.x, dy = to.y - from.y;
        return Mathf.Abs(dy) > Mathf.Abs(dx)
            ? (dy >= 0 ? new(0, 1) : new(0, -1))
            : (dx >= 0 ? new(1, 0) : new(-1, 0));
    }
}
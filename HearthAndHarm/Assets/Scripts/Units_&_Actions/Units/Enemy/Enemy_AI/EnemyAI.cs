using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
        var stats = unit.Stats;
        var room  = unit.CurrentRoomGrid;
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
                unit.MoveToPosition(path[i]); // auto-broadcasts in MP via EnemyUnit
                yield return new WaitForSeconds(stepDelay);
            }
            myPos = unit.GridPosition;
            dist  = myPos.ManhattanDistance(playerPos);
        }

        yield return new WaitForSeconds(stepDelay);

        // Attack phase
        if (!unit.IsDead && dist <= stats.attackRange)
        {
            // Re-find player in case they moved during our move phase
            player = FindPlayerInRoom() ?? player;
            PerformAttack(player);
            yield return new WaitForSeconds(stepDelay);
        }

        onComplete?.Invoke();
    }

    private void PerformAttack(Unit player)
    {
        var stats = unit.Stats;
        if (stats.attackData == null)
        {
            Debug.LogWarning($"[EnemyAI] {stats?.enemyName} has no attackData.");
            return;
        }

        int dmg = stats.attackData.CalculateDamage();
        AttackSpritePopup.Show(stats.attackData, player.transform.position);

        if (stats.attackData.attackPattern != null)
        {
            var facing   = Facing(unit.GridPosition, player.GetGridPosition());
            var hitTiles = stats.attackData.attackPattern.GetAffectedPositions(unit.GridPosition, facing);
            bool hit     = false;
            foreach (var t in hitTiles)
            {
                if (t != player.GetGridPosition()) continue;
                DealDamage(player.gameObject, dmg);
                hit = true;
                break;
            }
            if (!hit) DealDamage(player.gameObject, dmg);
        }
        else
        {
            DealDamage(player.gameObject, dmg);
        }

        Debug.Log($"[EnemyAI] {stats.enemyName} hit player for {dmg}.");
    }

    // ── Damage routing ─────────────────────────────────────────────────────

    private static void DealDamage(GameObject target, int dmg)
    {
        if (GameManager.IsMultiplayer)
            NetworkedHealthBridge.TakeDamage(target, dmg);
        else
            target.GetComponent<HealthComponent>()?.TakeDamage(dmg);
    }

    // ── Player targeting ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the nearest living player in this enemy's room.
    /// In MP uses NetworkedEnemyBridge to scan all players.
    /// In SP uses PlayerTarget for single-player compatibility.
    /// </summary>
    private Unit FindPlayerInRoom()
    {
        return NetworkedEnemyBridge.FindNearestPlayerInRoom(unit.CurrentRoomGrid, unit.GridPosition);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private bool IsTileOccupied(GridPosition pos, RoomGrid room)
    {
        var enemies = EnemyManager.Instance?.GetEnemiesInRoom(room);
        if (enemies != null)
            foreach (var e in enemies)
                if (e != unit && e != null && !e.IsDead && e.GridPosition == pos) return true;

        // Check all players in the room
        if (GameManager.IsMultiplayer)
        {
            foreach (var bridge in FindObjectsByType<NetworkedPlayerBridge>(FindObjectsSortMode.None))
            {
                var u  = bridge.GetComponent<Unit>();
                var hp = u?.GetComponent<HealthComponent>();
                if (hp != null && !hp.IsDead && u.GetGridPosition() == pos) return true;
            }
        }
        else
        {
            var pt = PlayerTarget.Instance;
            if (pt != null)
            {
                var pu = pt.GetUnit();
                var ph = pu?.GetComponent<HealthComponent>();
                if (ph != null && !ph.IsDead && pu.GetGridPosition() == pos) return true;
            }
        }

        return false;
    }

    private static Vector2Int Facing(GridPosition from, GridPosition to)
    {
        int dx = to.x - from.x, dy = to.y - from.y;
        return Mathf.Abs(dy) > Mathf.Abs(dx)
            ? (dy >= 0 ? new(0, 1) : new(0, -1))
            : (dx >= 0 ? new(1, 0) : new(-1, 0));
    }
}
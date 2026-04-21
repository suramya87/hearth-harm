using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ranged enemy AI. In multiplayer runs server-only.
/// Damage is routed through NetworkedHealthBridge so clients see HP changes.
/// </summary>
[RequireComponent(typeof(EnemyUnit))]
public class RangedEnemyAI : MonoBehaviour
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

        // Kite: retreat if too close
        if (stats.kiteEnabled && dist < stats.kiteRange)
        {
            var flee = FindFleePos(myPos, playerPos, room, stats.moveRange);
            if (flee.HasValue)
            {
                unit.MoveToPosition(flee.Value);   // auto-broadcasts in MP
                yield return new WaitForSeconds(stepDelay);
                myPos = unit.GridPosition;
                dist  = myPos.ManhattanDistance(playerPos);
            }
        }
        else if (dist > stats.attackRange || !HasLoS(myPos, playerPos, room))
        {
            var path  = new Pathfinder(room).FindPathToRange(myPos, playerPos, stats.attackRange);
            int steps = Mathf.Min(path.Count, stats.moveRange);
            for (int i = 0; i < steps; i++)
            {
                if (unit.IsDead) { onComplete?.Invoke(); yield break; }
                var next = path[i];
                int nd   = next.ManhattanDistance(playerPos);
                if (nd <= stats.attackRange && HasLoS(next, playerPos, room)) break;
                if (IsTileOccupied(next, room)) break;
                unit.MoveToPosition(next);         // auto-broadcasts in MP
                yield return new WaitForSeconds(stepDelay);
            }
            myPos = unit.GridPosition;
            dist  = myPos.ManhattanDistance(playerPos);
        }

        yield return new WaitForSeconds(stepDelay);

        if (!unit.IsDead && dist <= stats.attackRange && HasLoS(myPos, playerPos, room))
        {
            PerformAttack(player);
            yield return new WaitForSeconds(stepDelay);
        }

        onComplete?.Invoke();
    }

    private void PerformAttack(Unit player)
    {
        var stats = unit.Stats;
        if (stats.attackData == null) return;

        int dmg = stats.attackData.CalculateDamage();
        AttackSpritePopup.Show(stats.attackData, player.transform.position, new Vector3(0f, 0.5f, 0f));

        if (stats.attackData.attackPattern != null)
        {
            var facing = Facing(unit.GridPosition, player.GetGridPosition());
            var hits   = stats.attackData.attackPattern.GetAffectedPositions(unit.GridPosition, facing);
            bool hit   = false;
            foreach (var t in hits)
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

        Debug.Log($"[RangedEnemyAI] {stats.enemyName} shot player for {dmg}.");
    }

    // ── Damage routing ─────────────────────────────────────────────────────

    private static void DealDamage(GameObject target, int dmg)
    {
        if (GameManager.IsMultiplayer)
            NetworkedHealthBridge.TakeDamage(target, dmg);
        else
            target.GetComponent<HealthComponent>()?.TakeDamage(dmg);
    }

    // ── LoS (Bresenham) ────────────────────────────────────────────────────

    private bool HasLoS(GridPosition from, GridPosition to, RoomGrid room)
    {
        int x0=from.x, y0=from.y, x1=to.x, y1=to.y;
        int dx=Mathf.Abs(x1-x0), dy=Mathf.Abs(y1-y0);
        int sx=x0<x1?1:-1, sy=y0<y1?1:-1, err=dx-dy;
        while (true)
        {
            bool ep = (x0==from.x&&y0==from.y)||(x0==to.x&&y0==to.y);
            if (!ep && room.IsWall(new GridPosition(x0,y0))) return false;
            if (x0==x1&&y0==y1) break;
            int e2=2*err;
            if (e2>-dy){err-=dy;x0+=sx;}
            if (e2< dx){err+=dx;y0+=sy;}
        }
        return true;
    }

    private GridPosition? FindFleePos(GridPosition my, GridPosition player,
                                      RoomGrid room, int moveRange)
    {
        GridPosition? best = null;
        int bestD = my.ManhattanDistance(player);
        for (int dx=-moveRange;dx<=moveRange;dx++)
        for (int dy=-moveRange;dy<=moveRange;dy++)
        {
            if (Mathf.Abs(dx)+Mathf.Abs(dy) > moveRange) continue;
            var c = new GridPosition(my.x+dx, my.y+dy);
            if (!room.IsWalkableIgnoreOccupancy(c)) continue;
            if (IsTileOccupied(c, room)) continue;
            int d = c.ManhattanDistance(player);
            if (d > bestD) { bestD=d; best=c; }
        }
        return best;
    }

    private bool IsTileOccupied(GridPosition pos, RoomGrid room)
    {
        var enemies = EnemyManager.Instance?.GetEnemiesInRoom(room);
        if (enemies != null)
            foreach (var e in enemies)
            { if (e==unit||e==null||e.IsDead) continue; if (e.GridPosition==pos) return true; }
        var pt = PlayerTarget.Instance;
        if (pt != null)
        { var pu=pt.GetUnit(); var ph=pu?.GetComponent<HealthComponent>();
          if (ph!=null&&!ph.IsDead&&pu.GetGridPosition()==pos) return true; }
        return false;
    }

    private Unit FindPlayerInRoom()
    {
        var pt = PlayerTarget.Instance;
        return pt?.IsInRoom(unit.CurrentRoomGrid) == true ? pt.GetUnit() : null;
    }

    private static Vector2Int Facing(GridPosition from, GridPosition to)
    {
        int dx=to.x-from.x, dy=to.y-from.y;
        return Mathf.Abs(dy)>Mathf.Abs(dx)
            ? (dy>=0 ? new(0,1) : new(0,-1))
            : (dx>=0 ? new(1,0) : new(-1,0));
    }
}
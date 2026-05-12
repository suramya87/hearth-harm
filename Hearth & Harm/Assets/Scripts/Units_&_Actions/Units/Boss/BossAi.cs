// ─────────────────────────────────────────────────────────────────────────────
// BossAI.cs
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(BossUnit))]
[RequireComponent(typeof(BossPhaseController))]
public class BossAI : MonoBehaviour
{
    [SerializeField, Min(0f)] private float stepDelay      = 0.25f;
    [SerializeField, Min(0f)] private float actionDelay    = 0.4f;
    [SerializeField, Min(1)]  private int   invisCooldown  = 3;
    [SerializeField, Min(1)]  private int   rangedCooldown = 1;

    private BossUnit            boss;
    private BossPhaseController phase;

    private int turnCount          = 0;
    private int invisCooldownLeft  = 0;
    private int rangedCooldownLeft = 0;

    private void Awake()
    {
        boss  = GetComponent<BossUnit>();
        phase = GetComponent<BossPhaseController>();

        // Initialize here so phase system is ready before any damage lands
        if (boss.Stats != null)
            phase.Initialize(boss.Stats);
    }

    private void Start()
    {
        if (boss.Stats == null)
            Debug.LogError("[BossAI] BossStats is not assigned!", this);
    }

    // ── Entry point called by EnemyManager ────────────────────────────────

    public void TakeTurn(Action onComplete)
    {
        if (!boss.CanActThisTurn() || boss.IsDead) { onComplete?.Invoke(); return; }

        var target = FindPlayer();
        if (target == null) { onComplete?.Invoke(); return; }

        StartCoroutine(TurnRoutine(target, onComplete));
    }

    // ── Main turn coroutine ────────────────────────────────────────────────

    private IEnumerator TurnRoutine(Unit player, Action onComplete)
    {
        turnCount++;

        // Snapshot stats for this turn — intentional, won't reflect mid-turn stat swaps
        var stats = boss.Stats;
        var room  = boss.CurrentRoomGrid;

        if (stats == null || room == null) { onComplete?.Invoke(); yield break; }

        var myCenter  = boss.CenterGridPosition();
        var playerPos = player.GetGridPosition();
        int dist      = myCenter.ManhattanDistance(playerPos);

        // ── 1. MOVE PHASE ──────────────────────────────────────────────────
        // Invisible: always flee
        // Visible + kite enabled + too close: flee
        // Visible + too far: chase

        if (phase.IsInvisible)
        {
            var flee = FindFleePosition(myCenter, playerPos, room, stats.moveRange);
            if (flee.HasValue)
            {
                boss.MoveToPosition(flee.Value);
                yield return new WaitForSeconds(stepDelay);
                myCenter = boss.CenterGridPosition();
                dist     = myCenter.ManhattanDistance(playerPos);
            }
        }
        else if (stats.kiteEnabled && dist < stats.kiteRange)
        {
            var flee = FindFleePosition(myCenter, playerPos, room, stats.moveRange);
            if (flee.HasValue)
            {
                boss.MoveToPosition(flee.Value);
                yield return new WaitForSeconds(stepDelay);
                myCenter = boss.CenterGridPosition();
                dist     = myCenter.ManhattanDistance(playerPos);
            }
        }
        else if (dist > stats.attackRange)
        {
            var path  = new Pathfinder(room).FindPathToRange(myCenter, playerPos, stats.attackRange);
            int steps = Mathf.Min(path.Count, stats.moveRange);

            for (int i = 0; i < steps; i++)
            {
                if (boss.IsDead) { onComplete?.Invoke(); yield break; }
                if (!CanMoveTo(path[i], room)) break;
                boss.MoveToPosition(path[i]);
                yield return new WaitForSeconds(stepDelay);
            }

            myCenter = boss.CenterGridPosition();
            dist     = myCenter.ManhattanDistance(playerPos);
        }

        yield return new WaitForSeconds(actionDelay);
        if (boss.IsDead) { onComplete?.Invoke(); yield break; }

        // ── 2. ABILITY PHASE ───────────────────────────────────────────────
        // Invisible: ranged attack only
        // Visible: invis trigger → ranged → cleave
        // Going invisible and attacking on the same turn is intentionally blocked

        if (phase.IsInvisible)
        {
            if (dist <= stats.attackRange && rangedCooldownLeft <= 0)
            {
                player = FindPlayer() ?? player;
                PerformRangedAttack(player, stats);
                rangedCooldownLeft = rangedCooldown;
                yield return new WaitForSeconds(actionDelay);
            }
        }
        else
        {
            bool wentInvis = false;

            // Try to go invisible in Enraged phase
            if (phase.CurrentPhase == BossPhaseController.BossPhase.Enraged
                && !phase.IsInvisible
                && invisCooldownLeft <= 0)
            {
                phase.SetInvisible(true);
                invisCooldownLeft = invisCooldown;
                wentInvis = true;
                yield return new WaitForSeconds(actionDelay);
            }

            // Ranged attack — skipped if we just went invisible this turn
            if (!wentInvis && !boss.IsDead && dist <= stats.attackRange && rangedCooldownLeft <= 0)
            {
                player = FindPlayer() ?? player;
                PerformRangedAttack(player, stats);
                rangedCooldownLeft = rangedCooldown;
                yield return new WaitForSeconds(actionDelay);
            }

            // Cleave if player is adjacent and visible
            if (!boss.IsDead && dist <= 1 && stats.cleaveAttackData != null)
            {
                player = FindPlayer() ?? player;
                PerformCleave(player, stats);
                yield return new WaitForSeconds(actionDelay);
            }
        }

        // ── 3. END OF TURN ─────────────────────────────────────────────────

        if (invisCooldownLeft  > 0) invisCooldownLeft--;
        if (rangedCooldownLeft > 0) rangedCooldownLeft--;
        phase.TickInvisibility();

        onComplete?.Invoke();
    }

    // ── Attacks ────────────────────────────────────────────────────────────

    private void PerformRangedAttack(Unit player, BossStats stats)
    {
        if (stats.rangedAttackData == null)
        {
            Debug.LogWarning($"[BossAI] {stats.bossName} has no rangedAttackData.");
            return;
        }

        int dmg = stats.rangedAttackData.CalculateDamage();
        AttackSpritePopup.Show(stats.rangedAttackData, player.transform.position);
        DealDamageToPlayer(player, dmg);
        Debug.Log($"[BossAI] {stats.bossName} ranged attack → {dmg} dmg");
    }

    private void PerformCleave(Unit player, BossStats stats)
    {
        if (stats.cleaveAttackData == null) return;

        int dmg = stats.cleaveAttackData.CalculateDamage();
        AttackSpritePopup.Show(stats.cleaveAttackData, player.transform.position);
        DealDamageToPlayer(player, dmg);
        Debug.Log($"[BossAI] {stats.bossName} cleave → {dmg} dmg");
    }

    // ── Damage routing ─────────────────────────────────────────────────────

    private static void DealDamageToPlayer(Unit player, int dmg)
    {
        if (GameManager.IsMultiplayer)
            NetworkedHealthBridge.TakeDamage(player.gameObject, dmg);
        else
            player.GetComponent<HealthComponent>()?.TakeDamage(dmg);
    }

    // ── Movement helpers ───────────────────────────────────────────────────

    private bool CanMoveTo(GridPosition origin, RoomGrid room)
    {
        int size = boss.OccupiedCells.Count > 0
            ? Mathf.RoundToInt(Mathf.Sqrt(boss.OccupiedCells.Count))
            : 1;

        for (int dx = 0; dx < size; dx++)
        for (int dy = 0; dy < size; dy++)
        {
            var cell = new GridPosition(origin.x + dx, origin.y + dy);
            if (!room.IsWalkableIgnoreOccupancy(cell)) return false;
            if (boss.OccupiedCells.Contains(cell))     continue;
            if (room.HasAnyEnemyOnGridPosition(cell))  return false;
            if (room.HasAnyUnitOnGridPosition(cell))   return false;
        }
        return true;
    }

    private GridPosition? FindFleePosition(GridPosition center, GridPosition player,
                                           RoomGrid room, int moveRange)
    {
        GridPosition? best  = null;
        int           bestD = center.ManhattanDistance(player);

        for (int dx = -moveRange; dx <= moveRange; dx++)
        for (int dy = -moveRange; dy <= moveRange; dy++)
        {
            if (Mathf.Abs(dx) + Mathf.Abs(dy) > moveRange) continue;
            var candidate = new GridPosition(center.x + dx, center.y + dy);
            if (!CanMoveTo(candidate, room)) continue;
            int d = candidate.ManhattanDistance(player);
            if (d > bestD) { bestD = d; best = candidate; }
        }
        return best;
    }

    // ── Player targeting ───────────────────────────────────────────────────

    private Unit FindPlayer()
    {
        return NetworkedEnemyBridge.FindNearestPlayerInRoom(
            boss.CurrentRoomGrid, boss.CenterGridPosition());
    }
}
// ─────────────────────────────────────────────────────────────────────────────
// BossPhaseController.cs
// State machine that tracks boss health phases and manages:
//   • invisibility (alpha + damage reduction)
//   • minion wave spawning
//   • vulnerable window after minions die
//
// Add this to the boss prefab alongside BossUnit and BossAI.
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossPhaseController : MonoBehaviour
{
    public enum BossPhase
    {
        Normal,       // >50% HP, full damage
        Enraged,      // ≤50% HP, invisible, reduced damage, minions alive
        Vulnerable    // minions dead, increased damage window
    }

    // ── State ──────────────────────────────────────────────────────────────
    public BossPhase CurrentPhase { get; private set; } = BossPhase.Normal;

    private BossUnit              boss;
    private BossStats             stats;
    private BossDamageInterceptor interceptor;
    private HealthComponent       health;
    private EnemySpawner          spawner;

    // Tracks minions this boss spawned so we know when they're all dead
    private readonly List<EnemyUnit> spawnedMinions = new();

    private bool phaseTriggered  = false; // enrage triggered this run
    private bool minionsDead     = false;
    private bool invisActive     = false;
    private int  invisTurnsLeft  = 0;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        boss        = GetComponent<BossUnit>();
        interceptor = GetComponent<BossDamageInterceptor>();
        health      = GetComponent<HealthComponent>();
        spawner     = FindAnyObjectByType<EnemySpawner>();
    }

    private void Start()
    {
        health.OnHealthChanged += OnHealthChanged;
        interceptor.SetDamageMultiplier(1f);
    }

    private void OnDestroy()
    {
        if (health != null) health.OnHealthChanged -= OnHealthChanged;
    }

    // ── Health listener ────────────────────────────────────────────────────

    private void OnHealthChanged(int current, int max)
    {
        float pct = max > 0 ? (float)current / max : 0f;

        if (!phaseTriggered && pct <= stats.enrageThreshold)
            TriggerEnrage();

        CheckMinionStatus();
    }

    // ── Phase transitions ──────────────────────────────────────────────────

    private void TriggerEnrage()
    {
        phaseTriggered = true;
        CurrentPhase   = BossPhase.Enraged;

        // Go invisible and apply damage reduction
        SetInvisible(true);
        interceptor.SetDamageMultiplier(stats.damageReductionInvis);
        // Spawn the minion wave
        StartCoroutine(SpawnMinionWave());

        Debug.Log($"[BossPhaseController] {stats.bossName} ENRAGED — minion wave incoming!");
    }

    private void EnterVulnerable()
    {
        CurrentPhase  = BossPhase.Vulnerable;
        minionsDead   = true;

        // Come out of invisibility
        SetInvisible(false);
        interceptor.SetDamageMultiplier(stats.increasedDamageMultiplier);

        Debug.Log($"[BossPhaseController] {stats.bossName} VULNERABLE — increased damage active!");
    }

    // ── Invisibility ───────────────────────────────────────────────────────

    public void SetInvisible(bool on)
    {
        invisActive = on;
        boss.SetAlpha(on ? stats.invisAlpha : 1f);

        if (on)
        {
            invisTurnsLeft = stats.invisDurationTurns;
            interceptor.SetDamageMultiplier(stats.damageReductionInvis);
        }
        else
        {
            // Only restore full multiplier if we're NOT in the vulnerable window
            if (CurrentPhase != BossPhase.Vulnerable)
                interceptor.SetDamageMultiplier(1f);
        }
    }

    /// <summary>
    /// Called by BossAI at the end of each boss turn to tick down invisibility.
    /// </summary>
    public void TickInvisibility()
    {
        if (!invisActive) return;
        invisTurnsLeft--;
        if (invisTurnsLeft <= 0)
            SetInvisible(false);
    }

    public bool IsInvisible => invisActive;

    // ── Minion spawning ────────────────────────────────────────────────────

    private IEnumerator SpawnMinionWave()
    {
        if (spawner == null || stats.minionPrefabs.Count == 0) yield break;

        // Wait a frame so the phase transition visual settles
        yield return null;

        var room = boss.CurrentRoomGrid;
        if (room == null) yield break;

        int toSpawn = Mathf.Min(
            stats.minionsPerWave,
            stats.maxConcurrentMinions - spawnedMinions.Count
        );

        for (int i = 0; i < toSpawn; i++)
        {
            var prefab = stats.minionPrefabs[Random.Range(0, stats.minionPrefabs.Count)];
            var pos    = FindSpawnPosition(room);
            if (pos == null) continue;

            var minion = spawner.SpawnEnemy(prefab, room, pos.Value);
            if (minion == null) continue;

            spawnedMinions.Add(minion);

            // Listen for this minion dying so we can track the count
            minion.OnEnemyDied += OnMinionDied;

            Debug.Log($"[BossPhaseController] Spawned minion {minion.Stats?.enemyName} at {pos.Value}");
        }
    }

    private void OnMinionDied(EnemyUnit minion)
    {
        minion.OnEnemyDied -= OnMinionDied;
        spawnedMinions.Remove(minion);
        CheckMinionStatus();
    }

    private void CheckMinionStatus()
    {
        // Clean up any nulls from destroyed objects
        spawnedMinions.RemoveAll(m => m == null || m.IsDead);

        if (CurrentPhase == BossPhase.Enraged && spawnedMinions.Count == 0 && !minionsDead)
            EnterVulnerable();
    }

    // Find a walkable tile near the edges of the room (feels more dramatic)
    private GridPosition? FindSpawnPosition(RoomGrid room)
    {
        var floor = room.GetFloorTilemap();
        if (floor == null) return null;

        var b          = floor.cellBounds;
        var candidates = new System.Collections.Generic.List<GridPosition>();

        for (int x = b.xMin + 1; x < b.xMax - 1; x++)
        for (int y = b.yMin + 1; y < b.yMax - 1; y++)
        {
            var gp = new GridPosition(x, y);
            if (!room.IsWalkable(gp)) continue;

            // Prefer positions NOT overlapping the boss footprint
            if (boss.OccupiedCells.Contains(gp)) continue;

            candidates.Add(gp);
        }

        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }

    // ── Called by BossAI so the controller knows stats ─────────────────────
    public void Initialize(BossStats s) => stats = s;
}
// ─────────────────────────────────────────────────────────────────────────────
// BossPhaseController.cs
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BossPhaseController : MonoBehaviour
{
    public enum BossPhase { Normal, Enraged, Vulnerable }

    public BossPhase CurrentPhase { get; private set; } = BossPhase.Normal;

    private BossUnit              boss;
    private BossStats             stats;
    private BossDamageInterceptor interceptor;
    private HealthComponent       health;
    private EnemySpawner          spawner;

    private readonly List<EnemyUnit> spawnedMinions = new();

    private bool phaseTriggered      = false;
    private bool minionsDead         = false;
    private bool invisActive         = false;
    private int  invisTurnsLeft      = 0;
    private bool minionWaveDispatched = false; 

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

    public void Initialize(BossStats s)
    {
        stats = s;
        Debug.Log($"[BossPhaseController] Initialized with stats: {s?.bossName}");
    }

    // ── Health listener ────────────────────────────────────────────────────

    private void OnHealthChanged(int current, int max)
    {
        if (stats == null)
        {
            Debug.LogWarning("[BossPhaseController] OnHealthChanged fired but stats is null — Initialize not called yet.");
            return;
        }

        float pct = max > 0 ? (float)current / max : 0f;
        Debug.Log($"[BossPhaseController] HP={current}/{max} ({pct:P0}), threshold={stats.enrageThreshold}, triggered={phaseTriggered}, phase={CurrentPhase}");

        if (!phaseTriggered && pct <= stats.enrageThreshold)
            TriggerEnrage();

        CheckMinionStatus();
    }

    // ── Phase transitions ──────────────────────────────────────────────────

    private void TriggerEnrage()
    {
        phaseTriggered = true;
        CurrentPhase   = BossPhase.Enraged;

        SetInvisible(true);
        interceptor.SetDamageMultiplier(stats.damageReductionInvis);
        StartCoroutine(SpawnMinionWave());

        Debug.Log($"[BossPhaseController] {stats.bossName} ENRAGED!");
    }

    private void EnterVulnerable()
    {
        if (boss == null || boss.IsDead) return;
        CurrentPhase = BossPhase.Vulnerable;
        minionsDead  = true;

        SetInvisible(false);
        interceptor.SetDamageMultiplier(stats.increasedDamageMultiplier);

        Debug.Log($"[BossPhaseController] {stats.bossName} VULNERABLE!");
    }

    // ── Invisibility ───────────────────────────────────────────────────────

    public void SetInvisible(bool on)
    {
        if (stats == null)
        {
            Debug.LogError("[BossPhaseController] SetInvisible called before Initialize!");
            return;
        }
        if (boss == null || boss.IsDead) return; // add this

        invisActive = on;
        boss.SetAlpha(on ? stats.invisAlpha : 1f);

        if (on)
        {
            invisTurnsLeft = stats.invisDurationTurns;
            interceptor.SetDamageMultiplier(stats.damageReductionInvis);
            Debug.Log($"[BossPhaseController] Boss invisible for {invisTurnsLeft} turns.");
        }
        else
        {
            if (CurrentPhase != BossPhase.Vulnerable)
                interceptor.SetDamageMultiplier(1f);
            Debug.Log("[BossPhaseController] Boss visible again.");
        }
    }

    public void TickInvisibility()
    {
        if (!invisActive) return;
        if (boss == null || boss.IsDead) return; // add this
        invisTurnsLeft--;
        if (invisTurnsLeft <= 0)
            SetInvisible(false);
    }

    public bool IsInvisible => invisActive;

    // ── Minion spawning ────────────────────────────────────────────────────

    private IEnumerator SpawnMinionWave()
    {
        if (spawner == null || !stats.CanSpawnMinions)
        {
            minionWaveDispatched = true;
            yield break;
        }

        yield return null;
        minionWaveDispatched = true;

        if (boss == null || boss.IsDead) yield break;

        var room = boss.CurrentRoomGrid;

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
            minion.OnEnemyDied += OnMinionDied;

            Debug.Log($"[BossPhaseController] Spawned minion at {pos.Value}");
        }
    }

    private void OnMinionDied(EnemyUnit minion)
    {
        if (boss == null || boss.IsDead) return;
        minion.OnEnemyDied -= OnMinionDied;
        spawnedMinions.Remove(minion);
        CheckMinionStatus();
    }

    private void CheckMinionStatus()
    {
        if (boss == null || boss.IsDead) return;
        if (!minionWaveDispatched) return;

        spawnedMinions.RemoveAll(m => m == null || m.IsDead);
        if (CurrentPhase == BossPhase.Enraged && spawnedMinions.Count == 0 && !minionsDead)
            EnterVulnerable();
    }

    private GridPosition? FindSpawnPosition(RoomGrid room)
    {
        var floor = room.GetFloorTilemap();
        if (floor == null) return null;

        var b          = floor.cellBounds;
        var candidates = new List<GridPosition>();

        for (int x = b.xMin + 1; x < b.xMax - 1; x++)
        for (int y = b.yMin + 1; y < b.yMax - 1; y++)
        {
            var gp = new GridPosition(x, y);
            if (!room.IsWalkable(gp))            continue;
            if (boss.OccupiedCells.Contains(gp)) continue;
            candidates.Add(gp);
        }

        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }
}
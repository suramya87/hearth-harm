// ─────────────────────────────────────────────────────────────────────────────
// BossUnit.cs
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(HealthComponent))]
[RequireComponent(typeof(BossDamageInterceptor))]
public class BossUnit : MonoBehaviour, IHasHealth
{
    [Header("Stats")]
    [SerializeField] private BossStats bossStats;

    [Header("Footprint")]
    [SerializeField] private int footprintSize = 2;

    [Header("Debug")]
    [SerializeField] private bool          showDebugLogs;
    [SerializeField] private GameObject    selectedVisual;

    private GridPosition          gridPosition;
    private RoomGrid              currentRoomGrid;
    private HealthComponent       health;
    private BossDamageInterceptor interceptor;
    private SpriteRenderer[]      renderers;
    private bool                  initialized;
    private int                   turnsWaited;

    private readonly List<GridPosition> occupiedCells = new();

    public event Action<BossUnit> OnBossDied;

    // ── Properties ─────────────────────────────────────────────────────────
    public BossStats           Stats           => bossStats;
    public HealthComponent     Health          => health;
    public GridPosition        GridPosition    => gridPosition;
    public RoomGrid            CurrentRoomGrid => currentRoomGrid;
    public bool                IsInitialized   => initialized;
    public bool                IsDead          => health != null && health.IsDead;
    public List<GridPosition>  OccupiedCells   => occupiedCells;

    public int GetMaxHealth() => bossStats != null ? bossStats.maxHealth : 300;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        health      = GetComponent<HealthComponent>();
        interceptor = GetComponent<BossDamageInterceptor>();
        renderers   = GetComponentsInChildren<SpriteRenderer>();
    }

    private void Start()
    {
        health.OnDeath += HandleDeath;
        if (selectedVisual) selectedVisual.SetActive(false);
    }

    private void OnDestroy()
    {
        if (health != null) health.OnDeath -= HandleDeath;
        RemoveFromGrid();
        var eu = GetComponent<EnemyUnit>();
        if (eu != null) EnemyManager.Instance?.UnregisterEnemy(eu);
    }

    // ── Grid placement ─────────────────────────────────────────────────────

    public void PlaceOnGrid(RoomGrid room, GridPosition origin)
    {
        RemoveFromGrid();
        currentRoomGrid = room;
        gridPosition    = origin;
        occupiedCells.Clear();

        var eu = GetComponent<EnemyUnit>();
        for (int dx = 0; dx < footprintSize; dx++)
        for (int dy = 0; dy < footprintSize; dy++)
        {
            var cell = new GridPosition(origin.x + dx, origin.y + dy);
            occupiedCells.Add(cell);
            if (eu != null) room.AddEnemyAtGridPosition(cell, eu);
        }

        eu?.SyncRoomGrid(room);

        var originWorld = room.GetWorldPosition(origin);
        transform.position = new Vector3(
            originWorld.x + (footprintSize - 1) * 0.5f,
            originWorld.y + (footprintSize - 1) * 0.5f,
            transform.position.z
        );

        initialized = true;
        if (showDebugLogs)
            Debug.Log($"[BossUnit] {bossStats?.bossName} placed at {origin} ({footprintSize}x{footprintSize})");
    }

    public void MoveToPosition(GridPosition newOrigin)
    {
        if (!initialized) return;
        RemoveFromGrid();

        gridPosition = newOrigin;
        occupiedCells.Clear();

        var eu = GetComponent<EnemyUnit>();
        for (int dx = 0; dx < footprintSize; dx++)
        for (int dy = 0; dy < footprintSize; dy++)
        {
            var cell = new GridPosition(newOrigin.x + dx, newOrigin.y + dy);
            occupiedCells.Add(cell);
            if (eu != null) currentRoomGrid.AddEnemyAtGridPosition(cell, eu);
        }

        eu?.SyncRoomGrid(currentRoomGrid);

        var originWorld = currentRoomGrid.GetWorldPosition(newOrigin);
        transform.position = new Vector3(
            originWorld.x + (footprintSize - 1) * 0.5f,
            originWorld.y + (footprintSize - 1) * 0.5f,
            transform.position.z
        );
    }

    // ── Turn gating ────────────────────────────────────────────────────────

    public bool CanActThisTurn()
    {
        if (IsDead) return false;
        if (bossStats == null) return true;
        if (turnsWaited < bossStats.turnsBeforeFirstAction) { turnsWaited++; return false; }
        return true;
    }

    // ── Visibility ─────────────────────────────────────────────────────────

    public void SetAlpha(float alpha)
    {
        // Refresh in case Awake ran before child renderers existed
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<SpriteRenderer>();

        foreach (var sr in renderers)
        {
            if (sr == null) continue;
            var c = sr.color;
            c.a = alpha;
            sr.color = c;
        }
    }

    // ── Selection visual ───────────────────────────────────────────────────

    public void SetSelected(bool on)
    {
        if (selectedVisual) selectedVisual.SetActive(on);
    }

    // ── Center position ────────────────────────────────────────────────────

    public GridPosition CenterGridPosition() => new(
        gridPosition.x + footprintSize / 2,
        gridPosition.y + footprintSize / 2
    );

    // ── Death ──────────────────────────────────────────────────────────────

    private void HandleDeath()
    {
        if (showDebugLogs) Debug.Log($"[BossUnit] {bossStats?.bossName} died.");
        RemoveFromGrid();
        OnBossDied?.Invoke(this);
        Destroy(gameObject, 0.75f);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void RemoveFromGrid()
    {
        if (currentRoomGrid == null || !initialized) return;
        var eu = GetComponent<EnemyUnit>();
        foreach (var cell in occupiedCells)
            if (eu != null) currentRoomGrid.RemoveEnemyAtGridPosition(cell, eu);
        occupiedCells.Clear();
    }
}
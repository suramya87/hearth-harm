using System;
using UnityEngine;

/// <summary>
/// Represents one enemy on the grid.
/// Implements IHasHealth so HealthComponent auto-reads max HP from EnemyStats.
/// </summary>
[RequireComponent(typeof(HealthComponent))]
public class EnemyUnit : MonoBehaviour, IHasHealth
{
    [Header("Stats")]
    [SerializeField] private EnemyStats stats;

    [Header("Debug")]
    [SerializeField] private bool         showDebugLogs;
    [SerializeField] private GameObject   selectedVisual;

    private GridPosition     gridPosition;
    private RoomGrid         currentRoomGrid;
    private HealthComponent  health;
    private bool             initialized;
    private int              turnsWaited;

    public event Action<EnemyUnit> OnEnemyDied;

    // ── Properties ─────────────────────────────────────────────────────────
    public EnemyStats      Stats           => stats;
    public HealthComponent Health          => health;
    public GridPosition    GridPosition    => gridPosition;
    public RoomGrid        CurrentRoomGrid => currentRoomGrid;
    public bool            IsInitialized   => initialized;
    public bool            IsDead          => health != null && health.IsDead;

    // ── IHasHealth ─────────────────────────────────────────────────────────
    public int GetMaxHealth() => stats != null ? stats.maxHealth : 100;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()  { health = GetComponent<HealthComponent>(); }

    private void Start()
    {
        health.OnDeath += HandleDeath;
        if (selectedVisual) selectedVisual.SetActive(false);
    }

    private void OnDestroy()
    {
        if (health != null) health.OnDeath -= HandleDeath;
        if (currentRoomGrid != null && initialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, this);
        EnemyManager.Instance?.UnregisterEnemy(this);
    }

    // ── Grid placement ─────────────────────────────────────────────────────

    public void PlaceOnGrid(RoomGrid room, GridPosition pos)
    {
        if (currentRoomGrid != null && initialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, this);

        currentRoomGrid = room;
        gridPosition    = pos;

        var world = room.GetWorldPosition(pos);
        transform.position = new Vector3(world.x, world.y, transform.position.z);
        room.AddEnemyAtGridPosition(pos, this);
        initialized = true;

        if (showDebugLogs) Debug.Log($"[EnemyUnit] {stats?.enemyName} placed at {pos}");
    }

    public void MoveToPosition(GridPosition newPos)
    {
        if (!initialized) return;
        currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, this);
        gridPosition = newPos;
        currentRoomGrid.AddEnemyAtGridPosition(newPos, this);

        var world = currentRoomGrid.GetWorldPosition(newPos);
        transform.position = new Vector3(world.x, world.y, transform.position.z);

        if (showDebugLogs) Debug.Log($"[EnemyUnit] {stats?.enemyName} moved to {newPos}");
    }

    // ── Turn gating ────────────────────────────────────────────────────────

    public bool CanActThisTurn()
    {
        if (IsDead) return false;
        if (stats == null) return true;
        if (turnsWaited < stats.turnsBeforeFirstAction) { turnsWaited++; return false; }
        return true;
    }

    // ── Selection visual ───────────────────────────────────────────────────

    public void SetSelected(bool on)
    {
        if (selectedVisual) selectedVisual.SetActive(on);
    }

    // ── Death ──────────────────────────────────────────────────────────────

    private void HandleDeath()
    {
        if (showDebugLogs) Debug.Log($"[EnemyUnit] {stats?.enemyName} died.");
        if (currentRoomGrid != null && initialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, this);
        OnEnemyDied?.Invoke(this);
        EnemyManager.Instance?.UnregisterEnemy(this);
        Destroy(gameObject, 0.5f);
    }
}
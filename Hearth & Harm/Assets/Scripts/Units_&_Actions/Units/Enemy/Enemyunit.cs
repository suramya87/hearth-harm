// ─────────────────────────────────────────────────────────────────────────────
// EnemyUnit.cs
// ─────────────────────────────────────────────────────────────────────────────
using System;
using UnityEngine;

[RequireComponent(typeof(HealthComponent))]
public class EnemyUnit : MonoBehaviour, IHasHealth
{
    [Header("Stats")]
    [SerializeField] private EnemyStats stats;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs;
    [SerializeField] private GameObject selectedVisual;

    private GridPosition gridPosition;
    private RoomGrid currentRoomGrid;
    private HealthComponent health;
    private bool initialized;
    private int turnsWaited;

    public event Action<EnemyUnit> OnEnemyDied;

    public EnemyStats Stats => stats;
    public HealthComponent Health => health;
    public GridPosition GridPosition => gridPosition;
    public RoomGrid CurrentRoomGrid => currentRoomGrid;
    public bool IsInitialized => initialized;
    public bool IsDead => health != null && health.IsDead;

    // FIX: defer to BossUnit if present so HealthComponent gets the right max HP
    public int GetMaxHealth()
    {
        var boss = GetComponent<BossUnit>();
        if (boss != null) return boss.GetMaxHealth();
        return stats != null ? stats.maxHealth : 100;
    }

    private void Awake() => health = GetComponent<HealthComponent>();

    private void Start()
    {
        health.OnDeath += HandleDeath;
        if (selectedVisual) selectedVisual.SetActive(false);
    }

    private void OnDestroy()
    {
        if (health != null) health.OnDeath -= HandleDeath;

        // BossUnit owns its own cleanup — EnemyUnit must not interfere
        if (GetComponent<BossUnit>() != null) return;

        if (currentRoomGrid != null && initialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, this);

        EnemyManager.Instance?.UnregisterEnemy(this);
    }

    public void PlaceOnGrid(RoomGrid room, GridPosition pos)
    {
        if (currentRoomGrid != null && initialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, this);

        currentRoomGrid = room;
        gridPosition = pos;

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

        if (GameManager.IsMultiplayer)
        {
            var bridge = GetComponent<NetworkedEnemyBridge>();
            bridge?.ServerBroadcastMove(newPos, currentRoomGrid);
        }
    }

    public bool CanActThisTurn()
    {
        if (IsDead) return false;
        if (stats == null) return true;
        if (turnsWaited < stats.turnsBeforeFirstAction) { turnsWaited++; return false; }
        return true;
    }

    public void SetSelected(bool on)
    {
        if (selectedVisual) selectedVisual.SetActive(on);
    }

    internal void SyncRoomGrid(RoomGrid room)
    {
        currentRoomGrid = room;
        initialized     = true;
    }

    private void HandleDeath()
    {
        // FIX: BossUnit owns death — skip all EnemyUnit death logic for the boss
        if (GetComponent<BossUnit>() != null) return;

        if (stats != null && CurrencyManager.Instance != null)
        {
            int coinsDropped = stats.RollCoinDrop();
            CurrencyManager.Instance.AddCoins(coinsDropped);
            if (showDebugLogs)
                Debug.Log($"[EnemyUnit] {stats.enemyName} dropped {coinsDropped} coins.");
        }

        if (showDebugLogs) Debug.Log($"[EnemyUnit] {stats?.enemyName} died.");

        if (currentRoomGrid != null && initialized)
            currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, this);

        if (GameManager.IsMultiplayer)
        {
            var bridge = GetComponent<NetworkedEnemyBridge>();
            bridge?.NotifyDeathClientRpc();
        }

        OnEnemyDied?.Invoke(this);
        EnemyManager.Instance?.UnregisterEnemy(this);
        Destroy(gameObject, 0.5f);
    }
}
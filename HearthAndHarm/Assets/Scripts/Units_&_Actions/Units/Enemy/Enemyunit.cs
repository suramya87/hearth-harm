using System;
using UnityEngine;

[RequireComponent(typeof(HealthComponent))]
public class EnemyUnit : MonoBehaviour, IHasHealth
{
    [Header("Stats")]
    [SerializeField] private EnemyStats stats;

    [Header("Visual")]
    [Tooltip("The child GameObject that holds the SpriteRenderer + Animator (e.g. 'Sprite').")]
    [SerializeField] private Transform spriteChild;

    [Tooltip("Scale applied to the sprite child only. Root stays at (1,1,1).\n" +
             "1 = one tile exactly. 1.5 = sprite is 50% larger than the tile.")]
    [SerializeField] private Vector2 spriteScale = Vector2.one;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs;
    [SerializeField] private GameObject selectedVisual;

    private GridPosition gridPosition;
    private RoomGrid currentRoomGrid;
    private HealthComponent health;
    private bool initialized;
    private int turnsWaited;

    // Prevents HandleDeath and OnDestroy from both running cleanup.
    // HandleDeath sets this first; OnDestroy checks it before acting.
    private bool deathHandled = false;

    public event Action<EnemyUnit> OnEnemyDied;

    public EnemyStats Stats => stats;
    public HealthComponent Health => health;
    public GridPosition GridPosition => gridPosition;
    public RoomGrid CurrentRoomGrid => currentRoomGrid;
    public bool IsInitialized => initialized;
    public bool IsDead => health != null && health.IsDead;

    public int GetMaxHealth()
    {
        var boss = GetComponent<BossUnit>();
        if (boss != null) return boss.GetMaxHealth();
        return stats != null ? stats.maxHealth : 100;
    }

    private void Awake()
    {
        health = GetComponent<HealthComponent>();
        ApplySpriteScale();
    }

    private void Start()
    {
        health.OnDeath += HandleDeath;
        if (selectedVisual) selectedVisual.SetActive(false);
    }

    private void OnDestroy()
    {
        if (health != null) health.OnDeath -= HandleDeath;

        // Only run cleanup here if HandleDeath never fired — e.g. direct
        // Destroy() call, ClearAllEnemies(), or level reload. In normal
        // combat HandleDeath runs first and sets deathHandled = true, so
        // this block is skipped entirely for normal deaths.
        if (!deathHandled)
        {
            deathHandled = true;

            if (GetComponent<BossUnit>() != null) return;

            if (currentRoomGrid != null && initialized)
                currentRoomGrid.RemoveEnemyAtGridPosition(gridPosition, this);

            // Use plain UnregisterEnemy — room ref may or may not be valid
            // here depending on destroy order, but it's the best we can do.
            EnemyManager.Instance?.UnregisterEnemy(this);
        }
    }

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
        // Guard — HealthComponent.OnDeath could theoretically fire more than
        // once, and OnDestroy also calls cleanup. Only the first call wins.
        if (deathHandled) return;
        deathHandled = true;

        if (GetComponent<BossUnit>() != null) return;

        // Capture the room reference NOW, before any cleanup can null it.
        // This is the key fix — by the time Destroy(gameObject, 0.5f) fires
        // OnDestroy, currentRoomGrid may already be null, so UnregisterEnemy
        // can't reliably check it. We pass it explicitly instead.
        var roomAtDeath = currentRoomGrid;

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

        // Use the room-explicit overload so OnRoomCleared fires reliably
        // even if currentRoomGrid gets nulled before the check runs.
        EnemyManager.Instance?.UnregisterEnemyFromRoom(this, roomAtDeath);

        Destroy(gameObject, 0.5f);
    }

    // ── Sprite scale ───────────────────────────────────────────────────────

    private void ApplySpriteScale()
    {
        if (spriteChild == null) return;
        spriteChild.localScale = new Vector3(spriteScale.x, spriteScale.y, 1f);
    }

#if UNITY_EDITOR
    private void OnValidate() => ApplySpriteScale();
#endif
}
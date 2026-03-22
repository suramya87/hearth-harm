using UnityEngine;

/// <summary>
/// Loads class stats from ClassStatsDatabase.
/// Owns stamina and resets it on room transitions.
/// </summary>
public class PlayerStats : MonoBehaviour, IHasHealth
{
    [Header("Class")]
    public PlayerClass playerClass;

    [Header("Database")]
    public ClassStatsDatabase classStatsDatabase;

    [Header("Runtime (read-only in play)")]
    public int maxHealth;
    public int currentHealth;
    public int maxStamina;
    public int currentStamina;

    private HealthComponent health;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        health = GetComponent<HealthComponent>();
        ApplyClassStats();

        if (health != null)
        {
            health.InitializeHealth(maxHealth);
            currentHealth = health.CurrentHealth;
            health.OnHealthChanged += (cur, _) => currentHealth = cur;
        }
    }

    private void OnEnable()  => RoomManager.OnAnyRoomChanged += OnRoomChanged;
    private void OnDisable() => RoomManager.OnAnyRoomChanged -= OnRoomChanged;

    // ── Room transition ────────────────────────────────────────────────────

    private void OnRoomChanged(LevelGenerator.PlacedRoom _)
    {
        currentStamina = maxStamina;
        TurnSystem.Instance?.ForcePlayerTurn();
        Debug.Log("[PlayerStats] Room entered → stamina refilled, player turn forced.");
    }

    // ── Stats loading ──────────────────────────────────────────────────────

    private void ApplyClassStats()
    {
        if (classStatsDatabase == null)
        { Debug.LogError("[PlayerStats] Missing ClassStatsDatabase!"); return; }

        var stats = classStatsDatabase.Get(playerClass);
        if (stats == null) return;

        maxHealth  = stats.maxHealth;
        maxStamina = stats.maxStamina;
        currentHealth  = maxHealth;
        currentStamina = maxStamina;
    }

    // ── IHasHealth ─────────────────────────────────────────────────────────
    public int  GetMaxHealth()            => maxHealth;

    // ── Stamina helpers ────────────────────────────────────────────────────
    public int  GetCurrentStaminaPoints() => currentStamina;
    public void SetCurrentStaminaPoints(int v) => currentStamina = v;
    public int  GetMaxStaminaPoints()     => maxStamina;
}
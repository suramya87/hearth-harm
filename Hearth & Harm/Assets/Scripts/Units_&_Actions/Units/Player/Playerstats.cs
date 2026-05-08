using UnityEngine;

/// <summary>
/// Loads class stats from ClassStatsDatabase.
/// Owns stamina and derived player stat values.
/// </summary>
public class PlayerStats : MonoBehaviour, IHasHealth
{
    [Header("Class")]
    public PlayerClass playerClass;

    [Header("Database")]
    public ClassStatsDatabase classStatsDatabase;

    [Header("Derived Runtime Pools")]
    public int maxHealth;
    public int currentHealth;
    public int maxStamina;
    public int currentStamina;

    [Header("Core Stats")]
    public int strength;
    public int constitution;
    public int dexterity;
    public int intelligence;
    public int perception;
    public int charisma;
    public int luck;

    private HealthComponent health;

    private void Awake()
    {
        health = GetComponent<HealthComponent>();
        ApplyClassStats();

        if (health != null)
        {
            health.InitializeHealth(maxHealth);
            currentHealth = health.CurrentHealth;
            health.OnHealthChanged += OnHealthChanged;
        }
    }

    private void OnDestroy()
    {
        if (health != null)
            health.OnHealthChanged -= OnHealthChanged;
    }

    private void OnEnable() => RoomManager.OnAnyRoomChanged += OnRoomChanged;
    private void OnDisable() => RoomManager.OnAnyRoomChanged -= OnRoomChanged;

    private void OnHealthChanged(int current, int max)
    {
        currentHealth = current;
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom _)
    {
        currentStamina = maxStamina;
        TurnSystem.Instance?.ForcePlayerTurn();
        Debug.Log("[PlayerStats] Room entered → stamina refilled, player turn forced.");
    }

    private void ApplyClassStats()
    {
        if (classStatsDatabase == null)
        {
            Debug.LogError("[PlayerStats] Missing ClassStatsDatabase!");
            return;
        }

        ClassStats stats = classStatsDatabase.Get(playerClass);
        if (stats == null)
            return;

        strength = stats.strength;
        constitution = stats.constitution;
        dexterity = stats.dexterity;
        intelligence = stats.intelligence;
        perception = stats.perception;
        charisma = stats.charisma;
        luck = stats.luck;

        maxHealth = Mathf.Max(1, stats.baseMaxHealth + constitution);
        maxStamina = Mathf.Max(1, stats.baseMaxStamina + dexterity);

        currentHealth = maxHealth;
        currentStamina = maxStamina;
    }

    public int GetMaxHealth() => maxHealth;

    public int GetCurrentStaminaPoints() => currentStamina;

    public void SetCurrentStaminaPoints(int value)
    {
        currentStamina = Mathf.Clamp(value, 0, maxStamina);
    }

    public int GetMaxStaminaPoints() => maxStamina;

    public int GetPopularityBonusPercent()
    {
        return charisma;
    }
}
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

    [Header("Stamina Popup")]
    [SerializeField] private GameObject staminaNumberPrefab;
    [SerializeField] private Vector3 staminaPopupOffset = new(0f, 1f, 0f);

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
        RefillStaminaToMax();
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
        maxStamina = Mathf.Max(1, 10 + dexterity * 2);
        currentHealth = maxHealth;
        currentStamina = maxStamina;
    }

    public int GetMaxHealth() => maxHealth;

    public int GetCurrentStaminaPoints() => currentStamina;

    public void SetCurrentStaminaPoints_BROKEN_FIND_CALLER(int value)
    {
        int before = currentStamina;
        currentStamina = Mathf.Clamp(value, 0, maxStamina);

        Debug.LogWarning(
            $"[PlayerStats] SetCurrentStaminaPoints called: {before} → {currentStamina}\n" +
            $"{System.Environment.StackTrace}",
            this
        );
    }

    public int GetMaxStaminaPoints() => maxStamina;

    public int GetPopularityBonusPercent()
    {
        return charisma;
    }

    public int RollStaminaRecovery()
    {
        int diceCount = Mathf.Max(1, Mathf.CeilToInt(dexterity / 2f));

        int rolledRecovery = 0;

        for (int i = 0; i < diceCount; i++)
            rolledRecovery += Random.Range(1, 7);

        int before = currentStamina;
        int after = Mathf.Clamp(before + rolledRecovery, 0, maxStamina);

        currentStamina = after;

        int actualRecovered = after - before;

        if (actualRecovered > 0)
        {
            StaminaNumber.Spawn(
                staminaNumberPrefab,
                transform.position + staminaPopupOffset,
                actualRecovered
            );
        }

        Debug.Log(
            $"[PlayerStats] Stamina recovery: {diceCount}d6 rolled {rolledRecovery}, " +
            $"before {before}, recovered {actualRecovered}, current {currentStamina}/{maxStamina}"
        );

        return actualRecovered;
    }

    public void SpendStamina(int amount)
    {
        if (amount <= 0) return;
        currentStamina = Mathf.Clamp(currentStamina - amount, 0, maxStamina);
    }

    public void RefillStaminaToMax()
    {
        currentStamina = maxStamina;
    }

    public int RecoverStamina(int amount)
    {
        if (amount <= 0) return 0;

        int before = currentStamina;
        currentStamina = Mathf.Clamp(currentStamina + amount, 0, maxStamina);

        return currentStamina - before;
    }
}
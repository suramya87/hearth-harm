using System;
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

    public event Action<int, int> OnStaminaChanged;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        health = GetComponent<HealthComponent>();
        ApplyClassStats();

        if (health != null)
        {
            health.InitializeHealth(maxHealth);
            currentHealth = health.CurrentHealth;
            health.OnHealthChanged += (cur, max) => currentHealth = cur;
        }
    }

    private void OnEnable()
    {
        RoomManager.OnAnyRoomChanged += OnRoomChanged;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared += OnRoomCleared;
    }

    private void OnDisable()
    {
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= OnRoomCleared;
    }

    private void OnRoomCleared(RoomGrid clearedRoom)
    {
        RoomGrid currentRoom = RoomManager.Instance != null
            ? RoomManager.Instance.GetCurrentRoomGrid()
            : null;

        if (clearedRoom == null || clearedRoom != currentRoom) return;

        RefillStaminaToMax();
        Debug.Log("[PlayerStats] Combat room cleared → stamina refilled for traversal.");
    }

    // ── Room transition ────────────────────────────────────────────────────

    private void OnRoomChanged(LevelGenerator.PlacedRoom _)
    {
        RefillStaminaToMax();

        if (!GameManager.IsMultiplayer)
        {
            TurnSystem.Instance?.ForcePlayerTurn();
        }

        Debug.Log("[PlayerStats] Room entered → stamina refilled" +
                  (GameManager.IsMultiplayer ? "." : ", player turn forced."));
    }

    // ── Stats loading ──────────────────────────────────────────────────────

    private void ApplyClassStats()
    {
        if (classStatsDatabase == null)
        {
            Debug.LogError("[PlayerStats] Missing ClassStatsDatabase!");
            return;
        }

        ClassStats stats = classStatsDatabase.Get(playerClass);
        if (stats == null) return;

        strength     = stats.strength;
        constitution = stats.constitution;
        dexterity    = stats.dexterity;
        intelligence = stats.intelligence;
        perception   = stats.perception;
        charisma     = stats.charisma;
        luck         = stats.luck;

        maxHealth  = Mathf.Max(1, stats.baseMaxHealth + constitution);
        maxStamina = Mathf.Max(1, 10 + (dexterity * 2));

        currentHealth  = maxHealth;
        currentStamina = maxStamina;
    }

    public void InitializeWithClass(PlayerClass chosenClass)
    {
        playerClass = chosenClass;
        ApplyClassStats();

        if (health != null)
            health.InitializeHealth(maxHealth);

        currentHealth  = maxHealth;
        currentStamina = maxStamina;

        OnStaminaChanged?.Invoke(currentStamina, maxStamina);

        Debug.Log($"[PlayerStats] Initialized as {chosenClass} — " +
                  $"HP {currentHealth}/{maxHealth}, Stamina {currentStamina}/{maxStamina}");
    }

    // ── IHasHealth ─────────────────────────────────────────────────────────

    public int GetMaxHealth() => maxHealth;

    // ── Stamina API ────────────────────────────────────────────────────────

    public int  GetCurrentStaminaPoints() => currentStamina;
    public int  GetMaxStaminaPoints()     => maxStamina;

    public void SetCurrentStaminaPoints(int value)
    {
        currentStamina = Mathf.Clamp(value, 0, maxStamina);
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    public void SpendStamina(int amount)
    {
        if (amount <= 0) return;
        currentStamina = Mathf.Clamp(currentStamina - amount, 0, maxStamina);
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    public void RefillStaminaToMax()
    {
        currentStamina = maxStamina;
        OnStaminaChanged?.Invoke(currentStamina, maxStamina);
    }

    public int RollStaminaRecovery()
    {
        int diceCount      = Mathf.Max(1, Mathf.CeilToInt(dexterity / 2f));
        int rolledRecovery = 0;

        for (int i = 0; i < diceCount; i++)
            rolledRecovery += UnityEngine.Random.Range(1, 7);

        int before = currentStamina;
        currentStamina = Mathf.Clamp(currentStamina + rolledRecovery, 0, maxStamina);
        int actualRecovered = currentStamina - before;

        if (actualRecovered > 0)
        {
            OnStaminaChanged?.Invoke(currentStamina, maxStamina);

            if (staminaNumberPrefab != null)
                StaminaNumber.Spawn(
                    staminaNumberPrefab,
                    transform.position + staminaPopupOffset,
                    actualRecovered);
        }

        return actualRecovered;
    }

    public void IncreaseStat(PlayerStatType statType, int amount = 1)
    {
        switch (statType)
        {
            case PlayerStatType.Strength:      strength      += amount; break;
            case PlayerStatType.Constitution:  constitution  += amount; break;
            case PlayerStatType.Dexterity:     dexterity     += amount; break;
            case PlayerStatType.Intelligence:  intelligence  += amount; break;
            case PlayerStatType.Perception:    perception    += amount; break;
            case PlayerStatType.Charisma:      charisma      += amount; break;
            case PlayerStatType.Luck:          luck          += amount; break;
        }

        RecalculateDerivedStats();

        if (statType == PlayerStatType.Dexterity)
            RefillStaminaToMax();

        Debug.Log($"[PlayerStats] Increased {statType} by {amount}");
    }

    private void RecalculateDerivedStats()
    {
        ClassStats baseStats = classStatsDatabase != null
            ? classStatsDatabase.Get(playerClass)
            : null;

        if (baseStats == null) return;

        int oldMaxHealth   = maxHealth;
        int oldCurrentHP   = currentHealth;
        int oldCurrentStam = currentStamina;

        maxHealth  = Mathf.Max(1, baseStats.baseMaxHealth + constitution);
        maxStamina = Mathf.Max(1, 10 + dexterity * 2);

        int healthIncrease = maxHealth - oldMaxHealth;
        currentHealth  = healthIncrease > 0
            ? Mathf.Min(oldCurrentHP + healthIncrease, maxHealth)
            : Mathf.Clamp(oldCurrentHP, 1, maxHealth);

        currentStamina = Mathf.Clamp(oldCurrentStam, 0, maxStamina);

        if (health != null)
            health.SetHealth(currentHealth);

        OnStaminaChanged?.Invoke(currentStamina, maxStamina);

        Debug.Log($"[PlayerStats] Recalculated. HP {currentHealth}/{maxHealth}, " +
                  $"Stamina {currentStamina}/{maxStamina}");
    }
}
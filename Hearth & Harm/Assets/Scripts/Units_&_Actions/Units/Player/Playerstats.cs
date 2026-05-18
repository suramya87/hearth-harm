using UnityEngine;
using System;

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

    [Header("Progression")]
    public int availablePerkPoints;
    public PlayerStatType? pendingStatUpgrade;

    [Header("Core Stats")]
    public int strength;
    public int constitution;
    public int dexterity;
    public int intelligence;
    public int perception;
    public int charisma;
    public int luck;

    private HealthComponent health;

    // --- Lifecycle ---

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

        if (clearedRoom == null || clearedRoom != currentRoom)
            return;

        RefillStaminaToMax();

        Debug.Log("[PlayerStats] Combat room cleared → stamina refilled for traversal.");
    }

    // --- Room Transition ---

    private void OnRoomChanged(LevelGenerator.PlacedRoom _)
    {
        RefillStaminaToMax();
        TurnSystem.Instance?.ForcePlayerTurn();
        Debug.Log("[PlayerStats] Room entered → stamina refilled, player turn forced.");
    }

    // --- Stats Loading ---

    private void ApplyClassStats()
    {
        if (classStatsDatabase == null)
        {
            Debug.LogError("[PlayerStats] Missing ClassStatsDatabase!");
            return;
        }

        ClassStats stats = classStatsDatabase.Get(playerClass);
        if (stats == null) return;

        // Apply Core Stats
        strength = stats.strength;
        constitution = stats.constitution;
        dexterity = stats.dexterity;
        intelligence = stats.intelligence;
        perception = stats.perception;
        charisma = stats.charisma;
        luck = stats.luck;

        // Calculate Derived Stats
        maxHealth = Mathf.Max(1, stats.baseMaxHealth + constitution);
        maxStamina = Mathf.Max(1, 10 + (dexterity * 2));
        
        currentHealth = maxHealth;
        currentStamina = maxStamina;
    }

    // --- IHasHealth Implementation ---
    public int GetMaxHealth() => maxHealth;

    // --- Stamina Logic ---

    public int GetCurrentStaminaPoints() => currentStamina;

    // This is the method Unit.cs was looking for!
    public void SetCurrentStaminaPoints(int value)
    {
        currentStamina = Mathf.Clamp(value, 0, maxStamina);
    }

    public int GetMaxStaminaPoints() => maxStamina;

    public void SpendStamina(int amount)
    {
        if (amount <= 0) return;
        currentStamina = Mathf.Clamp(currentStamina - amount, 0, maxStamina);
    }

    public void RefillStaminaToMax() => currentStamina = maxStamina;

    /// <summary>
    /// Recovers stamina using a dice roll based on Dexterity.
    /// Used for "End Turn" or "Rest" mechanics.
    /// </summary>
    public int RollStaminaRecovery()
    {
        int diceCount = Mathf.Max(1, Mathf.CeilToInt(dexterity / 2f));
        int rolledRecovery = 0;

        for (int i = 0; i < diceCount; i++)
            rolledRecovery += UnityEngine.Random.Range(1, 7);

        int before = currentStamina;
        currentStamina = Mathf.Clamp(currentStamina + rolledRecovery, 0, maxStamina);
        int actualRecovered = currentStamina - before;

        if (actualRecovered > 0 && staminaNumberPrefab != null)
        {
            StaminaNumber.Spawn(
                staminaNumberPrefab,
                transform.position + staminaPopupOffset,
                actualRecovered
            );
        }

        return actualRecovered;
    }

    public void IncreaseStat(PlayerStatType statType, int amount = 1)
    {
        switch (statType)
        {
            case PlayerStatType.Strength: strength += amount; break;
            case PlayerStatType.Constitution: constitution += amount; break;
            case PlayerStatType.Dexterity: dexterity += amount; break;
            case PlayerStatType.Intelligence: intelligence += amount; break;
            case PlayerStatType.Perception: perception += amount; break;
            case PlayerStatType.Charisma: charisma += amount; break;
            case PlayerStatType.Luck: luck += amount; break;
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

        if (baseStats == null)
            return;

        int oldMaxHealth = maxHealth;
        int oldMaxStamina = maxStamina;

        int oldCurrentHealth = currentHealth;
        int oldCurrentStamina = currentStamina;

        maxHealth = Mathf.Max(1, baseStats.baseMaxHealth + constitution);
        maxStamina = Mathf.Max(1, 10 + dexterity * 2);

        int healthIncrease = maxHealth - oldMaxHealth;

        if (healthIncrease > 0)
            currentHealth = Mathf.Min(oldCurrentHealth + healthIncrease, maxHealth);
        else
            currentHealth = Mathf.Clamp(oldCurrentHealth, 1, maxHealth);

        currentStamina = Mathf.Clamp(oldCurrentStamina, 0, maxStamina);

        if (health != null)
            health.SetHealth(currentHealth);

        Debug.Log($"[PlayerStats] Recalculated stats. HP {currentHealth}/{maxHealth}, Stamina {currentStamina}/{maxStamina}");
    }

    public void AddPerkPoint(int amount = 1)
    {
        availablePerkPoints += Mathf.Max(0, amount);
        Debug.Log($"[PlayerStats] Added perk point. Available: {availablePerkPoints}");
    }

    public void PreviewStatUpgrade(PlayerStatType statType)
    {
        if (availablePerkPoints <= 0)
            return;

        pendingStatUpgrade = statType;
    }

    public void CancelPendingUpgrade()
    {
        pendingStatUpgrade = null;
    }

    public void ConfirmPendingUpgrade()
    {
        if (!pendingStatUpgrade.HasValue)
            return;

        if (availablePerkPoints <= 0)
            return;

        IncreaseStat(pendingStatUpgrade.Value, 1);
        availablePerkPoints--;

        pendingStatUpgrade = null;

        Debug.Log($"[PlayerStats] Confirmed stat upgrade. Remaining points: {availablePerkPoints}");
    }

    public int GetStatValue(PlayerStatType statType)
    {
        switch (statType)
        {
            case PlayerStatType.Strength: return strength;
            case PlayerStatType.Constitution: return constitution;
            case PlayerStatType.Dexterity: return dexterity;
            case PlayerStatType.Intelligence: return intelligence;
            case PlayerStatType.Perception: return perception;
            case PlayerStatType.Charisma: return charisma;
            case PlayerStatType.Luck: return luck;
            default: return 0;
        }
    }
}
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

    private void OnEnable() => RoomManager.OnAnyRoomChanged += OnRoomChanged;
    private void OnDisable() => RoomManager.OnAnyRoomChanged -= OnRoomChanged;

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
}
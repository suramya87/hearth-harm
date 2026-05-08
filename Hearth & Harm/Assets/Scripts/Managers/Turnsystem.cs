using System;
using UnityEngine;

/// <summary>
/// Single-player turn system.
/// Player calls NextTurn() → enemies run → stamina restored → player's turn again.
///
/// Multiplayer: replace with MultiplayerTurnSystem (same events, different impl).
/// The GameManager.IsMultiplayer flag tells UI which system to subscribe to.
/// </summary>
public class TurnSystem : MonoBehaviour
{
    public static TurnSystem Instance { get; private set; }

    public event EventHandler OnTurnChanged;
    public event Action OnPlayerTurnBegin;
    public event Action OnEnemyPhaseBegin;
    public event Action OnEnemyPhaseEnd;

    private int  turnNumber  = 1;
    private bool playerTurn  = true;

    public bool IsPlayerTurn => playerTurn;
    public int  TurnNumber   => turnNumber;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnsComplete += HandleEnemyTurnsComplete;
    }

    private void OnDestroy()
    {
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnsComplete -= HandleEnemyTurnsComplete;
    }

    // ── Public ─────────────────────────────────────────────────────────────

    public void NextTurn()
    {
        if (!playerTurn) return;

        Debug.LogWarning("[TurnSystem] NextTurn clicked. Checking stamina before enemy phase.");

        Unit selectedUnit = UnitActionSystem.Instance != null
            ? UnitActionSystem.Instance.GetSelectedUnit()
            : null;

        if (selectedUnit != null)
        {
            PlayerStats stats = selectedUnit.GetComponent<PlayerStats>();
            if (stats != null)
                Debug.LogWarning($"[TurnSystem] BEFORE enemy phase stamina: {stats.currentStamina}/{stats.maxStamina}", stats);
        }

        playerTurn = false;
        turnNumber++;
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        BeginEnemyPhase();
    }

    /// <summary>Called on room transition to bypass the enemy phase entirely.</summary>
    public void ForcePlayerTurn()
    {
        playerTurn = true;
        OnEnemyPhaseEnd?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        OnPlayerTurnBegin?.Invoke();
        Debug.Log("[TurnSystem] Player turn forced.");
    }

    public int GetTrunNumber() => turnNumber;   // keep old typo for UI compat

    // ── Private ────────────────────────────────────────────────────────────

    private void BeginEnemyPhase()
    {
        Debug.Log("[TurnSystem] Enemy phase begins.");
        OnEnemyPhaseBegin?.Invoke();

        if (EnemyManager.Instance != null && EnemyManager.Instance.GetEnemyCount() > 0)
            EnemyManager.Instance.RunEnemyTurns();
        else
            HandleEnemyTurnsComplete();
    }

    private void HandleEnemyTurnsComplete()
    {
        playerTurn = true;

        RecoverPlayerStaminaForNewTurn();

        OnEnemyPhaseEnd?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        OnPlayerTurnBegin?.Invoke();

        Debug.Log($"[TurnSystem] Player turn {turnNumber} begins.");
    }

    private void RecoverPlayerStaminaForNewTurn()
    {
        Unit selectedUnit = UnitActionSystem.Instance != null
            ? UnitActionSystem.Instance.GetSelectedUnit()
            : null;

        if (selectedUnit == null)
            return;

        PlayerStats stats = selectedUnit.GetComponent<PlayerStats>();

        if (stats == null)
            return;

        int recovered = stats.RollStaminaRecovery();

        Debug.Log($"[TurnSystem] Recovered {recovered} stamina at start of player turn.");
    }
}
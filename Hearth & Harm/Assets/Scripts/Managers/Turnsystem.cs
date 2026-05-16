using System;
using UnityEngine;

public class TurnSystem : MonoBehaviour
{
    public static TurnSystem Instance { get; private set; }

    public event EventHandler OnTurnChanged;
    public event Action OnPlayerTurnBegin;
    public event Action OnEnemyPhaseBegin;
    public event Action OnEnemyPhaseEnd;

    private int  turnNumber = 1;
    private bool playerTurn = true;

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

        playerTurn = false;
        turnNumber++;
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        BeginEnemyPhase();
    }

    public void ForcePlayerTurn()
    {
        playerTurn = true;
        InvalidateMoveCache();
        OnEnemyPhaseEnd?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        OnPlayerTurnBegin?.Invoke();
        Debug.Log("[TurnSystem] Player turn forced.");
    }

    public int GetTrunNumber() => turnNumber;

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

        RecoverPlayerStamina();

        InvalidateMoveCache();

        OnEnemyPhaseEnd?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        OnPlayerTurnBegin?.Invoke();

        Debug.Log($"[TurnSystem] Player turn {turnNumber} begins.");
    }

    private static void RecoverPlayerStamina()
    {
        var unit = UnitActionSystem.Instance?.GetSelectedUnit();
        if (unit == null) return;

        var stats = unit.GetComponent<PlayerStats>();
        if (stats == null) return;

        int recovered = stats.RollStaminaRecovery();
        Debug.Log($"[TurnSystem] Recovered {recovered} stamina.");
    }

    private static void InvalidateMoveCache()
    {
        var unit = UnitActionSystem.Instance?.GetSelectedUnit();
        if (unit == null) return;

        var move = unit.GetMoveAction();
        move?.InvalidateCache();
    }
}
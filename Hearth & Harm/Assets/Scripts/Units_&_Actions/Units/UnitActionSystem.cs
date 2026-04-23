using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Central input handler for unit selection and action execution.
/// Supports both single-player and multiplayer (NGO).
///
/// SP:  Selects the unit immediately after LevelGenerator.OnLevelReady fires.
/// MP:  Polls for the locally-owned NetworkObject for up to ~4 seconds, then
///      falls back to per-frame polling so late-spawned players are never missed.
///
///      Every frame in MP we also reconcile RoomManager against the bridge's
///      replicated currentRoomName so the TilemapHighlighter and HandleInput
///      always have the correct room even after a room transition.
/// </summary>
public class UnitActionSystem : MonoBehaviour
{
    public static UnitActionSystem Instance { get; private set; }

    [Header("Selection — layer that contains unit colliders")]
    [SerializeField] private LayerMask unitLayerMask;

    public event EventHandler       OnSelectedUnitChange;
    public event EventHandler       OnSelectedActionChange;
    public event EventHandler<bool> OnBusyChanged;

    private Unit               selectedUnit;
    private BaseAction         selectedAction;
    private bool               isBusy;
    private EnemyUnit          hoveredEnemy;
    private NetworkedPlayerBridge localBridge; // cached after unit is found

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()  => LevelGenerator.OnLevelReady += OnLevelReady;
    private void OnDisable() => LevelGenerator.OnLevelReady -= OnLevelReady;

    // ── Level ready ────────────────────────────────────────────────────────

    private void OnLevelReady()
    {
        selectedUnit   = null;
        selectedAction = null;
        localBridge    = null;
        StartCoroutine(FindAndSelectUnit());
    }

    /// <summary>
    /// Polls for a selectable unit after the level generates.
    /// SP:  finds the unit in 1-2 frames (spawned synchronously by LevelGenerator).
    /// MP:  waits up to ~4 seconds for the host to spawn and replicate our object.
    /// </summary>
    private IEnumerator FindAndSelectUnit()
    {
        int maxAttempts = GameManager.IsMultiplayer ? 240 : 10;

        for (int attempt = 0; attempt < maxAttempts && selectedUnit == null; attempt++)
        {
            yield return null;
            TrySelectOwnedUnit();
        }

        if (selectedUnit == null)
            Debug.LogWarning("[UnitActionSystem] Could not find a unit to select after waiting.");
        else
            Debug.Log($"[UnitActionSystem] Selected unit: {selectedUnit.name}");
    }

    // ── Update ─────────────────────────────────────────────────────────────

    private void Update()
    {
        // Keep trying until we have a unit (handles late MP spawns)
        if (selectedUnit == null)
        {
            TrySelectOwnedUnit();
            return;
        }

        // In MP, keep RoomManager reconciled with where our player actually is.
        // This runs every frame but is cheap — it bails out immediately if the
        // room name hasn't changed since the last check.
        if (GameManager.IsMultiplayer && localBridge != null)
            ReconcileRoomFromBridge();

        if (isBusy) return;

        // Respect turn system
        if (GameManager.IsMultiplayer)
        {
            if (NetworkedTurnSystem.Instance != null && !NetworkedTurnSystem.Instance.IsPlayerPhase)
                return;
        }
        else
        {
            if (TurnSystem.Instance != null && !TurnSystem.Instance.IsPlayerTurn)
                return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        HandleInput();
    }

    // ── Unit selection ─────────────────────────────────────────────────────

    /// <summary>
    /// SP:  selects any Unit in the scene.
    /// MP:  selects only the NetworkObject this peer owns.
    /// </summary>
    private void TrySelectOwnedUnit()
    {
        foreach (var u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            if (GameManager.IsMultiplayer)
            {
                var netObj = u.GetComponent<Unity.Netcode.NetworkObject>();
                if (netObj == null || !netObj.IsOwner) continue;
            }

            SetSelectedUnit(u);

            if (GameManager.IsMultiplayer)
            {
                localBridge = u.GetComponent<NetworkedPlayerBridge>();
                ReconcileRoomFromBridge();
            }

            Debug.Log($"[UnitActionSystem] Selected unit: {u.name}");
            return;
        }
    }

    private void SetSelectedUnit(Unit unit)
    {
        selectedUnit = unit;
        SetSelectedAction(unit.GetMoveAction());
        OnSelectedUnitChange?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelectedAction(BaseAction action)
    {
        selectedAction = action;
        OnSelectedActionChange?.Invoke(this, EventArgs.Empty);
    }

    // ── Room reconciliation ────────────────────────────────────────────────

    /// <summary>
    /// Reads the bridge's replicated currentRoomName and updates RoomManager
    /// if it's out of date. Called every frame in MP so the highlighter and
    /// input handling always paint/click the correct tilemap, including
    /// immediately after the player transitions to a new room.
    ///
    /// Matching by room instance name (not IsValidGridPosition) is exact —
    /// grid coords like (3,4) are valid in almost every room, but a name like
    /// "StartRoom_(0,0)" is unique.
    /// </summary>
    private void ReconcileRoomFromBridge()
    {
        if (localBridge == null) return;

        string bridgeRoom = localBridge.GetCurrentRoomName();
        if (string.IsNullOrEmpty(bridgeRoom)) return;

        // Already pointing at the right room — nothing to do
        var current = RoomManager.Instance?.GetCurrentRoom();
        if (current?.roomInstance != null && current.roomInstance.name == bridgeRoom) return;

        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return;

        foreach (var placed in gen.GetAllRooms())
        {
            if (placed.roomInstance == null) continue;
            if (placed.roomInstance.name != bridgeRoom) continue;

            RoomManager.Instance?.SetCurrentRoom(placed);
            Debug.Log($"[UnitActionSystem] Room reconciled → {bridgeRoom}");
            return;
        }
    }

    // ── Input handling ─────────────────────────────────────────────────────

    private void HandleInput()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        var room = RoomManager.Instance?.GetCurrentRoomGrid();
        if (room == null) return;

        Vector3      mouseWorld = MouseWorld2D.GetPosition();
        GridPosition mouseGP    = room.GetGridPosition(mouseWorld);

        // Enemy click
        EnemyUnit clickedEnemy = room.GetEnemyAtGridPosition(mouseGP);
        if (clickedEnemy != null && selectedAction is CombatAction ca)
        {
            if (ca.CanAfford() && ca.IsValidTarget(mouseGP))
            {
                SetBusy();
                PerformAttack(ca, mouseGP, clickedEnemy);
            }
            else
            {
                SelectEnemy(clickedEnemy);
            }
            return;
        }

        // Move / attack on grid
        switch (selectedAction)
        {
            case MoveAction move when move.IsValidTarget(mouseGP):
                SetBusy();
                PerformMove(move, mouseGP);
                break;

            case CombatAction combat when combat.CanAfford() && combat.IsValidTarget(mouseGP):
                SetBusy();
                PerformAttack(combat, mouseGP, null);
                break;
        }
    }

    // ── Action execution ───────────────────────────────────────────────────

    private void PerformMove(MoveAction move, GridPosition targetGP)
    {
        // MoveAction handles local movement; NetworkedPlayerBridge syncs to peers
        move.Move(targetGP, ClearBusy);
    }

    private void PerformAttack(CombatAction combat, GridPosition targetGP, EnemyUnit directTarget)
    {
        if (GameManager.IsMultiplayer)
            combat.PerformAttackNetworked(targetGP, ClearBusy);
        else
            combat.PerformAttack(targetGP, ClearBusy);
    }

    // ── Enemy selection ────────────────────────────────────────────────────

    private void SelectEnemy(EnemyUnit enemy)
    {
        if (hoveredEnemy != null) hoveredEnemy.SetSelected(false);
        hoveredEnemy = enemy;
        hoveredEnemy?.SetSelected(true);

        var hc = enemy?.GetComponent<HealthComponent>();
        EnemyHealthUI.Instance?.SetTarget(hc);
    }

    // ── Busy state ─────────────────────────────────────────────────────────

    private void SetBusy()   { isBusy = true;  OnBusyChanged?.Invoke(this, true);  }
    private void ClearBusy() { isBusy = false; OnBusyChanged?.Invoke(this, false); }

    // ── Public getters ─────────────────────────────────────────────────────

    public Unit        GetSelectedUnit()   => selectedUnit;
    public BaseAction  GetSelectedAction() => selectedAction;
}
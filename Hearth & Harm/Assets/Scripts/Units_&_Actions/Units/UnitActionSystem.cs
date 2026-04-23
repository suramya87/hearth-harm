using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Central input handler for unit selection and action execution.
/// Supports both single-player and multiplayer (NGO).
///
/// SP:  Selects the unit immediately after LevelGenerator.OnLevelReady fires.
/// MP:  Polls for the locally-owned NetworkObject for up to ~4 seconds after
///      level ready, then falls back to per-frame polling in Update so a
///      late-spawned player object is never missed.
/// </summary>
public class UnitActionSystem : MonoBehaviour
{
    public static UnitActionSystem Instance { get; private set; }

    [Header("Selection — layer that contains unit colliders")]
    [SerializeField] private LayerMask unitLayerMask;

    public event EventHandler       OnSelectedUnitChange;
    public event EventHandler       OnSelectedActionChange;
    public event EventHandler<bool> OnBusyChanged;

    private Unit      selectedUnit;
    private BaseAction selectedAction;
    private bool      isBusy;
    private EnemyUnit hoveredEnemy;

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
        // Keep trying until we have a unit (handles late MP spawns after coroutine gives up)
        if (selectedUnit == null)
        {
            TrySelectOwnedUnit();
            return;
        }

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
    /// Finds and selects the correct unit for this peer.
    /// SP:  any Unit in the scene.
    /// MP:  only the NetworkObject we own.
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

            // Once we have our unit, make sure the room is set for the highlighter
            if (GameManager.IsMultiplayer)
            {
                var bridge = u.GetComponent<NetworkedPlayerBridge>();
                if (bridge != null) TrySetRoomFromPlayerBridge(bridge);
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

    // ── Room resolution ────────────────────────────────────────────────────

    /// <summary>
    /// Uses the bridge's replicated grid position to find which PlacedRoom
    /// the local player is in, then tells RoomManager about it.
    /// This fixes the client-side case where SetStartRoomClientRpc used
    /// roomInstance.name (which can differ across peers).
    /// </summary>
    private void TrySetRoomFromPlayerBridge(NetworkedPlayerBridge bridge)
    {
        if (RoomManager.Instance?.GetCurrentRoom() != null) return; // already set

        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return;

        GridPosition pos = bridge.GetNetworkGridPosition();

        foreach (var placed in gen.GetAllRooms())
        {
            if (placed.roomGrid == null || !placed.roomGrid.IsInitialized()) continue;
            if (!placed.roomGrid.IsValidGridPosition(pos)) continue;

            RoomManager.Instance?.SetCurrentRoom(placed);
            Debug.Log($"[UnitActionSystem] Room set from player bridge position {pos}");
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
        // MoveAction handles local movement; NetworkedPlayerBridge syncs position to peers
        move.Move(targetGP, ClearBusy);
    }

    private void PerformAttack(CombatAction combat, GridPosition targetGP, EnemyUnit directTarget)
    {
        if (GameManager.IsMultiplayer)
            combat.PerformAttackNetworked(targetGP, ClearBusy);
        else
            combat.PerformAttack(targetGP, ClearBusy);
    }

    // ── Enemy hover / selection ────────────────────────────────────────────

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
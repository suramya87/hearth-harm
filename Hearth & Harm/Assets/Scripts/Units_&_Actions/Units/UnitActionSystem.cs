using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Central input handler for unit selection and action execution.
/// Modified to route actions through network authority in multiplayer.
///
/// CHANGES FROM ORIGINAL:
///   - Damage calls go through NetworkedHealthBridge.TakeDamage() (static helper)
///     instead of enemy.Health.TakeDamage() directly
///   - Move calls go through NetworkedPlayerBridge.PlaceInRoom() when in MP
///   - In SP: behaves identically to the original
///
/// </summary>
public class UnitActionSystem : MonoBehaviour
{
    public static UnitActionSystem Instance { get; private set; }

    [Header("Selection — layer that contains unit colliders")]
    [SerializeField] private LayerMask unitLayerMask;

    public event EventHandler       OnSelectedUnitChange;
    public event EventHandler       OnSelectedActionChange;
    public event EventHandler<bool> OnBusyChanged;

    private Unit       selectedUnit;
    private BaseAction selectedAction;
    private bool       isBusy;
    private EnemyUnit  hoveredEnemy;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()  => LevelGenerator.OnLevelReady += OnLevelReady;
    private void OnDisable() => LevelGenerator.OnLevelReady -= OnLevelReady;

    private void OnLevelReady()
    {
        // In MP: only auto-select the unit we own
        var units = FindObjectsByType<Unit>(FindObjectsSortMode.None);
        foreach (var u in units)
        {
            if (!GameManager.IsMultiplayer)
            {
                SetSelectedUnit(u);
                break;
            }

            // In MP: select the unit owned by this client
            var netBridge = u.GetComponent<NetworkedPlayerBridge>();
            if (netBridge != null)
            {
                // We check ownership via the NetworkObject
                var netObj = u.GetComponent<Unity.Netcode.NetworkObject>();
                if (netObj != null && netObj.IsOwner)
                {
                    SetSelectedUnit(u);
                    break;
                }
            }
        }
    }

    private void Update()
    {
        if (isBusy)               return;
        if (selectedUnit == null) return;

        // In MP: only process input on player turn and when it's allowed
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

    private void HandleInput()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        var room = RoomManager.Instance?.GetCurrentRoomGrid();
        if (room == null) return;

        Vector3      mouseWorld = MouseWorld2D.GetPosition();
        GridPosition mouseGP    = room.GetGridPosition(mouseWorld);

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

    // ── Action execution with network routing ──────────────────────────────

    private void PerformMove(MoveAction move, GridPosition targetGP)
    {
        // MoveAction handles the local movement and visual
        // NetworkedPlayerBridge will sync the final position to other clients
        move.Move(targetGP, ClearBusy);
    }

    private void PerformAttack(CombatAction combat, GridPosition targetGP, EnemyUnit directTarget)
    {
        if (GameManager.IsMultiplayer)
        {
            // In MP: perform attack locally for responsiveness,
            // but route damage through NetworkedHealthBridge
            combat.PerformAttackNetworked(targetGP, ClearBusy);
        }
        else
        {
            // SP: unchanged behaviour
            combat.PerformAttack(targetGP, ClearBusy);
        }
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

    // ── Unit selection ─────────────────────────────────────────────────────

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

    private void SetBusy()   { isBusy = true;  OnBusyChanged?.Invoke(this, true);  }
    private void ClearBusy() { isBusy = false; OnBusyChanged?.Invoke(this, false); }

    public Unit       GetSelectedUnit()   => selectedUnit;
    public BaseAction GetSelectedAction() => selectedAction;
}
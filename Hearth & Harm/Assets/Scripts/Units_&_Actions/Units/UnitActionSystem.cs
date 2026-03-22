using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Central input handler for unit selection and action execution.
///
/// 2D CHANGES
///   - Uses MouseWorld2D.GetPosition() instead of 3D raycast.
///   - Room grid looked up from RoomManager, not LevelGrid.
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
        var unit = FindAnyObjectByType<Unit>();
        if (unit != null) SetSelectedUnit(unit);
    }

    private void Update()
    {
        if (isBusy)         return;
        if (selectedUnit == null) return;
        if (TurnSystem.Instance != null && !TurnSystem.Instance.IsPlayerTurn) return;
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

        // Check for enemy click first
        EnemyUnit clickedEnemy = room.GetEnemyAtGridPosition(mouseGP);
        if (clickedEnemy != null && selectedAction is CombatAction ca)
        {
            if (ca.CanAfford() && ca.IsValidTarget(mouseGP))
            {
                SetBusy();
                ca.PerformAttack(mouseGP, ClearBusy);
            }
            else
            {
                // Select enemy for inspection
                SelectEnemy(clickedEnemy);
            }
            return;
        }

        // Normal action dispatch
        switch (selectedAction)
        {
            case MoveAction move when move.IsValidTarget(mouseGP):
                SetBusy(); move.Move(mouseGP, ClearBusy); break;

            case CombatAction combat when combat.CanAfford() && combat.IsValidTarget(mouseGP):
                SetBusy(); combat.PerformAttack(mouseGP, ClearBusy); break;
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

    private void SetBusy()  { isBusy = true;  OnBusyChanged?.Invoke(this, true);  }
    private void ClearBusy(){ isBusy = false; OnBusyChanged?.Invoke(this, false); }

    public Unit       GetSelectedUnit()   => selectedUnit;
    public BaseAction GetSelectedAction() => selectedAction;
}
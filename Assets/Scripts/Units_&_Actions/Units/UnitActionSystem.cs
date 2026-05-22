using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

public class UnitActionSystem : MonoBehaviour
{
    public static UnitActionSystem Instance { get; private set; }

    [Header("Selection — layer that contains unit colliders")]
    [SerializeField] private LayerMask unitLayerMask;

    [Header("Dice UI")]
    [SerializeField] private DiceBoxUI diceBoxUI;

    public event EventHandler       OnSelectedUnitChange;
    public event EventHandler       OnSelectedActionChange;
    public event EventHandler<bool> OnBusyChanged;

    private Unit                  selectedUnit;
    private BaseAction            selectedAction;
    private bool                  isBusy;
    private EnemyUnit             hoveredEnemy;
    private NetworkedPlayerBridge localBridge;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        if (diceBoxUI == null)
            diceBoxUI = FindFirstObjectByType<DiceBoxUI>();
    }

    private void OnEnable()  => LevelGenerator.OnLevelReady += OnLevelReady;
    private void OnDisable() => LevelGenerator.OnLevelReady -= OnLevelReady;

    // ── Level ready ────────────────────────────────────────────────────────

    private void OnLevelReady()
    {
        selectedUnit   = null;
        selectedAction = null;
        localBridge    = null;
        isBusy         = false;
        StartCoroutine(FindAndSelectUnit());
    }

    private IEnumerator FindAndSelectUnit()
    {
        yield return null;

        if (GameManager.IsMultiplayer)
        {
            float waited = 0f;
            while (waited < 8f)
            {
                waited += Time.deltaTime;
                var units = FindObjectsByType<Unit>(FindObjectsSortMode.None);
                foreach (var u in units)
                {
                    var netObj = u.GetComponent<Unity.Netcode.NetworkObject>();
                    if (netObj != null && netObj.IsOwner)
                    {
                        SetSelectedUnit(u);
                        localBridge = u.GetComponent<NetworkedPlayerBridge>();
                        Debug.Log($"[UnitActionSystem] MP unit found: {u.name}");
                        yield break;
                    }
                }
                yield return null;
            }
            Debug.LogWarning("[UnitActionSystem] Timed out waiting for owned unit.");
            yield break;
        }

        // Single player
        int attempts = 0;
        while (selectedUnit == null && attempts < 10)
        {
            var units = FindObjectsByType<Unit>(FindObjectsSortMode.None);
            foreach (var u in units)
            {
                SetSelectedUnit(u);
                break;
            }
            attempts++;
            yield return null;
        }
    }

    // ── Update ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (selectedUnit == null)
        {
            TrySelectOwnedUnit();
            return;
        }

        if (GameManager.IsMultiplayer && localBridge != null)
            ReconcileRoomFromBridge();

        // ── Turn guard — must come before ANY input handling ───────────────
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

        // ── Busy guard ─────────────────────────────────────────────────────
        if (isBusy) return;

        // ── UI guard ───────────────────────────────────────────────────────
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        // ── Per-action input ───────────────────────────────────────────────
        if (selectedAction is MoveAction moveAction)
        {
            moveAction.HandleActionInput();
            return; 
        }

        HandleInput();
    }

    // ── Unit selection ─────────────────────────────────────────────────────

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

        unit.GetMoveAction()?.InvalidateCache();

        OnSelectedUnitChange?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelectedAction(BaseAction action, bool clearDiceForMove)
    {
        selectedAction = action;

        if (diceBoxUI != null)
        {
            if (selectedAction is CombatAction combatAction &&
                combatAction.ActionData != null &&
                combatAction.ActionData.useDiceDamage)
            {
                diceBoxUI.ShowPendingDice(combatAction.ActionData);
            }
            else if (selectedAction is MoveAction)
            {
                if (clearDiceForMove)
                    diceBoxUI.Clear();
            }
            else
            {
                diceBoxUI.Clear();
            }
        }

        OnSelectedActionChange?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelectedAction(BaseAction action) => SetSelectedAction(action, true);

    // ── Room reconciliation ────────────────────────────────────────────────

    private void ReconcileRoomFromBridge()
    {
        if (localBridge == null) return;

        string bridgeRoom = localBridge.GetCurrentRoomName();
        if (string.IsNullOrEmpty(bridgeRoom)) return;

        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return;

        LevelGenerator.PlacedRoom targetPlaced = null;
        foreach (var placed in gen.GetAllRooms())
        {
            if (placed.roomInstance == null) continue;
            if (placed.roomInstance.name != bridgeRoom) continue;
            targetPlaced = placed;
            break;
        }

        if (targetPlaced == null) return;

        var current = RoomManager.Instance?.GetCurrentRoom();
        bool roomManagerWrong = current == null ||
                                current.roomInstance == null ||
                                current.roomInstance.name != bridgeRoom;

        if (roomManagerWrong)
        {
            RoomManager.Instance?.SetCurrentRoom(targetPlaced);
            Debug.Log($"[UnitActionSystem] RoomManager reconciled → {bridgeRoom}");
        }

        if (!selectedUnit.IsInitialized())
        {
            var gp = localBridge.GetNetworkGridPosition();
            if (targetPlaced.roomGrid != null)
            {
                selectedUnit.IsSyncingFromNetwork = true;
                selectedUnit.PlaceInRoom(targetPlaced.roomGrid, gp);
                selectedUnit.IsSyncingFromNetwork = false;
                Debug.Log($"[UnitActionSystem] Unit initialized via reconcile → {bridgeRoom} {gp}");
            }
        }
    }


    private void HandleInput()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        var unitGrid = selectedUnit?.GetCurrentRoomGrid();
        if (unitGrid == null) return;

        Vector3      mouseWorld = MouseWorld2D.GetPosition();
        GridPosition mouseGP    = unitGrid.GetGridPosition(mouseWorld);

        // Combat action: check for enemy click first.
        if (selectedAction is CombatAction ca)
        {
            EnemyUnit clickedEnemy = unitGrid.GetEnemyAtGridPosition(mouseGP);
            if (clickedEnemy != null)
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

            if (ca.CanAfford() && ca.IsValidTarget(mouseGP))
            {
                SetBusy();
                PerformAttack(ca, mouseGP, null);
            }
            return;
        }
    }

    // ── Action execution ───────────────────────────────────────────────────

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

    private void SetBusy()
    {
        isBusy = true;
        OnBusyChanged?.Invoke(this, true);
    }

    private void ClearBusy()
    {
        isBusy = false;

        if (selectedAction != null)
            SetSelectedAction(selectedAction);

        OnBusyChanged?.Invoke(this, false);
    }

    // ── Public getters ─────────────────────────────────────────────────────

    public Unit       GetSelectedUnit()   => selectedUnit;
    public BaseAction GetSelectedAction() => selectedAction;
}
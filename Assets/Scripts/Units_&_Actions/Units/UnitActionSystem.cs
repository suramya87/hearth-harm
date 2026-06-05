using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Manages the currently selected unit and action.
/// In multiplayer each client only controls its own owned Unit.
/// </summary>
public class UnitActionSystem : MonoBehaviour
{
    public static UnitActionSystem Instance { get; private set; }

    public event EventHandler       OnSelectedUnitChanged;
    public event EventHandler       OnSelectedActionChanged;
    public event EventHandler<bool> OnBusyChanged;
    public event EventHandler       OnActionStarted;

    [SerializeField] private Unit      selectedUnit;
    [SerializeField] private LayerMask unitLayerMask;

    private BaseAction selectedAction;
    private bool       isBusy;

    public bool IsBusy => isBusy;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (!GameManager.IsMultiplayer)
        {
            var unit = selectedUnit != null ? selectedUnit : FindAnyObjectByType<Unit>();
            SetSelectedUnit(unit);
        }
        else
        {
            StartCoroutine(FindOwnedUnitCoroutine());
        }
    }

    private IEnumerator FindOwnedUnitCoroutine()
    {
        float timeout = 30f;
        float elapsed = 0f;
        Unit  owned   = null;

        while (owned == null && elapsed < timeout)
        {
            owned = FindLocalOwnedUnit();
            if (owned == null)
            {
                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }
        }

        if (owned != null)
        {
            SetSelectedUnit(owned);
            Debug.Log($"[UnitActionSystem] Coroutine found owned unit: {owned.name}");
        }
        else
        {
            Debug.LogWarning("[UnitActionSystem] Timed out waiting for owned unit.");
        }
    }

    private void Update()
    {
        if (isBusy) return;
        if (!IsLocalPlayerTurn()) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        if (GameManager.IsMultiplayer)
        {
            if (selectedUnit == null || !IsOwnedByLocalPlayer(selectedUnit))
            {
                var owned = FindLocalOwnedUnit();
                if (owned != null && owned != selectedUnit)
                {
                    Debug.Log($"[UnitActionSystem] Auto-correcting selected unit to {owned.name}");
                    SetSelectedUnit(owned);
                }
                if (selectedUnit == null) return;
            }
        }

        if (selectedAction is MoveAction moveAction)
            moveAction.HandleActionInput();
        else if (selectedAction is CombatAction combatAction)
            combatAction.HandleActionInput();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void SetSelectedUnit(Unit unit)
    {
        if (GameManager.IsMultiplayer && unit != null && !IsOwnedByLocalPlayer(unit))
        {
            Debug.LogWarning($"[UnitActionSystem] Refused non-owned unit {unit.name}.");
            return;
        }

        selectedUnit = unit;

        if (selectedUnit != null)
        {
            var move = selectedUnit.GetMoveAction();
            if (move != null) SetSelectedAction(move, notify: false);
        }

        Debug.Log($"[UnitActionSystem] Selected unit: {(unit != null ? unit.name : "null")}");
        OnSelectedUnitChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelectedAction(BaseAction action, bool notify = true)
    {
        selectedAction = action;
        if (notify) OnSelectedActionChanged?.Invoke(this, EventArgs.Empty);
    }

    public Unit       GetSelectedUnit()   => selectedUnit;
    public BaseAction GetSelectedAction() => selectedAction;

    public void TakeAction(BaseAction action, Action onComplete)
    {
        if (!IsLocalPlayerTurn())
        {
            Debug.LogWarning("[UnitActionSystem] TakeAction blocked — not player turn.");
            return;
        }
        if (GameManager.IsMultiplayer && !IsOwnedByLocalPlayer(action.GetUnit()))
        {
            Debug.LogWarning("[UnitActionSystem] TakeAction blocked — not our unit.");
            return;
        }

        SetBusy();
        OnActionStarted?.Invoke(this, EventArgs.Empty);
        action.TakeAction(() =>
        {
            ClearBusy();
            onComplete?.Invoke();
        });
    }

    private void SetBusy()
    {
        isBusy = true;
        OnBusyChanged?.Invoke(this, true);
    }

    private void ClearBusy()
    {
        isBusy = false;
        OnBusyChanged?.Invoke(this, false);
    }

    // ── Ownership helpers ──────────────────────────────────────────────────

    public static Unit FindLocalOwnedUnit()
    {
        if (!GameManager.IsMultiplayer)
            return FindAnyObjectByType<Unit>();

        foreach (var u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
            if (IsOwnedByLocalPlayer(u)) return u;

        return null;
    }

    public static bool IsOwnedByLocalPlayer(Unit unit)
    {
        if (unit == null) return false;
        if (!GameManager.IsMultiplayer) return true;
        var netObj = unit.GetComponent<Unity.Netcode.NetworkObject>();
        return netObj != null && netObj.IsOwner;
    }

    // ── Turn helpers ────────────────────────────────────────────────────────

    public static bool IsLocalPlayerTurn()
    {
        if (!GameManager.IsMultiplayer)
            return TurnSystem.Instance == null || TurnSystem.Instance.IsPlayerTurn;
        return NetworkedTurnSystem.Instance == null ||
               NetworkedTurnSystem.Instance.IsPlayerPhase;
    }
}
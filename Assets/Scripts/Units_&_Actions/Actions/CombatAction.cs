using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class CombatAction : BaseAction
{
    [Header("Data")]
    [SerializeField] private CombatActionData actionData;

    [Header("Facing correction (0-3 x 90 CCW)")]
    [Range(0, 3)]
    [SerializeField] private int facingRotationSteps = 1;

    [Header("Optional dice box UI")]
    [SerializeField] private DiceBoxUI diceBox;

    [Header("Damage Numbers")]
    [SerializeField] private GameObject damageNumberPrefab;

    private Vector2Int         currentFacing = new(0, 1);
    private List<GridPosition> lastPreview   = new();

    // Pending target set by HandleActionInput, consumed by TakeAction.
    private GridPosition pendingTargetGP;
    private bool         hasPendingTarget;

    public CombatActionData ActionData           => actionData;
    public void SetActionData(CombatActionData d) => actionData = d;

    public override string GetActionName() =>
        actionData != null ? actionData.actionName : "Attack";

    private void Start()
    {
        if (diceBox == null)
            diceBox = FindFirstObjectByType<DiceBoxUI>();
    }


    public void HandleActionInput()
    {
        if (!CanExecuteLocally()) return;
        if (!Input.GetMouseButtonDown(0)) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        var room = unit.GetCurrentRoomGrid();
        if (room == null) return;

        Vector3 raw      = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector3 mouse    = new Vector3(raw.x, raw.y, 0f);
        GridPosition gp  = room.GetGridPosition(mouse);

        if (!IsValidTarget(gp)) return;

        pendingTargetGP  = gp;
        hasPendingTarget = true;

        UnitActionSystem.Instance?.TakeAction(this, () =>
        {
            var move = unit.GetMoveAction();
            if (move != null)
                UnitActionSystem.Instance?.SetSelectedAction(move);
        });
    }

    public override void TakeAction(Action onComplete)
    {
        if (!hasPendingTarget) { onComplete?.Invoke(); return; }
        hasPendingTarget = false;
        PerformAttack(pendingTargetGP, onComplete);
    }

    // ---- Preview ----

    public List<GridPosition> GetPreviewPositions(GridPosition mouseGP)
    {
        if (actionData == null) return new();
        var unitGP = unit.GetGridPosition();
        if (actionData.rotatesToFacing)
            currentFacing = ApplyCorrection(FacingToward(unitGP, mouseGP));

        List<GridPosition> positions;
        if (IsRanged())
            positions = InRange(unitGP, mouseGP) ? PatternAt(mouseGP, currentFacing) : new();
        else
            positions = PatternAt(unitGP, currentFacing);

        lastPreview = positions;
        return positions;
    }

    // ---- Execution ----

    public void PerformAttack(GridPosition targetGP, Action onComplete)
    {
        if (!CanExecuteLocally()) { onComplete?.Invoke(); return; }
        StartCoroutine(ExecuteAttackFlowRoutine(targetGP, onComplete));
    }

    public void PerformAttackNetworked(GridPosition targetGP, Action onComplete)
    {
        if (!CanExecuteLocally()) { onComplete?.Invoke(); return; }
        StartCoroutine(ExecuteAttackFlowRoutine(targetGP, onComplete));
    }

    private IEnumerator ExecuteAttackFlowRoutine(GridPosition targetGP, Action onComplete)
    {
        if (actionData == null) { onComplete?.Invoke(); yield break; }

        onActionComplete = onComplete;
        isActive         = true;

        var unitGP = unit.GetGridPosition();

        if (actionData.rotatesToFacing)
            currentFacing = ApplyCorrection(FacingToward(unitGP, targetGP));

        unitAnimator?.SetFacing(currentFacing);
        unitAnimator?.TriggerAttack();

        var hitPositions = IsRanged()
            ? PatternAt(targetGP, currentFacing)
            : PatternAt(unitGP,   currentFacing);

        AttackSpritePopup.ShowOnTiles(actionData, hitPositions);
        SpendStamina();

        int  finalDamage  = 0;
        bool diceFinished = false;

        if (diceBox != null && actionData.useDiceDamage)
        {
            int strengthBonus  = playerStats != null ? playerStats.strength : 0;
            int totalFlatBonus = actionData.flatBonus + strengthBonus;

            yield return diceBox.PlayPhysicsD6Roll(actionData.diceCount, totalFlatBonus, result =>
            {
                finalDamage  = Mathf.Max(1, Mathf.RoundToInt(result * actionData.damageMultiplier));
                diceFinished = true;
            });
        }
        else
        {
            finalDamage  = CalculateDamage(RollDamageDice());
            diceFinished = true;
        }

        while (!diceFinished) yield return null;

        if (GameManager.IsMultiplayer)
            ApplyDamageNetworked(hitPositions, finalDamage);
        else
            ApplyDamage(hitPositions, finalDamage);

        playerAnimator?.RefreshStaminaState();

        isActive = false;
        onActionComplete?.Invoke();
    }

    // ---- Damage application ----

    private void ApplyDamage(List<GridPosition> positions, int dmg)
    {
        var room = unit.GetCurrentRoomGrid();
        if (room == null) return;

        var hitEnemies = new HashSet<EnemyUnit>();
        var hitUnits   = new HashSet<Unit>();

        foreach (var pos in positions)
        {
            if (!room.IsValidGridPosition(pos)) continue;

            foreach (var enemy in room.GetEnemiesAtGridPosition(pos))
            {
                if (enemy == null || enemy.IsDead || !hitEnemies.Add(enemy)) continue;
                var interceptor = enemy.GetComponent<BossDamageInterceptor>();
                if (interceptor != null) interceptor.TakeDamage(dmg);
                else                     enemy.Health.TakeDamage(dmg);
                DamageNumber.Spawn(damageNumberPrefab, enemy.transform.position, dmg);
            }

            foreach (var target in room.GetUnitsAtGridPosition(pos))
            {
                if (target == unit && !actionData.canTargetSelf) continue;
                if (!hitUnits.Add(target)) continue;
                target.GetComponent<HealthComponent>()?.TakeDamage(dmg);
                DamageNumber.Spawn(damageNumberPrefab, target.transform.position, dmg);
            }
        }
    }

    private void ApplyDamageNetworked(List<GridPosition> positions, int dmg)
    {
        var room = unit.GetCurrentRoomGrid();
        if (room == null) return;

        var hitEnemies = new HashSet<EnemyUnit>();
        var hitUnits   = new HashSet<Unit>();

        foreach (var pos in positions)
        {
            if (!room.IsValidGridPosition(pos)) continue;

            foreach (var enemy in room.GetEnemiesAtGridPosition(pos))
            {
                if (enemy == null || enemy.IsDead || !hitEnemies.Add(enemy)) continue;
                var interceptor = enemy.GetComponent<BossDamageInterceptor>();
                if (interceptor != null) interceptor.TakeDamage(dmg);
                else                     NetworkedHealthBridge.TakeDamage(enemy.gameObject, dmg);
            }

            foreach (var target in room.GetUnitsAtGridPosition(pos))
            {
                if (target == unit && !actionData.canTargetSelf) continue;
                if (!hitUnits.Add(target)) continue;
                NetworkedHealthBridge.TakeDamage(target.gameObject, dmg);
            }
        }
    }

    // ---- Valid targets ----

    public List<GridPosition> GetValidActionGridPositionList()
    {
        var valid = new List<GridPosition>();
        if (actionData == null) return valid;

        var room   = unit.GetCurrentRoomGrid();
        if (room == null) return valid;

        var unitGP = unit.GetGridPosition();

        if (IsRanged())
        {
            for (int dx = -actionData.maxRange; dx <= actionData.maxRange; dx++)
            for (int dy = -actionData.maxRange; dy <= actionData.maxRange; dy++)
            {
                int d = Mathf.Abs(dx) + Mathf.Abs(dy);
                if (d < actionData.minRange || d > actionData.maxRange) continue;
                if (d == 0 && !actionData.canTargetSelf) continue;
                var c = new GridPosition(unitGP.x + dx, unitGP.y + dy);
                if (room.IsValidGridPosition(c)) valid.Add(c);
            }
        }
        else
        {
            foreach (var facing in Cardinals())
            foreach (var gp in PatternAt(unitGP, facing))
                if (room.IsValidGridPosition(gp) && !valid.Contains(gp))
                    valid.Add(gp);
        }

        return valid;
    }

    public bool IsValidTarget(GridPosition gp) =>
        GetValidActionGridPositionList().Contains(gp);

    public bool CanAfford()
    {
        if (actionData == null) return false;
        if (!actionData.requiresEnoughStamina) return true;
        if (playerStats == null) return false;
        return playerStats.currentStamina >= actionData.staminaCost;
    }

    // ---- Helpers ----

    private void SpendStamina()
    {
        if (playerStats == null) return;
        playerStats.currentStamina = Mathf.Max(0, playerStats.currentStamina - actionData.staminaCost);
    }

    private List<GridPosition> PatternAt(GridPosition origin, Vector2Int facing) =>
        actionData.attackPattern != null
            ? actionData.attackPattern.GetAffectedPositions(origin, facing)
            : new List<GridPosition> { origin };

    private bool IsRanged() => actionData != null && actionData.maxRange > 0;

    private bool InRange(GridPosition from, GridPosition to)
    {
        int d = Mathf.Abs(from.x - to.x) + Mathf.Abs(from.y - to.y);
        return d >= actionData.minRange && d <= actionData.maxRange;
    }

    private Vector2Int ApplyCorrection(Vector2Int f)
    {
        for (int i = 0; i < facingRotationSteps; i++) f = new(-f.y, f.x);
        return f;
    }

    private static Vector2Int FacingToward(GridPosition from, GridPosition to)
    {
        int dx = to.x - from.x, dy = to.y - from.y;
        return Mathf.Abs(dy) > Mathf.Abs(dx)
            ? (dy >= 0 ? new(0, 1) : new(0, -1))
            : (dx >= 0 ? new(1, 0) : new(-1, 0));
    }

    private static readonly Vector2Int[] _cardinals =
        { new(0,1), new(0,-1), new(1,0), new(-1,0) };
    private static IEnumerable<Vector2Int> Cardinals() => _cardinals;

    private List<int> RollDamageDice()
    {
        if (!actionData.useDiceDamage) return new List<int> { actionData.baseDamage };
        if (actionData.diceCount <= 0) return new List<int>();
        return DiceRoller.RollMultiple(actionData.dieType, actionData.diceCount);
    }

    private int CalculateDamage(List<int> rolls)
    {
        int strengthBonus = playerStats != null ? playerStats.strength : 0;
        int total = actionData.useDiceDamage
            ? actionData.flatBonus + strengthBonus
            : actionData.baseDamage + strengthBonus;

        if (actionData.useDiceDamage)
            foreach (int r in rolls) total += r;

        return Mathf.Max(1, Mathf.RoundToInt(total * actionData.damageMultiplier));
    }
}
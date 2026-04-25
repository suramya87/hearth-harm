using System;
using System.Collections.Generic;
using UnityEngine;

public class CombatAction : BaseAction
{
    [Header("Data")]
    [SerializeField] private CombatActionData actionData;

    [Header("Facing correction (0–3 × 90° CCW)")]
    [Range(0, 3)]
    [SerializeField] private int facingRotationSteps = 1;

    [Header("Optional dice box UI")]
    [SerializeField] private DiceBoxUI diceBox;

    private Vector2Int         currentFacing = new(0, 1);
    private List<GridPosition> lastPreview   = new();

    public CombatActionData ActionData     => actionData;
    public void SetActionData(CombatActionData d) => actionData = d;

    public override string GetActionName() =>
        actionData != null ? actionData.actionName : "Attack";

    private void Start()
    {
        if (diceBox == null) diceBox = FindAnyObjectByType<DiceBoxUI>();
    }

    // ── Preview ────────────────────────────────────────────────────────────

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

    // ── Execution ──────────────────────────────────────────────────────────

    /// <summary>
    /// Main entry point. Automatically routes damage through NetworkedHealthBridge
    /// when running in multiplayer — no need to call PerformAttackNetworked separately.
    /// </summary>
    public void PerformAttack(GridPosition targetGP, Action onComplete)
    {
        ExecuteAttackFlow(targetGP, onComplete);
    }

    /// <summary>Explicit networked variant kept for API compatibility.</summary>
    public void PerformAttackNetworked(GridPosition targetGP, Action onComplete)
    {
        ExecuteAttackFlow(targetGP, onComplete);
    }

    private void ExecuteAttackFlow(GridPosition targetGP, Action onComplete)
    {
        if (actionData == null) { onComplete?.Invoke(); return; }

        onActionComplete = onComplete;
        isActive = true;

        var unitGP = unit.GetGridPosition();
        if (actionData.rotatesToFacing)
            currentFacing = ApplyCorrection(FacingToward(unitGP, targetGP));

        unitAnimator?.SetFacing(currentFacing);
        unitAnimator?.TriggerAttack();

        var hitPositions = IsRanged()
            ? PatternAt(targetGP, currentFacing)
            : PatternAt(unitGP, currentFacing);

        AttackSpritePopup.ShowOnTiles(actionData, hitPositions);

        SpendStamina();

        // Multiplayer: server is authoritative for damage — route through bridge.
        // Single-player: apply directly.
        if (GameManager.IsMultiplayer)
            ApplyDamageNetworked(hitPositions);
        else
            ApplyDamage(hitPositions);

        playerAnimator?.RefreshStaminaState();

        isActive = false;
        SelectMoveAction();
        onActionComplete?.Invoke();
    }

    // ── Damage Application ─────────────────────────────────────────────────

    private void ApplyDamage(List<GridPosition> positions)
    {
        var room = unit.GetCurrentRoomGrid();
        if (room == null) return;

        int dmg = RollDamage();

        foreach (var pos in positions)
        {
            if (!room.IsValidGridPosition(pos)) continue;

            foreach (var enemy in room.GetEnemiesAtGridPosition(pos))
            {
                if (enemy == null || enemy.IsDead) continue;
                enemy.Health.TakeDamage(dmg);
            }

            foreach (var target in room.GetUnitsAtGridPosition(pos))
            {
                if (target == unit && !actionData.canTargetSelf) continue;
                target.GetComponent<HealthComponent>()?.TakeDamage(dmg);
            }
        }
    }

    private void ApplyDamageNetworked(List<GridPosition> positions)
    {
        var room = unit.GetCurrentRoomGrid();
        if (room == null) return;

        int dmg = RollDamage();

        foreach (var pos in positions)
        {
            if (!room.IsValidGridPosition(pos)) continue;

            foreach (var enemy in room.GetEnemiesAtGridPosition(pos))
            {
                if (enemy == null || enemy.IsDead) continue;
                NetworkedHealthBridge.TakeDamage(enemy.gameObject, dmg);
            }

            foreach (var target in room.GetUnitsAtGridPosition(pos))
            {
                if (target == unit && !actionData.canTargetSelf) continue;
                NetworkedHealthBridge.TakeDamage(target.gameObject, dmg);
            }
        }
    }

    // ── Valid targets ──────────────────────────────────────────────────────

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

    public bool CanAfford() =>
        playerStats == null ||
        !actionData.requiresEnoughStamina ||
        playerStats.currentStamina >= actionData.staminaCost;

    // ── Helpers ────────────────────────────────────────────────────────────

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

    private static readonly Vector2Int[] _cardinals = { new(0,1), new(0,-1), new(1,0), new(-1,0) };
    private static IEnumerable<Vector2Int> Cardinals() => _cardinals;

    private int RollDamage()
    {
        if (!actionData.useDiceDamage) return actionData.baseDamage;
        if (actionData.diceCount <= 0) return actionData.flatBonus;

        var rolls = DiceRoller.RollMultiple(actionData.dieType, actionData.diceCount);
        int total = actionData.flatBonus;
        foreach (int r in rolls) total += r;

        diceBox?.Clear();
        diceBox?.ShowRoll(rolls, actionData.flatBonus);

        return Mathf.Max(1, Mathf.RoundToInt(total * actionData.damageMultiplier));
    }

    private void SelectMoveAction()
    {
        var moveAction = GetComponent<MoveAction>();
        if (moveAction == null || UnitActionSystem.Instance == null) return;
        UnitActionSystem.Instance.SetSelectedAction(moveAction);
    }
}
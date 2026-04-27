using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
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

    [Header("Damage Numbers")]
    [SerializeField] private GameObject damageNumberPrefab;

    private Vector2Int         currentFacing = new(0, 1);
    private List<GridPosition> lastPreview   = new();

    public CombatActionData ActionData     => actionData;
    public void SetActionData(CombatActionData d) => actionData = d;

    public override string GetActionName() =>
        actionData != null ? actionData.actionName : "Attack";

    private void Start()
    {
        if (diceBox == null)
            diceBox = FindFirstObjectByType<DiceBoxUI>();
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

    public void PerformAttack(GridPosition targetGP, Action onComplete)
    {
        ExecuteAttackFlow(targetGP, onComplete);
    }

    public void PerformAttackNetworked(GridPosition targetGP, Action onComplete)
    {
        ExecuteAttackFlow(targetGP, onComplete);
    }

    private void ExecuteAttackFlow(GridPosition targetGP, Action onComplete)
    {
        StartCoroutine(ExecuteAttackFlowRoutine(targetGP, onComplete));
    }

    private IEnumerator ExecuteAttackFlowRoutine(GridPosition targetGP, Action onComplete)
    {
        if (actionData == null)
        {
            onComplete?.Invoke();
            yield break;
        }

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

        // ── STAMINA SYNC ──────────────────────────────────────────────────
        // SpendStamina must be synced so all peers show the correct value.
        // In MP the owner spends locally and broadcasts; in SP spend locally.
        SpendStamina();
        if (GameManager.IsMultiplayer)
        {
            var bridge = unit.GetComponent<NetworkedPlayerBridge>();
            if (bridge != null && bridge.IsOwner && playerStats != null)
                SyncStaminaServerRpc(playerStats.currentStamina);
        }
        // ─────────────────────────────────────────────────────────────────

        // ── DICE / DAMAGE SYNC ────────────────────────────────────────────
        // In multiplayer ONLY the owner (attacker) rolls dice and then
        // broadcasts the final computed damage value. This guarantees every
        // peer applies the same number regardless of RNG state differences.
        // In single-player the original local-roll path is preserved.
        // ─────────────────────────────────────────────────────────────────

        int  finalDamage  = 0;
        bool diceFinished = false;

        if (GameManager.IsMultiplayer)
        {
            var networkBridge = unit.GetComponent<NetworkedPlayerBridge>();
            bool isOwner = networkBridge != null && networkBridge.IsOwner;

            if (isOwner)
            {
                // Owner rolls and presents dice
                List<int> rolls = RollDamageDice();

                if (diceBox != null)
                {
                    yield return diceBox.PlayRollPresentation(rolls, actionData.flatBonus, (result) =>
                    {
                        finalDamage = Mathf.RoundToInt(result * actionData.damageMultiplier);
                        diceFinished = true;
                    });
                }
                else
                {
                    finalDamage = CalculateDamage(rolls);
                    diceFinished = true;
                }

                while (!diceFinished) yield return null;

                // Broadcast the authoritative damage value and positions to server
                var posArrayX = new int[hitPositions.Count];
                var posArrayY = new int[hitPositions.Count];
                for (int i = 0; i < hitPositions.Count; i++)
                {
                    posArrayX[i] = hitPositions[i].x;
                    posArrayY[i] = hitPositions[i].y;
                }

                if (networkBridge != null)
                    networkBridge.RequestApplyDamageServerRpc(posArrayX, posArrayY, finalDamage);
            }
            // Non-owners do nothing here — damage is applied via the ServerRpc → ClientRpc chain
        }
        else
        {
            // ── Single-player path (unchanged) ────────────────────────────
            List<int> rolls = RollDamageDice();

            if (diceBox != null)
            {
                yield return diceBox.PlayRollPresentation(rolls, actionData.flatBonus, (result) =>
                {
                    finalDamage = Mathf.RoundToInt(result * actionData.damageMultiplier);
                    diceFinished = true;
                });
            }
            else
            {
                finalDamage = CalculateDamage(rolls);
                diceFinished = true;
            }

            while (!diceFinished) yield return null;

            ApplyDamageWithValue(hitPositions, finalDamage);
        }

        playerAnimator?.RefreshStaminaState();
        isActive = false;
        SelectMoveAction();
        onActionComplete?.Invoke();
    }

    // ── Stamina ServerRpc (owner → server → all clients) ──────────────────

    [ServerRpc(RequireOwnership = false)]
    private void SyncStaminaServerRpc(int newStamina)
    {
        SyncStaminaClientRpc(newStamina);
    }

    [ClientRpc]
    private void SyncStaminaClientRpc(int newStamina)
    {
        // Only apply on non-owners — the owner already spent stamina locally
        var bridge = unit.GetComponent<NetworkedPlayerBridge>();
        if (bridge != null && bridge.IsOwner) return;

        if (playerStats != null)
            playerStats.currentStamina = newStamina;

        playerAnimator?.RefreshStaminaState();
    }

    // ── Damage Application ─────────────────────────────────────────────────

    private void ApplyDamageWithValue(List<GridPosition> positions, int dmg)
    {
        var room = unit.GetCurrentRoomGrid();
        if (room == null) return;

        foreach (var pos in positions)
        {
            if (!room.IsValidGridPosition(pos)) continue;

            foreach (var enemy in room.GetEnemiesAtGridPosition(pos))
            {
                if (enemy == null || enemy.IsDead) continue;
                enemy.Health.TakeDamage(dmg);
                DamageNumber.Spawn(damageNumberPrefab, enemy.transform.position, dmg);
            }

            foreach (var target in room.GetUnitsAtGridPosition(pos))
            {
                if (target == unit && !actionData.canTargetSelf) continue;
                target.GetComponent<HealthComponent>()?.TakeDamage(dmg);
                DamageNumber.Spawn(damageNumberPrefab, target.transform.position, dmg);
            }
        }
    }

    private void ApplyDamageWithValueNetworked(List<GridPosition> positions, int dmg)
    {
        var room = unit.GetCurrentRoomGrid();
        if (room == null) return;

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

    public bool CanAfford()
    {
        if (actionData == null) return false;
        if (!actionData.requiresEnoughStamina) return true;
        if (playerStats == null) return false;
        return playerStats.currentStamina >= actionData.staminaCost;
    }

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

    private List<int> RollDamageDice()
    {
        if (!actionData.useDiceDamage)
            return new List<int> { actionData.baseDamage };
        if (actionData.diceCount <= 0)
            return new List<int>();
        return DiceRoller.RollMultiple(actionData.dieType, actionData.diceCount);
    }

    private int CalculateDamage(List<int> rolls)
    {
        int total = actionData.flatBonus;
        foreach (int r in rolls) total += r;
        return Mathf.Max(1, Mathf.RoundToInt(total * actionData.damageMultiplier));
    }

    private void SelectMoveAction()
    {
        var moveAction = GetComponent<MoveAction>();
        if (moveAction == null || UnitActionSystem.Instance == null) return;
        UnitActionSystem.Instance.SetSelectedAction(moveAction);
    }
}
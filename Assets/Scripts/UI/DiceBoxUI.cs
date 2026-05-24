using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Handles dice UI:
/// - pending dice preview when an action is selected
/// - physical dice roll presentation when an action is executed
/// - final roll result display
/// </summary>
public class DiceBoxUI : MonoBehaviour
{
    [Header("UI Text")]
    [SerializeField] private TextMeshProUGUI resultsText;
    [SerializeField] private TextMeshProUGUI totalText;

    [Header("Pending Dice Preview")]
    [SerializeField] private Transform pendingDiceContainer;
    [SerializeField] private GameObject pendingDieUIPrefab;
    [SerializeField] private float pendingDiceScale = 1f;

    [Header("UI Visibility")]
    [SerializeField] private GameObject dimOverlay;
    [SerializeField] private GameObject diceRenderImage;

    [Header("Physics Dice")]
    [SerializeField] private DicePhysicsRoller physicsRoller;

    [Header("Timing")]
    [SerializeField] private float resultHoldTime = 0.35f;

    private readonly List<int> rolls = new();
    private readonly List<GameObject> pendingDiceObjects = new();
    private bool preservingResolvedRoll;
    private int bonus;

    private void Awake()
    {
        SetDim(false);
        SetDiceRenderVisible(false);

        if (physicsRoller != null)
            physicsRoller.SetStageVisible(false);

        ClearPendingDice();
        Refresh();
    }

    public void ShowPendingDice(CombatActionData actionData)
    {
        preservingResolvedRoll = false;
        Clear();

        if (actionData == null || !actionData.useDiceDamage || actionData.diceCount <= 0)
            return;

        bonus = actionData.flatBonus;

        if (resultsText != null)
            resultsText.text = $"{actionData.diceCount}d{(int)actionData.dieType}";

        if (totalText != null)
        {
            totalText.text = actionData.flatBonus != 0
                ? $"Bonus: {actionData.flatBonus:+#;-#;0}"
                : "Ready";
        }

        for (int i = 0; i < actionData.diceCount; i++)
            SpawnDiceIconUnknown();
    }

    public IEnumerator PlayPhysicsD6Roll(int diceCount, int flatBonus, Action<int> onComplete)
    {
        if (physicsRoller == null)
        {
            Debug.LogError($"{nameof(DiceBoxUI)} has no Physics Roller assigned.");
            onComplete?.Invoke(0);
            yield break;
        }

        ClearResultsOnly();
        ClearPendingDice();

        bonus = flatBonus;

        SetDim(true);
        SetDiceRenderVisible(true);
        physicsRoller.SetStageVisible(true);

        if (resultsText != null) resultsText.text = "...";
        if (totalText != null) totalText.text = "...";

        List<int> physicsResults = null;

        yield return physicsRoller.RollD6(diceCount, results =>
        {
            physicsResults = results;
        });

        SetDiceRenderVisible(false);
        physicsRoller.SetStageVisible(false);

        ShowResolvedDice(physicsResults, flatBonus);

        if (pendingDiceContainer != null)
            pendingDiceContainer.gameObject.SetActive(true);

        if (resultsText != null)
            resultsText.gameObject.SetActive(true);

        if (totalText != null)
            totalText.gameObject.SetActive(true);

        int total = GetTotal();

        yield return new WaitForSeconds(resultHoldTime);

        SetDim(false);

        onComplete?.Invoke(total);
    }

    public void ShowRoll(List<int> results, int flatBonus = 0)
    {
        ClearResultsOnly();
        ClearPendingDice();

        if (results != null)
            rolls.AddRange(results);

        bonus = flatBonus;

        Refresh();
    }

    public void Clear()
    {
        if (preservingResolvedRoll)
        {
            Debug.Log("[DiceBoxUI] Clear ignored because resolved roll is being preserved.");
            return;
        }

        ClearResultsOnly();
        ClearPendingDice();

        SetDim(false);
        SetDiceRenderVisible(false);

        if (physicsRoller != null)
        {
            physicsRoller.ClearDice();
            physicsRoller.SetStageVisible(false);
        }

        Refresh();
    }

    private void ClearResultsOnly()
    {
        rolls.Clear();
        bonus = 0;
        Refresh();
    }

    private void ClearPendingDice()
    {
        foreach (GameObject pendingDie in pendingDiceObjects)
        {
            if (pendingDie != null)
                Destroy(pendingDie);
        }

        pendingDiceObjects.Clear();
    }

    private void SpawnDiceIconUnknown()
    {
        if (pendingDiceContainer == null || pendingDieUIPrefab == null)
            return;

        GameObject icon = Instantiate(pendingDieUIPrefab, pendingDiceContainer);

        icon.transform.localScale = Vector3.one * pendingDiceScale;

        pendingDiceObjects.Add(icon);

        DiceResultIconUI iconUI = icon.GetComponent<DiceResultIconUI>();

        if (iconUI != null)
            iconUI.SetUnknown();
    }

    private void SpawnDiceIconValue(int value)
    {
        if (pendingDiceContainer == null || pendingDieUIPrefab == null)
            return;

        GameObject icon = Instantiate(pendingDieUIPrefab, pendingDiceContainer);

        icon.transform.localScale = Vector3.one * pendingDiceScale;

        pendingDiceObjects.Add(icon);

        DiceResultIconUI iconUI = icon.GetComponent<DiceResultIconUI>();

        if (iconUI != null)
            iconUI.SetValue(value);
    }

    private void ShowResolvedDice(List<int> results, int flatBonus)
    {
        preservingResolvedRoll = true;
        ClearPendingDice();

        rolls.Clear();

        if (results != null)
            rolls.AddRange(results);

        bonus = flatBonus;

        foreach (int roll in rolls)
            SpawnDiceIconValue(roll);

        Refresh();
    }
    private void Refresh()
    {
        if (rolls.Count == 0)
        {
            if (resultsText != null) resultsText.text = "-";
            if (totalText != null) totalText.text = "-";
            return;
        }

        if (resultsText != null)
            resultsText.text = string.Join(", ", rolls);

        int diceSum = 0;

        foreach (int roll in rolls)
            diceSum += roll;

        int total = diceSum + bonus;

        if (totalText != null)
        {
            totalText.text = bonus != 0
                ? $"{diceSum} + {bonus} = <b>{total}</b>"
                : $"<b>{total}</b>";
        }
    }

    private int GetTotal()
    {
        int total = bonus;

        foreach (int roll in rolls)
            total += roll;

        return total;
    }

    private void SetDim(bool active)
    {
        if (dimOverlay != null)
            dimOverlay.SetActive(active);
    }

    private void SetDiceRenderVisible(bool active)
    {
        if (diceRenderImage != null)
            diceRenderImage.SetActive(active);
    }
}
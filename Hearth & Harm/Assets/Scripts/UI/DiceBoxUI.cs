using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Handles dice roll UI presentation and receives final results from the physics dice roller.
/// </summary>
public class DiceBoxUI : MonoBehaviour
{
    [Header("UI Text")]
    [SerializeField] private TextMeshProUGUI resultsText;
    [SerializeField] private TextMeshProUGUI totalText;

    [Header("UI Visibility")]
    [SerializeField] private GameObject dimOverlay;
    [SerializeField] private GameObject diceRenderImage;

    [Header("Physics Dice")]
    [SerializeField] private DicePhysicsRoller physicsRoller;

    [Header("Timing")]
    [SerializeField] private float resultHoldTime = 0.35f;
    [SerializeField] private bool hideDiceAfterRoll = false;

    private readonly List<int> rolls = new();
    private int bonus;

    private void Awake()
    {
        SetDim(false);
        SetDiceRenderVisible(false);

        if (physicsRoller != null)
            physicsRoller.SetStageVisible(false);

        Refresh();
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

        rolls.Clear();

        if (physicsResults != null)
            rolls.AddRange(physicsResults);

        Refresh();

        int total = GetTotal();

        yield return new WaitForSeconds(resultHoldTime);

        SetDim(false);
        SetDiceRenderVisible(false);
        physicsRoller.SetStageVisible(false);

        onComplete?.Invoke(total);
    }

    public void ShowRoll(List<int> results, int flatBonus = 0)
    {
        ClearResultsOnly();

        if (results != null)
            rolls.AddRange(results);

        bonus = flatBonus;

        Refresh();
    }

    public void Clear()
    {
        ClearResultsOnly();

        SetDim(false);
        SetDiceRenderVisible(false);

        if (physicsRoller != null)
            physicsRoller.ClearDice();

        Refresh();
    }

    private void ClearResultsOnly()
    {
        rolls.Clear();
        bonus = 0;
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
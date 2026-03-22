using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Shows dice roll results in the HUD.</summary>
public class DiceBoxUI : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI resultsText;
    public TextMeshProUGUI totalText;

    [Header("Visuals")]
    public Transform    diceSpawnPoint;
    public Transform    diceCenter;
    public DiceVisual   d6Prefab;
    public Transform    diceVisualContainer;

    private readonly List<DiceVisual> visuals = new();
    private List<int> rolls = new();
    private int bonus;

    public void ShowRoll(List<int> results, int flatBonus = 0)
    {
        rolls = new(results); bonus = flatBonus;
        foreach (int v in results) SpawnDie(v);
        Refresh();
    }

    private void SpawnDie(int v)
    {
        var die = Instantiate(d6Prefab, diceVisualContainer);
        die.Initialize(diceSpawnPoint.localPosition, diceCenter.localPosition, v);
        visuals.Add(die);
        LayoutDice();
    }

    private void LayoutDice()
    {
        float sx = 50f, sy = 50f;
        int maxRow = 6, total = visuals.Count;
        int rows = Mathf.CeilToInt((float)total / maxRow);
        float topY = diceCenter.localPosition.y + (rows - 1) * sy * 0.5f;
        int idx = 0;
        for (int row = 0; row < rows; row++)
        {
            int inRow = Mathf.Min(maxRow, total - idx);
            float startX = diceCenter.localPosition.x - (inRow - 1) * sx * 0.5f;
            for (int col = 0; col < inRow; col++)
            {
                visuals[idx++].UpdateTarget(new Vector3(startX + col * sx, topY - row * sy, diceCenter.localPosition.z));
            }
        }
    }

    public void Clear()
    {
        rolls.Clear(); bonus = 0;
        foreach (var v in visuals) if (v != null) Destroy(v.gameObject);
        visuals.Clear();
        Refresh();
    }

    private void Refresh()
    {
        if (rolls.Count == 0) { if (resultsText) resultsText.text = "-"; if (totalText) totalText.text = "-"; return; }
        if (resultsText) resultsText.text = string.Join(", ", rolls);
        int sum = bonus; foreach (int r in rolls) sum += r;
        if (totalText) totalText.text = bonus != 0
            ? $"{sum - bonus} +{bonus} = <b>{sum}</b>"
            : $"{sum}";
    }
}
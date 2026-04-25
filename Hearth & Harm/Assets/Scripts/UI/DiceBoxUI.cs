using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Shows dice roll results in the HUD.
/// Can either display instantly with ShowRoll(), or play a timed presentation
/// before reporting the final total.
/// </summary>
public class DiceBoxUI : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI resultsText;
    public TextMeshProUGUI totalText;

    [Header("Main Roll Area")]
    [SerializeField] private RectTransform mainRollArea;

    [Header("Visuals")]
    public Transform diceSpawnPoint;
    public Transform diceCenter;
    public DiceVisual d6Prefab;
    public Transform diceVisualContainer;

    [Header("Presentation Timing")]
    [SerializeField] private float chaoticRollTime = 0.75f;
    [SerializeField] private float settleTime = 0.5f;
    [SerializeField] private float resultHoldTime = 0.35f;

    [Header("Layout")]
    [SerializeField] private float spacingX = 50f;
    [SerializeField] private float spacingY = 50f;
    [SerializeField] private int maxPerRow = 6;
    [SerializeField] private float chaoticSpawnRadius = 120f;

    [Header("Dice Scale")]
    [SerializeField] private float chaosScale = 1.5f;
    [SerializeField] private float finalScale = 0.6f;

    private readonly List<DiceVisual> visuals = new();
    private List<int> rolls = new();
    private int bonus;


    public void ShowRoll(List<int> results, int flatBonus = 0)
    {
        Clear();

        rolls = new List<int>(results);
        bonus = flatBonus;

        foreach (int v in results)
        {
            SpawnDie(v, diceSpawnPoint.localPosition);
        }

        LayoutDice();
        Refresh();
    }

    public IEnumerator PlayRollPresentation(List<int> results, int flatBonus, Action<int> onComplete)
    {
        Clear();

        rolls = new List<int>(results);
        bonus = flatBonus;

        if (resultsText != null) resultsText.text = "...";
        if (totalText != null) totalText.text = "...";

        foreach (int value in results)
        {
            Vector3 randomSpawn = GetChaoticSpawnPosition();
            SpawnDie(value, randomSpawn);
        }

        yield return new WaitForSeconds(chaoticRollTime);

        LayoutDice();

        foreach (DiceVisual die in visuals)
        {
            if (die != null)
                die.SetTargetScale(finalScale);
        }

        yield return new WaitForSeconds(settleTime);

        Refresh();

        int total = GetTotal();

        yield return new WaitForSeconds(resultHoldTime);

        onComplete?.Invoke(total);
    }

    private void SpawnDie(int value, Vector3 spawnPosition)
    {
        if (d6Prefab == null || diceVisualContainer == null) return;

        DiceVisual die = Instantiate(d6Prefab, diceVisualContainer);

        // During chaos phase, the die stays around its random spawn position.
        die.Initialize(spawnPosition, spawnPosition, value);
        die.SetTargetScale(chaosScale);

        visuals.Add(die);
    }

    private Vector3 GetChaoticSpawnPosition()
    {
        if (mainRollArea == null)
        {
            Vector2 fallback = UnityEngine.Random.insideUnitCircle * chaoticSpawnRadius;
            Vector3 center = diceCenter != null ? diceCenter.localPosition : Vector3.zero;
            return center + new Vector3(fallback.x, fallback.y, 0f);
        }

        Rect rect = mainRollArea.rect;

        float x = UnityEngine.Random.Range(rect.xMin, rect.xMax);
        float y = UnityEngine.Random.Range(rect.yMin, rect.yMax);

        return diceVisualContainer.InverseTransformPoint(
            mainRollArea.TransformPoint(new Vector3(x, y, 0f))
        );
    }

    private void LayoutDice()
    {
        if (diceCenter == null) return;

        int total = visuals.Count;
        if (total == 0) return;

        int rows = Mathf.CeilToInt((float)total / maxPerRow);
        float topY = diceCenter.localPosition.y + (rows - 1) * spacingY * 0.5f;

        int index = 0;

        for (int row = 0; row < rows; row++)
        {
            int inRow = Mathf.Min(maxPerRow, total - index);
            float startX = diceCenter.localPosition.x - (inRow - 1) * spacingX * 0.5f;

            for (int col = 0; col < inRow; col++)
            {
                Vector3 target = new Vector3(
                    startX + col * spacingX,
                    topY - row * spacingY,
                    diceCenter.localPosition.z
                );

                visuals[index].UpdateTarget(target);
                index++;
            }
        }
    }

    public void Clear()
    {
        rolls.Clear();
        bonus = 0;

        foreach (DiceVisual visual in visuals)
        {
            if (visual != null)
                Destroy(visual.gameObject);
        }

        visuals.Clear();
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
                : $"{total}";
        }
    }

    private int GetTotal()
    {
        int total = bonus;

        foreach (int roll in rolls)
            total += roll;

        return total;
    }
}
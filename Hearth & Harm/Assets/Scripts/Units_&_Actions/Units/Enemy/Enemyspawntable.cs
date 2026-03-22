using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject that defines which enemies spawn in a given room type
/// at a given level range.
/// Create via: Assets > Create > Combat > Enemy Spawn Table
/// </summary>
[CreateAssetMenu(fileName = "NewSpawnTable", menuName = "Combat/Enemy Spawn Table")]
public class EnemySpawnTable : ScriptableObject
{
    [Serializable]
    public class EnemyEntry
    {
        public GameObject prefab;
        [Range(0f, 100f)] public float percentage = 50f;
    }

    [Header("Enemy Composition")]
    [Tooltip("Percentages should total 100.")]
    public List<EnemyEntry> entries = new();

    [Header("Room Type")]
    public LevelGenerator.RoomType roomType = LevelGenerator.RoomType.Normal;

    [Header("Level Range")]
    [Min(1)] public int minLevel = 1;
    [Min(1)] public int maxLevel = 99;

    public bool IsActiveForLevel(int level) => level >= minLevel && level <= maxLevel;

    /// <summary>Returns how many of each enemy type to spawn given the total budget.</summary>
    public List<(GameObject prefab, int count)> CalculateSpawns(int budget)
    {
        var result = new List<(GameObject, int)>();
        foreach (var e in entries)
        {
            if (e.prefab == null || e.percentage <= 0f) continue;
            int count = Mathf.Max(1, Mathf.RoundToInt(budget * (e.percentage / 100f)));
            result.Add((e.prefab, count));
        }
        return result;
    }

#if UNITY_EDITOR
    [Header("Summary (read-only)")]
    [SerializeField, TextArea(3,5)] private string _summary;
    private void OnValidate()
    {
        if (maxLevel < minLevel) maxLevel = minLevel;
        float total = 0f; foreach (var e in entries) total += e.percentage;
        string ok = Mathf.Abs(total - 100f) < 0.1f ? "OK" : $"WARNING: {total:F0}% (should be 100)";
        _summary = $"Levels {minLevel}-{(maxLevel >= 99 ? "inf" : maxLevel.ToString())} | {roomType} | {ok}";
        foreach (var e in entries) _summary += $"\n  {(e.prefab ? e.prefab.name : "none")} {e.percentage:F0}%";
    }
#endif
}
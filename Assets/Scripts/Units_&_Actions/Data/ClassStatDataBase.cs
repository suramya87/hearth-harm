using System.Collections.Generic;
using UnityEngine;
[CreateAssetMenu(menuName = "Data/Class Stats Database")]
public class ClassStatsDatabase : ScriptableObject
{
    public List<ClassStats> classes = new();
    public ClassStats Get(PlayerClass c)
    {
        foreach (var s in classes) if (s.playerClass == c) return s;
        Debug.LogError($"[ClassStatsDatabase] No entry for {c}"); return null;
    }
}
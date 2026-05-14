using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewBossStats", menuName = "Boss/Boss Stats")]
public class BossStats : ScriptableObject
{
    [Header("Identity")]
    public string bossName  = "Boss";
    public int    maxHealth = 300;

    [Header("Movement")]
    [Min(1)] public int moveRange   = 3;
    [Min(1)] public int attackRange = 5;
    [Min(0)] public int turnsBeforeFirstAction = 0;

    [Header("Attacks")]
    public CombatActionData rangedAttackData;
    public CombatActionData cleaveAttackData;

    [Header("Phase Thresholds")]
    [Range(0f, 1f)] public float enrageThreshold = 0.5f;

    [Header("Invisibility")]
    [Range(0f, 1f)] public float invisAlpha           = 0.1f;
    [Range(0f, 1f)] public float damageReductionInvis = 0.5f;
    [Min(1)]        public int   invisDurationTurns   = 2;

    [Header("Minion Spawning")]
    public List<GameObject> minionPrefabs        = new();
    [Min(0)] public int     minionsPerWave        = 3;  
    [Min(0)] public int     maxConcurrentMinions  = 5;  

    [Header("Phase Damage Modifiers")]
    [Range(0f, 3f)] public float increasedDamageMultiplier = 1.75f; 

    [Header("Kiting")]
    public bool  kiteEnabled = false;
    [Min(0)] public int kiteRange = 2;

    // ── Helpers ────────────────────────────────────────────────────────────

    public bool CanSpawnMinions =>
        minionPrefabs != null &&
        minionPrefabs.Count > 0 &&
        minionsPerWave > 0 &&
        maxConcurrentMinions > 0;
}
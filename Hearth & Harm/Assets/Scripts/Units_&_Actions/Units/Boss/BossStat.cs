// ─────────────────────────────────────────────────────────────────────────────
// BossStats.cs
// ScriptableObject that drives all boss tuning. Create via
// Assets > Create > Boss/Boss Stats
// ─────────────────────────────────────────────────────────────────────────────
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewBossStats", menuName = "Boss/Boss Stats")]
public class BossStats : ScriptableObject
{
    [Header("Identity")]
    public string bossName = "Boss";
    public int    maxHealth = 300;

    [Header("Movement")]
    [Min(1)] public int moveRange    = 3;
    [Min(1)] public int attackRange  = 5;
    [Min(1)] public int turnsBeforeFirstAction = 0;

    [Header("Attacks")]
    public CombatActionData rangedAttackData;   // used for the standard ranged shot
    public CombatActionData cleaveAttackData;   // optional melee cleave when player is adjacent

    [Header("Phase Thresholds")]
    [Range(0f, 1f)] public float enrageThreshold  = 0.5f;   // HP% that triggers enrage
    [Range(0f, 1f)] public float vulnerableBuffer = 0.05f;  // grace band below threshold

    [Header("Invisibility")]
    [Range(0f, 1f)] public float invisAlpha           = 0.1f;  // sprite alpha while invisible
    [Range(0f, 1f)] public float damageReductionInvis = 0.5f;  // multiplier (0.5 = 50% dmg)
    [Min(1)]        public int   invisDurationTurns   = 2;

    [Header("Minion Spawning")]
    public List<GameObject> minionPrefabs = new();           // pool the boss picks from
    [Min(1)] public int minionsPerWave    = 3;
    [Min(1)] public int maxConcurrentMinions = 5;

    [Header("Phase Damage Modifiers")]
    [Range(0f, 3f)] public float reducedDamageMultiplier  = 0.4f;  // while minions alive
    [Range(0f, 3f)] public float increasedDamageMultiplier = 1.75f; // vulnerable window

    [Header("Kiting")]
    public bool kiteEnabled = false;
    [Min(0)] public int kiteRange = 2;
}
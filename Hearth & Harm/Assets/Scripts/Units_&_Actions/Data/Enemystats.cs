using UnityEngine;

/// <summary>
/// Data asset for one enemy type.
/// Create via: Assets > Create > Combat > Enemy Stats
/// </summary>
[CreateAssetMenu(fileName = "NewEnemyStats", menuName = "Combat/Enemy Stats")]
public class EnemyStats : ScriptableObject
{
    [Header("Identity")]
    public string enemyName = "Enemy";

    [Header("Health")]
    [Min(1)] public int maxHealth = 50;

    [Header("Movement")]
    [Min(1)] public int moveRange = 3;

    [Header("Attack")]
    [Tooltip("Attack range is read from attackData.maxRange.")]
    public CombatActionData attackData;

    [Header("Ranged Behaviour")]
    public bool kiteEnabled = true;
    [Min(1)] public int kiteRange = 2;

    [Header("Behaviour")]
    public bool alwaysChases = true;
    [Min(0)] public int turnsBeforeFirstAction = 0;

    /// <summary>Attack range in tiles. Falls back to 1 (melee) when no attackData.</summary>
    public int attackRange => attackData != null ? attackData.maxRange : 1;

#if UNITY_EDITOR
    [Header("Summary (read-only)")]
    [SerializeField, TextArea(2,3)] private string _summary;
    private void OnValidate()
    {
        int r = attackData != null ? attackData.maxRange : 1;
        _summary = $"HP:{maxHealth}  Move:{moveRange}  Range:{r}\n" +
                   $"Attack:{(attackData ? attackData.actionName : "None")}  Kite:{kiteEnabled}({kiteRange})";
    }
#endif
}
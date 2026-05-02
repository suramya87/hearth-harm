using UnityEngine;

[CreateAssetMenu(fileName = "NewCombatAction", menuName = "Combat/Combat Action Data")]
public class CombatActionData : ScriptableObject
{
    [Header("Identity")]
    public string actionName = "Attack";
    public Sprite icon;
    [Tooltip("Animation frames in order. Leave empty to show icon as static flash.")]
    public Sprite[] animationFrames;

    [Header("Damage Mode")]
    public bool useDiceDamage = false;

    [Header("Flat Damage (dice disabled)")]
    [Min(0)] public int baseDamage = 10;

    [Header("Dice Damage (dice enabled)")]
    [Min(0)] public int diceCount = 1;
    public DieType dieType = DieType.D6;
    [Tooltip("Added to dice total.")]
    public int flatBonus = 0;

    [Header("Damage Multiplier")]
    [Min(0f)] public float damageMultiplier = 1f;

    [Header("Attack Pattern")]
    public AttackPattern attackPattern;
    public bool rotatesToFacing = true;

    [Header("Range")]
    [Min(0)] public int minRange;
    [Min(0)] public int maxRange;
    public bool canTargetSelf = false;

    [Header("Stamina Cost")]
    [Min(0)] public int staminaCost = 2;
    public bool requiresEnoughStamina = true;

    [Header("Visual Feedback")]
    public Color aoeHighlightColor   = new(1f, 0.25f, 0.25f, 1f);
    public Color rangeHighlightColor = new(1f, 0.8f,  0.2f,  1f);
    [Tooltip("Show hit sprite on every tile instead of only the target.")]
    public bool showSpritePerTile = false;

    // ── Damage helper ──────────────────────────────────────────────────────

    public int CalculateDamage()
    {
        int raw;
        if (useDiceDamage)
        {
            raw = flatBonus;
            int sides = (int)dieType;
            for (int i = 0; i < diceCount; i++)
                raw += UnityEngine.Random.Range(1, sides + 1);
        }
        else raw = baseDamage;

        return Mathf.Max(1, Mathf.RoundToInt(raw * damageMultiplier));
    }

#if UNITY_EDITOR
    [Header("Summary (read-only)")]
    [SerializeField, TextArea(3,5)] private string _summary;
    private void OnValidate()
    {
        if (maxRange < minRange) maxRange = minRange;
        string dmg = useDiceDamage ? $"{diceCount}d{(int)dieType}+{flatBonus}" : $"{baseDamage}";
        string mult = !Mathf.Approximately(damageMultiplier,1f) ? $" x{damageMultiplier}" : "";
        _summary = $"Damage: {dmg}{mult}\nStamina: {staminaCost}\n" +
                   $"Pattern: {(attackPattern ? attackPattern.name : "single")}\n" +
                   $"Range: {(maxRange == 0 ? "Melee" : $"{minRange}-{maxRange}")}\n" +
                   $"Rotates: {rotatesToFacing}";
    }
#endif
}
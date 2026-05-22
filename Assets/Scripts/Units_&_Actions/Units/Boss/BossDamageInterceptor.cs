// ─────────────────────────────────────────────────────────────────────────────
// BossDamageInterceptor.cs
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

[RequireComponent(typeof(HealthComponent))]
public class BossDamageInterceptor : MonoBehaviour
{
    private HealthComponent health;
    private float           damageMultiplier = 1f;

    // Deduplication — track which frame we last took damage in
    private int   lastDamageFrame  = -1;
    private int   pendingDamage    = 0;

    private void Awake() => health = GetComponent<HealthComponent>();

    private void LateUpdate()
    {
        // Flush accumulated damage once per frame
        if (pendingDamage > 0)
        {
            int modified = Mathf.Max(1, Mathf.RoundToInt(pendingDamage * damageMultiplier));
            health.TakeDamage(modified);
            Debug.Log($"[BossDamageInterceptor] Flushed {pendingDamage} raw → {modified} modified dmg");
            pendingDamage = 0;
        }
    }

    public void SetDamageMultiplier(float multiplier)
    {
        damageMultiplier = multiplier;
        Debug.Log($"[BossDamageInterceptor] Damage multiplier → {multiplier:F2}");
    }

    public void TakeDamage(int rawAmount)
    {
        // Accumulate all hits this frame — LateUpdate flushes them as one
        pendingDamage += rawAmount;
        Debug.Log($"[BossDamageInterceptor] Accumulated {rawAmount} dmg this frame (total pending: {pendingDamage})");
    }
}
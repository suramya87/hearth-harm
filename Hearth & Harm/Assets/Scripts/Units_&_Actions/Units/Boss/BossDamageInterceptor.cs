// ─────────────────────────────────────────────────────────────────────────────
// BossDamageInterceptor.cs
// Sits between the AI's DealDamage call and HealthComponent.TakeDamage.
// BossAI calls interceptor.TakeDamage(amount) instead of health.TakeDamage directly.
// This is the single place where phase multipliers are applied.
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

[RequireComponent(typeof(HealthComponent))]
public class BossDamageInterceptor : MonoBehaviour
{
    private HealthComponent health;
    private float           damageMultiplier = 1f;

    private void Awake() => health = GetComponent<HealthComponent>();

    /// <summary>Set by BossPhaseController when phase transitions occur.</summary>
    public void SetDamageMultiplier(float multiplier)
    {
        damageMultiplier = multiplier;
        Debug.Log($"[BossDamageInterceptor] Damage multiplier → {multiplier:F2}");
    }

    /// <summary>
    /// Call this instead of health.TakeDamage from outside.
    /// Applies the current phase multiplier then forwards to HealthComponent.
    /// </summary>
    public void TakeDamage(int rawAmount)
    {
        int modified = Mathf.Max(1, Mathf.RoundToInt(rawAmount * damageMultiplier));
        health.TakeDamage(modified);
    }
}
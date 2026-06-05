using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(HealthComponent))]
public class NetworkedHealthBridge : NetworkBehaviour
{
    private HealthComponent health;

    private void Awake()
    {
        health = GetComponent<HealthComponent>();
    }

    // ── Static helpers ─────────────────────────────────────────────────────

    public static void TakeDamage(GameObject target, int amount)
    {
        if (!GameManager.IsMultiplayer)
        {
            target.GetComponent<HealthComponent>()?.TakeDamage(amount);
            return;
        }

        var bridge = target.GetComponent<NetworkedHealthBridge>();
        if (bridge != null)
            bridge.TakeDamageInstance(amount);
        else
            target.GetComponent<HealthComponent>()?.TakeDamage(amount);
    }

    public static void Heal(GameObject target, int amount)
    {
        if (!GameManager.IsMultiplayer)
        {
            target.GetComponent<HealthComponent>()?.Heal(amount);
            return;
        }

        var bridge = target.GetComponent<NetworkedHealthBridge>();
        if (bridge != null)
            bridge.Heal(amount);
        else
            target.GetComponent<HealthComponent>()?.Heal(amount);
    }

    // ── Instance API ───────────────────────────────────────────────────────

    public void TakeDamageInstance(int amount)
    {
        if (!GameManager.IsMultiplayer)
        {
            health.TakeDamage(amount);
            return;
        }

        if (!IsSpawned)
        {
            health.TakeDamage(amount);
            return;
        }

        RequestTakeDamageServerRpc(amount);
    }

    public void Heal(int amount)
    {
        if (!GameManager.IsMultiplayer)
        {
            health.Heal(amount);
            return;
        }
        RequestHealServerRpc(amount);
    }

    // ── Server RPCs ────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RequestTakeDamageServerRpc(int amount)
    {
        if (health.IsDead) return;
        health.TakeDamage(amount);
        SyncHealthClientRpc(health.CurrentHealth, health.MaxHealth, amount, health.IsDead);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestHealServerRpc(int amount)
    {
        if (health.IsDead) return;
        health.Heal(amount);
        SyncHealthClientRpc(health.CurrentHealth, health.MaxHealth, 0, false);
    }

    // ── Client RPCs ────────────────────────────────────────────────────────

    [ClientRpc]
    private void SyncHealthClientRpc(int currentHp, int maxHp, int damageDealt, bool isDead)
    {
        if (IsServer) return;

        if (damageDealt > 0)
            health.TakeDamage(damageDealt);
        else
            health.SetHealth(currentHp);
    }
}
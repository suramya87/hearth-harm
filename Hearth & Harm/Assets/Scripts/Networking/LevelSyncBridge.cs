using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;


[RequireComponent(typeof(LevelGenerator))]
public class LevelSyncBridge : NetworkBehaviour
{
    [Header("Settings")]
    [Tooltip("How many seconds to wait for all clients before timing out and starting anyway.")]
    [SerializeField] private float clientReadyTimeout = 30f;

    // ── Events ─────────────────────────────────────────────────────────────

    /// <summary>Fired on all peers once the level is ready and players can be spawned.</summary>
    public static event Action OnNetworkLevelReady;

    // ── State ──────────────────────────────────────────────────────────────

    private LevelGenerator  levelGenerator;
    private int             syncSeed;
    private int             clientsReady;
    private int             expectedClients;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        levelGenerator = GetComponent<LevelGenerator>();
    }

    public override void OnNetworkSpawn()
    {
        if (!GameManager.IsMultiplayer) return;

        if (IsServer)
        {
            // Give clients a moment to spawn before we generate
            StartCoroutine(HostGenerateAfterDelay());
        }
        // Clients wait for the SyncLevelClientRpc broadcast
    }

    // ── Host side ──────────────────────────────────────────────────────────

    private IEnumerator HostGenerateAfterDelay()
    {
        yield return new WaitForSeconds(0.5f);

        // Pick a seed and bake it into Unity's random state
        syncSeed = UnityEngine.Random.Range(1, int.MaxValue);
        UnityEngine.Random.InitState(syncSeed);

        Debug.Log($"[LevelSyncBridge] Host generating with seed {syncSeed}");
        levelGenerator.GenerateLevel();

        // How many non-host clients do we expect?
        expectedClients = NetworkManager.Singleton.ConnectedClientsList.Count - 1;
        clientsReady    = 0;

        if (expectedClients == 0)
        {
            // Solo host — just fire ready
            FireLevelReady();
            yield break;
        }

        // Tell all clients to generate with this seed
        SyncLevelClientRpc(syncSeed);

        // Wait for all clients to confirm
        float elapsed = 0f;
        while (clientsReady < expectedClients && elapsed < clientReadyTimeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (clientsReady < expectedClients)
            Debug.LogWarning($"[LevelSyncBridge] Timed out waiting for clients ({clientsReady}/{expectedClients} ready). Proceeding anyway.");

        FireLevelReady();
    }

    [ServerRpc(RequireOwnership = false)]
    public void ClientReadyServerRpc()
    {
        clientsReady++;
        Debug.Log($"[LevelSyncBridge] Client ready ({clientsReady}/{expectedClients})");
    }

    private void FireLevelReady()
    {
        Debug.Log("[LevelSyncBridge] All peers ready — firing OnNetworkLevelReady");
        NotifyAllPeersLevelReadyClientRpc();
    }

    // ── Client side ────────────────────────────────────────────────────────

    [ClientRpc]
    private void SyncLevelClientRpc(int seed)
    {
        if (IsServer) return; // Host already generated

        Debug.Log($"[LevelSyncBridge] Client regenerating with seed {seed}");
        UnityEngine.Random.InitState(seed);
        levelGenerator.GenerateLevel();

        // Tell server we're done
        ClientReadyServerRpc();
    }

    [ClientRpc]
    private void NotifyAllPeersLevelReadyClientRpc()
    {
        OnNetworkLevelReady?.Invoke();

        Debug.Log("[LevelSyncBridge] Level ready on this peer.");
    }

    // ── Single-player passthrough ──────────────────────────────────────────


    public void TriggerSinglePlayerGenerate()
    {
        if (GameManager.IsMultiplayer) return;
        levelGenerator.GenerateLevel();
    }
}
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

    private LevelGenerator levelGenerator;
    private int            syncSeed;
    private int            clientsReady;
    private int            expectedClients;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        levelGenerator = GetComponent<LevelGenerator>();
    }

    public override void OnNetworkSpawn()
    {
        if (!GameManager.IsMultiplayer) return;

        if (IsServer)
            StartCoroutine(HostGenerateAfterDelay());
        // Clients wait for SyncLevelClientRpc
    }

    // ── Host side ──────────────────────────────────────────────────────────

    private IEnumerator HostGenerateAfterDelay()
    {
        yield return new WaitForSeconds(0.5f);

        syncSeed = UnityEngine.Random.Range(1, int.MaxValue);
        UnityEngine.Random.InitState(syncSeed);

        Debug.Log($"[LevelSyncBridge] Host generating with seed {syncSeed}");
        levelGenerator.GenerateLevel();

        // How many non-host clients are we waiting for?
        expectedClients = NetworkManager.Singleton.ConnectedClientsList.Count - 1;
        clientsReady    = 0;

        if (expectedClients == 0)
        {
            FireLevelReady();
            yield break;
        }

        SyncLevelClientRpc(syncSeed);

        float elapsed = 0f;
        while (clientsReady < expectedClients && elapsed < clientReadyTimeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (clientsReady < expectedClients)
            Debug.LogWarning($"[LevelSyncBridge] Timed out waiting for clients " +
                             $"({clientsReady}/{expectedClients} ready). Proceeding anyway.");

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

        // Fire on server first so NetworkedPlayerSpawner can begin
        OnNetworkLevelReady?.Invoke();

        // Then fire on all clients (NOT the server again — hence the guard in the ClientRpc)
        NotifyClientsLevelReadyClientRpc();
    }

    // ── Client side ────────────────────────────────────────────────────────

    [ClientRpc]
    private void SyncLevelClientRpc(int seed)
    {
        if (IsServer) return; // Host already generated

        Debug.Log($"[LevelSyncBridge] Client regenerating with seed {seed}");
        UnityEngine.Random.InitState(seed);
        levelGenerator.GenerateLevel();

        ClientReadyServerRpc();
    }

    [ClientRpc]
    private void NotifyClientsLevelReadyClientRpc()
    {

        if (IsServer) return;

        OnNetworkLevelReady?.Invoke();
        Debug.Log("[LevelSyncBridge] Level ready on this client.");
    }

    // ── Single-player passthrough ──────────────────────────────────────────

    public void TriggerSinglePlayerGenerate()
    {
        if (GameManager.IsMultiplayer) return;
        levelGenerator.GenerateLevel();
    }
}
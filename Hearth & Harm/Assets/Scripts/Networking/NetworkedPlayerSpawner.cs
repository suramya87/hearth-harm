using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-only. Spawns one player prefab per connected client once the level is ready.
///
/// Character index priority:
///   1. LobbySync (populated during menu char-select via NGO RPCs — always present)
///   2. CharacterSelectionSync (legacy fallback if you spawn it separately)
///   3. CharacterSelection.Index static (single-player / editor fallback)
/// </summary>
public class NetworkedPlayerSpawner : NetworkBehaviour
{
    [Header("Player Prefabs (index matches character selection)")]
    [SerializeField] private List<GameObject> playerPrefabs;

    [Header("Fallback if selection index is out of range")]
    [SerializeField] private GameObject fallbackPrefab;

    [Header("Spawn spread (grid units apart)")]
    [SerializeField] private int spawnSpread = 1;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        LevelSyncBridge.OnNetworkLevelReady += OnLevelReady;
    }

    public override void OnNetworkDespawn()
    {
        LevelSyncBridge.OnNetworkLevelReady -= OnLevelReady;
    }

    private void OnLevelReady()
    {
        if (!IsServer) return;
        StartCoroutine(SpawnAllPlayersNextFrame());
    }

    private IEnumerator SpawnAllPlayersNextFrame()
    {
        yield return null;
        SpawnAllPlayers();
    }

    private void SpawnAllPlayers()
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) { Debug.LogError("[NetworkedPlayerSpawner] No LevelGenerator!"); return; }

        var startRoom = gen.GetAllRooms().Find(r =>
            r.prefabData.roomType == LevelGenerator.RoomType.Start &&
            r.roomGrid != null && r.roomGrid.IsInitialized());

        if (startRoom == null)
        {
            Debug.LogError("[NetworkedPlayerSpawner] No valid start room found!");
            return;
        }

        RoomManager.Instance?.SetCurrentRoom(startRoom);

        var clients = NetworkManager.Singleton.ConnectedClientsList;
        int cx = startRoom.roomGrid.GetWidth()  / 2;
        int cy = startRoom.roomGrid.GetHeight() / 2;

        var offsets = new Vector2Int[]
        {
            new(0, 0),
            new(-spawnSpread, 0),
            new( spawnSpread, 0),
            new(0, -spawnSpread),
            new(0,  spawnSpread)
        };

        for (int i = 0; i < clients.Count; i++)
        {
            ulong clientId = clients[i].ClientId;
            int   charIndex = ResolveCharacterIndex(clientId);

            GameObject prefab = GetPrefab(charIndex);
            if (prefab == null) continue;

            var offset  = i < offsets.Length ? offsets[i] : new Vector2Int(i, 0);
            var spawnGP = new GridPosition(cx + offset.x, cy + offset.y);

            if (!startRoom.roomGrid.IsValidGridPosition(spawnGP) ||
                !startRoom.roomGrid.IsWalkableIgnoreOccupancy(spawnGP))
                spawnGP = new GridPosition(cx, cy);

            Vector3 spawnWorld = startRoom.roomGrid.GetWorldPosition(spawnGP);

            var go     = Instantiate(prefab, spawnWorld, Quaternion.identity);
            var netObj = go.GetComponent<NetworkObject>();

            if (netObj == null)
            {
                Debug.LogError($"[NetworkedPlayerSpawner] {prefab.name} missing NetworkObject!");
                Destroy(go);
                continue;
            }

            netObj.SpawnAsPlayerObject(clientId, destroyWithScene: true);

            var unit = go.GetComponent<Unit>();
            unit?.PlaceInRoom(startRoom.roomGrid, spawnGP);

            var bridge = go.GetComponent<NetworkedPlayerBridge>();
            bridge?.InitialPlacement(startRoom.roomGrid, spawnGP);

            // Tell this client which character index they are for local UI
            NotifyClientCharIndexClientRpc(charIndex, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            });

            Debug.Log($"[NetworkedPlayerSpawner] Spawned player {clientId} (char {charIndex}) at {spawnGP}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Character index resolution
    // Priority: LobbySync → CharacterSelectionSync → static fallback
    // ─────────────────────────────────────────────────────────────────────

    private int ResolveCharacterIndex(ulong clientId)
    {
        // Primary: LobbySync is always present — it was populated during char-select
        if (LobbySync.Instance != null)
        {
            int idx = LobbySync.Instance.GetCharacterIndex(clientId);
            Debug.Log($"[NetworkedPlayerSpawner] Client {clientId} char index from LobbySync: {idx}");
            return idx;
        }

        // Legacy: CharacterSelectionSync if someone spawned it separately
        if (CharacterSelectionSync.Instance != null)
        {
            int idx = CharacterSelectionSync.Instance.GetCharacterIndex(clientId);
            Debug.Log($"[NetworkedPlayerSpawner] Client {clientId} char index from CharacterSelectionSync: {idx}");
            return idx;
        }

        // Last resort: static value (only valid for the host / single-player testing)
        Debug.LogWarning($"[NetworkedPlayerSpawner] No sync source for client {clientId} — using CharacterSelection.Index={CharacterSelection.Index}");
        return CharacterSelection.Index;
    }

    [ClientRpc]
    private void NotifyClientCharIndexClientRpc(int charIndex, ClientRpcParams rpcParams = default)
    {
        // Keep the static class in sync so UI that reads CharacterSelection.Index is correct
        CharacterSelection.Index = charIndex;
        Debug.Log($"[NetworkedPlayerSpawner] Local char index confirmed: {charIndex}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Prefab lookup
    // ─────────────────────────────────────────────────────────────────────

    private GameObject GetPrefab(int index)
    {
        if (playerPrefabs != null && index >= 0 && index < playerPrefabs.Count)
            return playerPrefabs[index];
        if (fallbackPrefab != null)
        {
            Debug.LogWarning($"[NetworkedPlayerSpawner] Index {index} out of range — using fallback prefab.");
            return fallbackPrefab;
        }
        Debug.LogError($"[NetworkedPlayerSpawner] No prefab for index {index} and no fallback assigned!");
        return null;
    }
}
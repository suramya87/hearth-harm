using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Lightweight replacement for LobbySync's character-index tracking.
/// Syncs each player's chosen character index to the server so
/// NetworkedPlayerSpawner knows which prefab to spawn.
///
/// SETUP:
///   Add to a NetworkObject prefab. Register in NetworkManager's Network Prefabs.
///   Host spawns this object once at game start (or it can be in the scene
///   as a pre-placed NetworkObject).
///
/// USAGE:
///   Before loading the game scene, the local player calls:
///       CharacterSelectionSync.Instance.SubmitCharacterIndex(myIndex);
///   NetworkedPlayerSpawner then reads it via GetCharacterIndex(clientId).
///
/// If you already have a lobby/character selection system that stores this
/// data another way, you can delete this file and update NetworkedPlayerSpawner
/// to read from whatever source you use.
/// </summary>
public class CharacterSelectionSync : NetworkBehaviour
{
    public static CharacterSelectionSync Instance { get; private set; }

    // clientId → character prefab index
    private readonly Dictionary<ulong, int> characterIndexMap = new();

    public override void OnNetworkSpawn()
    {
        if (Instance != null && Instance != this) { NetworkObject.Despawn(); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Each client registers their selection when they join
        SubmitCharacterIndexServerRpc(
            NetworkManager.Singleton.LocalClientId,
            CharacterSelection.Index   // reads from your existing static class
        );
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Call this when the local player confirms their character choice.</summary>
    public void SubmitCharacterIndex(int index)
    {
        CharacterSelection.Index = index; // keep the static class in sync too
        SubmitCharacterIndexServerRpc(NetworkManager.Singleton.LocalClientId, index);
    }

    /// <summary>Server reads this when spawning players.</summary>
    public int GetCharacterIndex(ulong clientId)
    {
        characterIndexMap.TryGetValue(clientId, out int idx);
        return idx;
    }

    // ── ServerRpc ──────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void SubmitCharacterIndexServerRpc(ulong clientId, int index)
    {
        characterIndexMap[clientId] = index;
        Debug.Log($"[CharacterSelectionSync] Client {clientId} selected character {index}");
    }
}
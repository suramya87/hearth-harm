using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;


public class CharacterSelectionSync : NetworkBehaviour
{
    public static CharacterSelectionSync Instance { get; private set; }

    private readonly Dictionary<ulong, int> characterIndexMap = new();

    public override void OnNetworkSpawn()
    {
        if (Instance != null && Instance != this) { NetworkObject.Despawn(); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

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

    public void SubmitCharacterIndex(int index)
    {
        CharacterSelection.Index = index; 
        SubmitCharacterIndexServerRpc(NetworkManager.Singleton.LocalClientId, index);
    }

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
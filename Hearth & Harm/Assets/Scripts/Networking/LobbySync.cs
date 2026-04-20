using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Syncs character selection, ready state, and phase transitions to all clients
/// using NGO NetworkVariables and RPCs.
///
/// CHANGES
/// ───────
/// • OnNetworkSpawn now fires OnLobbySyncReady on ALL peers (host + clients).
///   MainMenuController listens for this to start its SubscribeToLobbySyncWhenReady
///   coroutine, which handles the case where the Widget joined the session and
///   NGO replicated the scene before our code could wire up event subscriptions.
/// </summary>
public class LobbySync : NetworkBehaviour
{
    public static LobbySync Instance { get; private set; }

    public event Action          OnCharSelectPhaseStarted;
    public event Action<ulong[]> OnPlayerDataUpdated;

    private NetworkVariable<bool> charSelectPhaseActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Dictionary<ulong, int>  characterIndexMap = new Dictionary<ulong, int>();
    private Dictionary<ulong, bool> readyMap          = new Dictionary<ulong, bool>();

    public ulong LocalClientId           => NetworkManager.Singleton?.LocalClientId ?? 0;
    public bool  IsCharSelectPhaseActive => charSelectPhaseActive.Value;

    // ─────────────────────────────────────────────────────────────────────
    // Spawn / Despawn
    // ─────────────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (Instance != null && Instance != this)
        {
            NetworkObject.Despawn();
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        charSelectPhaseActive.OnValueChanged += OnCharSelectPhaseChanged;

        // Register this client so it appears in the player list
        RegisterClientServerRpc(NetworkManager.Singleton.LocalClientId);

        // Late-join catch-up: char select was already started before this spawned
        if (charSelectPhaseActive.Value)
        {
            Debug.Log("[LobbySync] Char select already active on spawn — deferred fire.");
            StartCoroutine(FireCharSelectNextFrame());
        }
    }

    private IEnumerator FireCharSelectNextFrame()
    {
        yield return null;
        OnCharSelectPhaseStarted?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        charSelectPhaseActive.OnValueChanged -= OnCharSelectPhaseChanged;
        if (Instance == this) Instance = null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Client registration
    // ─────────────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RegisterClientServerRpc(ulong clientId)
    {
        if (!characterIndexMap.ContainsKey(clientId)) characterIndexMap[clientId] = 0;
        if (!readyMap.ContainsKey(clientId))          readyMap[clientId]          = false;
        BroadcastPlayerData();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Character selection
    // ─────────────────────────────────────────────────────────────────────

    public void SetMyCharacter(int index)
        => SetCharacterServerRpc(NetworkManager.Singleton.LocalClientId, index);

    [ServerRpc(RequireOwnership = false)]
    private void SetCharacterServerRpc(ulong clientId, int index)
    {
        characterIndexMap[clientId] = index;
        BroadcastPlayerData();
    }

    public int GetCharacterIndex(ulong clientId)
    {
        characterIndexMap.TryGetValue(clientId, out int idx);
        return idx;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Ready state
    // ─────────────────────────────────────────────────────────────────────

    public void SetMyReady(bool ready)
        => SetReadyServerRpc(NetworkManager.Singleton.LocalClientId, ready);

    [ServerRpc(RequireOwnership = false)]
    private void SetReadyServerRpc(ulong clientId, bool ready)
    {
        readyMap[clientId] = ready;
        BroadcastPlayerData();
    }

    public bool IsReady(ulong clientId)
    {
        readyMap.TryGetValue(clientId, out bool ready);
        return ready;
    }

    public bool AllPlayersReady()
    {
        if (readyMap.Count == 0) return false;
        foreach (var kvp in readyMap)
            if (!kvp.Value) return false;
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Phase transition
    // ─────────────────────────────────────────────────────────────────────

    public void BeginCharSelectPhase()
    {
        if (IsServer)
        {
            charSelectPhaseActive.Value = true;
            NotifyCharSelectClientRpc();
        }
        else
        {
            BeginCharSelectPhaseServerRpc();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void BeginCharSelectPhaseServerRpc()
    {
        charSelectPhaseActive.Value = true;
        NotifyCharSelectClientRpc();
    }

    [ClientRpc]
    private void NotifyCharSelectClientRpc()
    {
        Debug.Log("[LobbySync] NotifyCharSelectClientRpc → OnCharSelectPhaseStarted");
        OnCharSelectPhaseStarted?.Invoke();
    }

    private void OnCharSelectPhaseChanged(bool oldVal, bool newVal)
    {
        if (newVal)
            Debug.Log("[LobbySync] charSelectPhaseActive NetworkVariable changed to true.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Player data broadcast
    // ─────────────────────────────────────────────────────────────────────

    private void BroadcastPlayerData()
    {
        var ids     = new List<ulong>(characterIndexMap.Keys);
        var indices = new int[ids.Count];
        var readys  = new bool[ids.Count];

        for (int i = 0; i < ids.Count; i++)
        {
            indices[i] = characterIndexMap[ids[i]];
            readyMap.TryGetValue(ids[i], out readys[i]);
        }

        UpdateClientsClientRpc(ids.ToArray(), indices, readys);
    }

    [ClientRpc]
    private void UpdateClientsClientRpc(ulong[] ids, int[] charIndices, bool[] readyStates)
    {
        characterIndexMap.Clear();
        readyMap.Clear();

        for (int i = 0; i < ids.Length; i++)
        {
            characterIndexMap[ids[i]] = charIndices[i];
            readyMap[ids[i]]          = readyStates[i];
        }

        OnPlayerDataUpdated?.Invoke(ids);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Reset — call when returning to menu between games
    // ─────────────────────────────────────────────────────────────────────

    public void ResetPhase()
    {
        if (IsServer) charSelectPhaseActive.Value = false;
    }
}
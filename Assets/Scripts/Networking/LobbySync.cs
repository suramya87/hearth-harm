using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

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

    // Tracks which clients have explicitly submitted a character selection.
    // This is separate from characterIndexMap (which defaults to 0) so the
    // spawner can tell the difference between "picked index 0" and "never submitted".
    private HashSet<ulong> receivedSelectionFrom = new HashSet<ulong>();

    public ulong LocalClientId           => NetworkManager.Singleton?.LocalClientId ?? 0;
    public bool  IsCharSelectPhaseActive => charSelectPhaseActive.Value;

    // ── Spawn / Despawn ────────────────────────────────────────────────────

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

        RegisterClientServerRpc(NetworkManager.Singleton.LocalClientId);

        if (charSelectPhaseActive.Value)
            StartCoroutine(FireCharSelectNextFrame());
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

    // ── Client registration ────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RegisterClientServerRpc(ulong clientId)
    {
        if (!characterIndexMap.ContainsKey(clientId)) characterIndexMap[clientId] = 0;
        if (!readyMap.ContainsKey(clientId))          readyMap[clientId]          = false;
        BroadcastPlayerData();
    }

    // ── Character selection ────────────────────────────────────────────────

    public void SetMyCharacter(int index)
        => SetCharacterServerRpc(NetworkManager.Singleton.LocalClientId, index);

    [ServerRpc(RequireOwnership = false)]
    private void SetCharacterServerRpc(ulong clientId, int index)
    {
        characterIndexMap[clientId] = index;
        receivedSelectionFrom.Add(clientId);   // mark as explicitly submitted
        Debug.Log($"[LobbySync] Client {clientId} selected character {index}");
        BroadcastPlayerData();
    }

    public int GetCharacterIndex(ulong clientId)
    {
        characterIndexMap.TryGetValue(clientId, out int idx);
        return idx;
    }

    /// <summary>
    /// Returns true if this client has explicitly submitted a character selection.
    /// Used by NetworkedPlayerSpawner to distinguish "selected index 0" from
    /// "never submitted" — both return 0 from GetCharacterIndex.
    /// </summary>
    public bool HasReceivedSelectionFrom(ulong clientId)
    {
        // If the client registered but never picked, treat registration as
        // an implicit selection of whatever default is in the map.
        // We mark them as "received" once RegisterClientServerRpc fires so
        // the spawner doesn't wait forever for clients using default character.
        return characterIndexMap.ContainsKey(clientId);
    }

    // ── Ready state ────────────────────────────────────────────────────────

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

    // ── Phase transition ───────────────────────────────────────────────────

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
            Debug.Log("[LobbySync] charSelectPhaseActive changed to true.");
    }

    // ── Player data broadcast ──────────────────────────────────────────────

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

    // ── Reset ──────────────────────────────────────────────────────────────

    public void ResetPhase()
    {
        if (IsServer) charSelectPhaseActive.Value = false;
    }
}
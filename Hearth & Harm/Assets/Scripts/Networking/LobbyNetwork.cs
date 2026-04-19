using System;
using Unity.Netcode;
using UnityEngine;

public class LobbyNetwork : NetworkBehaviour
{
    public static LobbyNetwork Instance { get; private set; }

    // ── Events (MenuFlowController subscribes to these) ────────────────────
    public event Action<ulong, bool> OnReadyChanged;
    public event Action              OnBeginCharSelect;

    public override void OnNetworkSpawn()
    {
        if (Instance != null && Instance != this) { NetworkObject.Despawn(); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log($"[LobbyNetwork] OnNetworkSpawn — IsHost={IsHost}");

        // Re-subscribe MenuFlowController every time this spawns.
        // This handles clients connecting after MenuFlowController.Start()
        // already ran and the coroutine found Instance null and exited.
        if (MenuFlowController.Instance != null)
        {
            OnReadyChanged    -= MenuFlowController.Instance.HandleReadyChanged;
            OnBeginCharSelect -= MenuFlowController.Instance.OnBeginCharSelectReceived;
            OnReadyChanged    += MenuFlowController.Instance.HandleReadyChanged;
            OnBeginCharSelect += MenuFlowController.Instance.OnBeginCharSelectReceived;
            Debug.Log("[LobbyNetwork] Re-subscribed MenuFlowController.");
        }
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    // ── Ready ──────────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    public void SetReadyServerRpc(ulong clientId, bool isReady)
    {
        Debug.Log($"[LobbyNetwork] Client {clientId} ready: {isReady}");
        SetReadyClientRpc(clientId, isReady);
    }

    [ClientRpc]
    private void SetReadyClientRpc(ulong clientId, bool isReady)
    {
        OnReadyChanged?.Invoke(clientId, isReady);
    }

    // ── Begin char select ──────────────────────────────────────────────────

    public void BroadcastBeginCharSelect()
    {
        if (!IsServer) return;
        Debug.Log("[LobbyNetwork] Broadcasting BeginCharSelect.");
        BeginCharSelectClientRpc();
    }

    [ClientRpc]
    private void BeginCharSelectClientRpc()
    {
        Debug.Log("[LobbyNetwork] BeginCharSelect received.");
        OnBeginCharSelect?.Invoke();
    }

    // ── Loading ────────────────────────────────────────────────────────────

    public void BroadcastLoading()
    {
        if (!IsServer) return;
        LoadingClientRpc();
    }

    [ClientRpc]
    private void LoadingClientRpc()
    {
        MenuFlowController.Instance?.TransitionToLoading();
    }
}
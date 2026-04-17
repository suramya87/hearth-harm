using Unity.Netcode;
using UnityEngine;

public class LobbyNetwork : NetworkBehaviour
{
    public static LobbyNetwork Instance { get; private set; }

    public override void OnNetworkSpawn()
    {
        if (Instance != null && Instance != this) { NetworkObject.Despawn(); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    // ── Ready tracking ─────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    public void SetReadyServerRpc(ulong clientId, bool isReady)
    {
        var menu = FindObjectOfType<MultiplayerMenuController>();
        if (menu != null) menu.HandleReadyChanged(clientId, isReady);
        Debug.Log($"[LobbyNetwork] Client {clientId} ready: {isReady}");
    }

    // ── Panel broadcasts (host → all clients) ──────────────────────────────

    public void BroadcastBeginCharSelect()
    {
        if (!IsHost) return;
        BeginCharSelectClientRpc();
    }

    [ClientRpc]
    private void BeginCharSelectClientRpc()
    {
        MenuFlowController.Instance?.TransitionToMPCharSelect();
    }

    public void BroadcastLoading()
    {
        if (!IsHost) return;
        LoadingClientRpc();
    }

    [ClientRpc]
    private void LoadingClientRpc()
    {
        MenuFlowController.Instance?.TransitionToLoading();
    }
}
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;


public class NetworkedTurnSystem : NetworkBehaviour
{
    public static NetworkedTurnSystem Instance { get; private set; }

    // ── Network variables ──────────────────────────────────────────────────

    private NetworkVariable<int> turnNumber = new(1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isPlayerPhase = new(true,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Events ─────────────────────────────────────────────────────────────

    public event Action       OnPlayerTurnBegin;
    public event Action       OnEnemyPhaseBegin;
    public event Action       OnEnemyPhaseEnd;
    public event EventHandler OnTurnChanged;


    public event Action<ulong[], bool[]> OnTurnStatusUpdated;

    // ── State ──────────────────────────────────────────────────────────────

    public bool IsPlayerPhase => isPlayerPhase.Value;
    public int  TurnNumber    => turnNumber.Value;

    private readonly HashSet<ulong> playersEndedTurn = new();

    private bool localEndTurnPending;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (!GameManager.IsMultiplayer) return;

        isPlayerPhase.OnValueChanged += OnPhaseChanged;
        turnNumber.OnValueChanged    += OnTurnNumberChanged;

        if (IsServer && EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnsComplete += ServerHandleEnemyTurnsComplete;
    }

    public override void OnNetworkDespawn()
    {
        isPlayerPhase.OnValueChanged -= OnPhaseChanged;
        turnNumber.OnValueChanged    -= OnTurnNumberChanged;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnsComplete -= ServerHandleEnemyTurnsComplete;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void RequestEndTurn()
    {
        if (!GameManager.IsMultiplayer)
        {
            TurnSystem.Instance?.NextTurn();
            return;
        }

        if (!isPlayerPhase.Value || localEndTurnPending) return;
        if (NetworkManager.Singleton == null) return;

        localEndTurnPending = true;
        RequestEndTurnServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    public bool HasLocalPlayerEndedTurn()
    {
        if (NetworkManager.Singleton == null) return false;
        return localEndTurnPending ||
               playersEndedTurn.Contains(NetworkManager.Singleton.LocalClientId);
    }

    // ── Server RPCs ────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RequestEndTurnServerRpc(ulong clientId)
    {
        if (!isPlayerPhase.Value)
        {
            Debug.Log($"[NetworkedTurnSystem] Ignored end-turn from {clientId} (not player phase).");
            return;
        }

        playersEndedTurn.Add(clientId);
        Debug.Log($"[NetworkedTurnSystem] {clientId} ended turn " +
                  $"({playersEndedTurn.Count} / {GetRelevantPlayerCount()} relevant players)");

        BroadcastTurnStatus();

        if (AllRelevantPlayersEndedTurn())
            ServerBeginEnemyPhase();
    }

    // ── Relevant player calculation ────────────────────────────────────────
    private int GetRelevantPlayerCount()
    {
        return NetworkManager.Singleton.ConnectedClientsList.Count;

    }

    private bool AllRelevantPlayersEndedTurn()
    {
        // All connected clients must have ended turn
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            if (!playersEndedTurn.Contains(client.ClientId)) return false;
        return true;
    }


    // ── Server logic ───────────────────────────────────────────────────────

    private void ServerBeginEnemyPhase()
    {
        playersEndedTurn.Clear();
        turnNumber.Value++;
        isPlayerPhase.Value = false;

        NotifyEnemyPhaseBeginClientRpc();

        // Run enemy AI on server — clients see results via NetworkedEnemyBridge RPCs
        if (EnemyManager.Instance != null && EnemyManager.Instance.GetEnemyCount() > 0)
        {
            Debug.Log($"[NetworkedTurnSystem] Starting enemy phase with {EnemyManager.Instance.GetEnemyCount()} enemies.");
            EnemyManager.Instance.RunEnemyTurns();
        }
        else
        {
            Debug.Log("[NetworkedTurnSystem] No enemies — skipping to player phase.");
            ServerHandleEnemyTurnsComplete();
        }
    }

    private void ServerHandleEnemyTurnsComplete()
    {
        isPlayerPhase.Value = true;
        NotifyPlayerPhaseBeginClientRpc();
        Debug.Log($"[NetworkedTurnSystem] Enemy phase complete. Turn {turnNumber.Value} → player phase.");
    }

    // ── Turn status broadcast ──────────────────────────────────────────────

    private void BroadcastTurnStatus()
    {
        var clients  = NetworkManager.Singleton.ConnectedClientsList;
        var ids      = new ulong[clients.Count];
        var statuses = new bool[clients.Count];

        for (int i = 0; i < clients.Count; i++)
        {
            ids[i]      = clients[i].ClientId;
            statuses[i] = playersEndedTurn.Contains(clients[i].ClientId);
        }

        UpdateTurnStatusClientRpc(ids, statuses);
    }

    // ── Client RPCs ────────────────────────────────────────────────────────

    [ClientRpc]
    private void UpdateTurnStatusClientRpc(ulong[] clientIds, bool[] endedTurn)
    {
        OnTurnStatusUpdated?.Invoke(clientIds, endedTurn);
    }

    [ClientRpc]
    private void NotifyEnemyPhaseBeginClientRpc()
    {
        OnEnemyPhaseBegin?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        Debug.Log("[NetworkedTurnSystem] Enemy phase begin (client).");
    }

    [ClientRpc]
    private void NotifyPlayerPhaseBeginClientRpc()
    {
        localEndTurnPending = false;

        OnPlayerTurnBegin?.Invoke();
        OnEnemyPhaseEnd?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        Debug.Log($"[NetworkedTurnSystem] Player turn {turnNumber.Value} begin (client).");
    }


    private void OnPhaseChanged(bool oldVal, bool newVal)      { }
    private void OnTurnNumberChanged(int oldVal, int newVal)   { }

    // ── Force helpers ──────────────────────────────────────────────────────

    public void RequestForcePlayerTurn()
    {
        if (!GameManager.IsMultiplayer)
        {
            TurnSystem.Instance?.ForcePlayerTurn();
            return;
        }
        ForcePlayerTurnServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ForcePlayerTurnServerRpc()
    {
        playersEndedTurn.Clear();
        isPlayerPhase.Value = true;
        NotifyPlayerPhaseBeginClientRpc();
    }
}
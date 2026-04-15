using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;


public class NetworkedTurnSystem : NetworkBehaviour
{
    public static NetworkedTurnSystem Instance { get; private set; }

    // ── Network state ──────────────────────────────────────────────────────

    private NetworkVariable<int>  turnNumber    = new(1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isPlayerPhase = new(true,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);


    public event Action           OnPlayerTurnBegin;
    public event Action           OnEnemyPhaseBegin;
    public event Action           OnEnemyPhaseEnd;
    public event EventHandler     OnTurnChanged;

    // ── State ──────────────────────────────────────────────────────────────

    public bool IsPlayerPhase => isPlayerPhase.Value;
    public int  TurnNumber    => turnNumber.Value;

    private readonly HashSet<ulong> playersEndedTurn = new();

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

        // Subscribe to enemy manager so server knows when enemy phase ends
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


    public void RequestEndTurn()
    {
        if (!GameManager.IsMultiplayer)
        {
            TurnSystem.Instance?.NextTurn();
            return;
        }

        if (!isPlayerPhase.Value) return;
        RequestEndTurnServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    // ── Server RPCs ────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RequestEndTurnServerRpc(ulong clientId)
    {
        if (!isPlayerPhase.Value) return;

        playersEndedTurn.Add(clientId);
        Debug.Log($"[NetworkedTurnSystem] Player {clientId} ended turn " +
                  $"({playersEndedTurn.Count}/{NetworkManager.Singleton.ConnectedClientsList.Count})");

        if (AllPlayersEndedTurn())
            ServerBeginEnemyPhase();
    }

    // ── Server logic ───────────────────────────────────────────────────────

    private bool AllPlayersEndedTurn()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            if (!playersEndedTurn.Contains(client.ClientId)) return false;
        return true;
    }

    private void ServerBeginEnemyPhase()
    {
        playersEndedTurn.Clear();
        turnNumber.Value++;
        isPlayerPhase.Value = false;

        NotifyEnemyPhaseBeginClientRpc();

        // Run enemy AI (server only — clients just watch the results)
        if (EnemyManager.Instance != null && EnemyManager.Instance.GetEnemyCount() > 0)
            EnemyManager.Instance.RunEnemyTurns();
        else
            ServerHandleEnemyTurnsComplete();
    }

    private void ServerHandleEnemyTurnsComplete()
    {
        isPlayerPhase.Value = true;
        NotifyPlayerPhaseBeginClientRpc();
    }

    // ── Client RPCs ────────────────────────────────────────────────────────

    [ClientRpc]
    private void NotifyEnemyPhaseBeginClientRpc()
    {
        OnEnemyPhaseBegin?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        Debug.Log("[NetworkedTurnSystem] Enemy phase begin.");
    }

    [ClientRpc]
    private void NotifyPlayerPhaseBeginClientRpc()
    {
        OnPlayerTurnBegin?.Invoke();
        OnEnemyPhaseEnd?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        Debug.Log($"[NetworkedTurnSystem] Player turn {turnNumber.Value} begin.");
    }

    // ── Phase change callbacks ─────────────────────────────────────────────

    private void OnPhaseChanged(bool oldVal, bool newVal) { /* NetworkVariables handled via RPCs above */ }
    private void OnTurnNumberChanged(int oldVal, int newVal) { }



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
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkedTurnSystem : NetworkBehaviour
{
    public static NetworkedTurnSystem Instance { get; private set; }

    // ── Network state ──────────────────────────────────────────────────────

    private NetworkVariable<int> turnNumber = new(1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isPlayerPhase = new(true,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public event Action       OnPlayerTurnBegin;
    public event Action       OnEnemyPhaseBegin;
    public event Action       OnEnemyPhaseEnd;
    public event EventHandler OnTurnChanged;

    public bool IsPlayerPhase => isPlayerPhase.Value;
    public int  TurnNumber    => turnNumber.Value;

    private readonly HashSet<ulong> playersEndedTurn = new();

    // Lightweight debounce — reset only when server confirms new player phase.
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

        // ── FIX: removed client-side  if (!isPlayerPhase.Value) return ────
        // The NetworkVariable arrives with a delay. The client's value can be
        // stale — server may already have changed phase while client still reads
        // true (or vice versa). The server's ServerRpc already guards the phase.
        // We only debounce locally so the button can't be spammed mid-flight.
        if (localEndTurnPending) return;
        if (NetworkManager.Singleton == null) return;

        localEndTurnPending = true;
        RequestEndTurnServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    // ── Server RPCs ────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RequestEndTurnServerRpc(ulong clientId)
    {
        // Server is the single source of truth on phase.
        if (!isPlayerPhase.Value)
        {
            Debug.Log($"[NetworkedTurnSystem] Ignored end-turn from {clientId} — not player phase.");
            return;
        }

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
        // Reset debounce only on the server's authoritative signal.
        localEndTurnPending = false;

        OnPlayerTurnBegin?.Invoke();
        OnEnemyPhaseEnd?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        Debug.Log($"[NetworkedTurnSystem] Player turn {turnNumber.Value} begin.");
    }

    // ── NetworkVariable callbacks ──────────────────────────────────────────

    private void OnPhaseChanged(bool oldVal, bool newVal) { }
    private void OnTurnNumberChanged(int oldVal, int newVal) { }

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
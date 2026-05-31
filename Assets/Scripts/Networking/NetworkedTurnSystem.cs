using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkedTurnSystem : NetworkBehaviour
{
    public static NetworkedTurnSystem Instance { get; private set; }

    private NetworkVariable<int> turnNumber = new(1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private bool localIsPlayerPhase = true;

    public bool IsPlayerPhase => localIsPlayerPhase;
    public int  TurnNumber    => turnNumber.Value;

    public bool HasLocalPlayerEndedTurn() => !localIsPlayerPhase;

    public event Action       OnPlayerTurnBegin;
    public event Action       OnEnemyPhaseBegin;
    public event Action       OnEnemyPhaseEnd;
    public event EventHandler OnTurnChanged;

    public event Action<ulong[], bool[]> OnTurnStatusUpdated;

    private readonly HashSet<ulong> clientsInEnemyPhase = new();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (!GameManager.IsMultiplayer) return;
        turnNumber.OnValueChanged += OnTurnNumberChanged;
    }

    public override void OnNetworkDespawn()
    {
        turnNumber.OnValueChanged -= OnTurnNumberChanged;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void RequestEndTurn()
    {
        if (!GameManager.IsMultiplayer)
        {
            TurnSystem.Instance?.NextTurn();
            return;
        }

        if (!localIsPlayerPhase) return;
        if (NetworkManager.Singleton == null) return;

        string roomName = GetLocalPlayerRoomName();
        ulong  clientId = NetworkManager.Singleton.LocalClientId;

        RequestEndTurnServerRpc(clientId, roomName);
    }

    // ── Server RPC ─────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RequestEndTurnServerRpc(ulong clientId, string roomName)
    {
        if (clientsInEnemyPhase.Contains(clientId))
        {
            Debug.Log($"[NetworkedTurnSystem] {clientId} already in enemy phase — ignored.");
            return;
        }

        clientsInEnemyPhase.Add(clientId);
        Debug.Log($"[NetworkedTurnSystem] Client {clientId} ended turn in '{roomName}'.");

        BeginEnemyPhaseForClientRpc(clientId);

        StartCoroutine(RunEnemyTurnsForClient(clientId, roomName));
    }

    // ── Enemy turn execution ───────────────────────────────────────────────

    private IEnumerator RunEnemyTurnsForClient(ulong clientId, string roomName)
    {
        if (string.IsNullOrEmpty(roomName))
        {
            Debug.Log($"[NetworkedTurnSystem] Client {clientId} has no room — returning turn.");
            FinishEnemyPhaseForClient(clientId);
            yield break;
        }

        RoomGrid room = FindRoomGridByName(roomName);
        if (room == null)
        {
            Debug.LogWarning($"[NetworkedTurnSystem] Room '{roomName}' not found — returning turn.");
            FinishEnemyPhaseForClient(clientId);
            yield break;
        }

        var enemies = EnemyManager.Instance?.GetEnemiesInRoom(room);
        if (enemies == null || enemies.Count == 0)
        {
            Debug.Log($"[NetworkedTurnSystem] No enemies in '{roomName}' — returning turn immediately.");
            FinishEnemyPhaseForClient(clientId);
            yield break;
        }

        Debug.Log($"[NetworkedTurnSystem] Running {enemies.Count} enemies in '{roomName}' for client {clientId}.");

        if (EnemyTurnQueue.Instance != null)
            EnemyTurnQueue.Instance.BuildQueue(room, enemies);

        bool done    = false;
        void OnDone() => done = true;

        EnemyManager.Instance.OnEnemyTurnsComplete += OnDone;
        EnemyManager.Instance.RunEnemyTurns();

        float timeout = 60f, elapsed = 0f;
        while (!done && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        EnemyManager.Instance.OnEnemyTurnsComplete -= OnDone;

        if (!done)
            Debug.LogWarning($"[NetworkedTurnSystem] Enemy turns timed out for '{roomName}'.");

        FinishEnemyPhaseForClient(clientId);
    }

    private void FinishEnemyPhaseForClient(ulong clientId)
    {
        clientsInEnemyPhase.Remove(clientId);
        turnNumber.Value++;
        ReturnTurnToClientRpc(clientId);
        Debug.Log($"[NetworkedTurnSystem] Returned turn to client {clientId}.");
    }

    // ── Client RPCs ────────────────────────────────────────────────────────

    [ClientRpc]
    private void BeginEnemyPhaseForClientRpc(ulong targetClientId)
    {
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;

        localIsPlayerPhase = false;
        OnEnemyPhaseBegin?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        Debug.Log("[NetworkedTurnSystem] My enemy phase began.");
    }

    [ClientRpc]
    private void ReturnTurnToClientRpc(ulong targetClientId)
    {
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;

        localIsPlayerPhase = true;
        OnPlayerTurnBegin?.Invoke();
        OnEnemyPhaseEnd?.Invoke();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        Debug.Log("[NetworkedTurnSystem] My player turn began.");
    }

    // ── Force helpers ──────────────────────────────────────────────────────

    public void RequestForcePlayerTurn()
    {
        if (!GameManager.IsMultiplayer)
        {
            TurnSystem.Instance?.ForcePlayerTurn();
            return;
        }

        if (NetworkManager.Singleton == null) return;
        ForcePlayerTurnServerRpc(NetworkManager.Singleton.LocalClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void ForcePlayerTurnServerRpc(ulong clientId)
    {
        clientsInEnemyPhase.Remove(clientId);
        ReturnTurnToClientRpc(clientId);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string GetLocalPlayerRoomName()
    {
        foreach (var u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var net = u.GetComponent<Unity.Netcode.NetworkObject>();
            if (net != null && net.IsOwner)
                return u.GetCurrentRoomGrid()?.gameObject.name ?? "";
        }
        return "";
    }

    private static RoomGrid FindRoomGridByName(string name)
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return null;
        foreach (var placed in gen.GetAllRooms())
            if (placed.roomGrid != null && placed.roomGrid.gameObject.name == name)
                return placed.roomGrid;
        return null;
    }

    private void OnTurnNumberChanged(int oldVal, int newVal) { }
}
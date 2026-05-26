using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkedTurnSystem : NetworkBehaviour
{
    public static NetworkedTurnSystem Instance { get; private set; }

    private NetworkVariable<int> turnNumber = new(1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isPlayerPhase = new(true,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public event Action       OnPlayerTurnBegin;
    public event Action       OnEnemyPhaseBegin;
    public event Action       OnEnemyPhaseEnd;
    public event EventHandler OnTurnChanged;
    public event Action<ulong[], bool[]> OnTurnStatusUpdated;

    public bool IsPlayerPhase => isPlayerPhase.Value;
    public int  TurnNumber    => turnNumber.Value;

    private readonly HashSet<ulong> playersEndedTurn = new();
    private bool localEndTurnPending;

    // Tracks which rooms have active players in them (server only).
    // Used so enemy turns run in every room that has at least one player,
    // not just the host's room.
    private readonly HashSet<string> activeRoomNames = new();

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

        // Tell the server which room this player is currently in so it can
        // run enemies for that room during the enemy phase.
        string roomName = GetLocalPlayerRoomName();
        RequestEndTurnServerRpc(NetworkManager.Singleton.LocalClientId, roomName);
    }

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

    public bool HasLocalPlayerEndedTurn()
    {
        if (NetworkManager.Singleton == null) return false;
        return localEndTurnPending ||
               playersEndedTurn.Contains(NetworkManager.Singleton.LocalClientId);
    }

    // ── Server RPCs ────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void RequestEndTurnServerRpc(ulong clientId, string roomName)
    {
        if (!isPlayerPhase.Value)
        {
            Debug.Log($"[NetworkedTurnSystem] Ignored end-turn from {clientId} (not player phase).");
            return;
        }

        playersEndedTurn.Add(clientId);

        // Record this room as active so enemies there get their turn.
        if (!string.IsNullOrEmpty(roomName))
            activeRoomNames.Add(roomName);

        Debug.Log($"[NetworkedTurnSystem] {clientId} ended turn in '{roomName}' " +
                  $"({playersEndedTurn.Count}/{GetRelevantPlayerCount()})");

        BroadcastTurnStatus();

        if (AllRelevantPlayersEndedTurn())
            ServerBeginEnemyPhase();
    }

    private int GetRelevantPlayerCount()
        => NetworkManager.Singleton.ConnectedClientsList.Count;

    private bool AllRelevantPlayersEndedTurn()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            if (!playersEndedTurn.Contains(client.ClientId)) return false;
        return true;
    }

    // ── Enemy phase ────────────────────────────────────────────────────────

    private void ServerBeginEnemyPhase()
    {
        playersEndedTurn.Clear();
        turnNumber.Value++;
        isPlayerPhase.Value = false;

        NotifyEnemyPhaseBeginClientRpc();

        // Run enemy turns for every room that has an active player.
        // This fixes the bug where only the host's room got enemy turns.
        if (EnemyManager.Instance != null)
        {
            var allEnemies = EnemyManager.Instance.GetAllEnemies();

            // Collect all rooms that have enemies AND have a player in them.
            var roomsToProcess = new HashSet<RoomGrid>();

            foreach (var roomName in activeRoomNames)
            {
                // Find the RoomGrid by name.
                RoomGrid found = FindRoomGridByName(roomName);
                if (found != null && EnemyManager.Instance.GetEnemiesInRoom(found).Count > 0)
                    roomsToProcess.Add(found);
            }

            // Also include rooms where ALL enemies live (for host-only games or
            // rooms the server knows about even without an explicit player report).
            if (roomsToProcess.Count == 0)
            {
                // Fallback: run all enemies like before.
                if (allEnemies.Count > 0)
                {
                    Debug.Log($"[NetworkedTurnSystem] No active rooms reported — running all {allEnemies.Count} enemies.");
                    EnemyManager.Instance.RunEnemyTurns();
                    return;
                }
                else
                {
                    Debug.Log("[NetworkedTurnSystem] No enemies — skipping to player phase.");
                    ServerHandleEnemyTurnsComplete();
                    return;
                }
            }

            Debug.Log($"[NetworkedTurnSystem] Running enemy turns for {roomsToProcess.Count} active room(s).");
            StartCoroutine(RunEnemyTurnsForRooms(new List<RoomGrid>(roomsToProcess)));
        }
        else
        {
            ServerHandleEnemyTurnsComplete();
        }
    }

    private System.Collections.IEnumerator RunEnemyTurnsForRooms(List<RoomGrid> rooms)
    {
        foreach (var room in rooms)
        {
            var enemies = EnemyManager.Instance?.GetEnemiesInRoom(room);
            if (enemies == null || enemies.Count == 0) continue;

            Debug.Log($"[NetworkedTurnSystem] Running {enemies.Count} enemies in {room.gameObject.name}");

            // Tell EnemyManager to run turns for this specific room.
            // We temporarily set the queue for this room then run.
            if (EnemyTurnQueue.Instance != null)
                EnemyTurnQueue.Instance.BuildQueue(room, enemies);

            bool done = false;
            EnemyManager.Instance.OnEnemyTurnsComplete += OnRoomDone;
            EnemyManager.Instance.RunEnemyTurns();

            float timeout = 30f;
            float elapsed = 0f;
            while (!done && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            EnemyManager.Instance.OnEnemyTurnsComplete -= OnRoomDone;

            void OnRoomDone() => done = true;
        }

        activeRoomNames.Clear();
        ServerHandleEnemyTurnsComplete();
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

    private void ServerHandleEnemyTurnsComplete()
    {
        activeRoomNames.Clear();
        isPlayerPhase.Value = true;
        NotifyPlayerPhaseBeginClientRpc();
        Debug.Log($"[NetworkedTurnSystem] Enemy phase complete → player turn {turnNumber.Value}.");
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
        => OnTurnStatusUpdated?.Invoke(clientIds, endedTurn);

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

    private void OnPhaseChanged(bool oldVal, bool newVal)    { }
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
        activeRoomNames.Clear();
        isPlayerPhase.Value = true;
        NotifyPlayerPhaseBeginClientRpc();
    }
}
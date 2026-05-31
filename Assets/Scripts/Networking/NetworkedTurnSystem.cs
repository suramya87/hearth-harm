using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkedTurnSystem : NetworkBehaviour
{
    public static NetworkedTurnSystem Instance { get; private set; }

    public event EventHandler OnTurnChanged;
    public event Action       OnPlayerTurnBegin;
    public event Action       OnEnemyPhaseBegin;

    // ── Networked state ────────────────────────────────────────────────────
    private NetworkVariable<int> turnNumber = new(1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<bool> isPlayerPhase = new(true,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int  TurnNumber    => turnNumber.Value;
    public bool IsPlayerPhase => isPlayerPhase.Value;

    // ── Per-room combat state (server-only) ────────────────────────────────
    private readonly Dictionary<string, HashSet<ulong>> roomCombatants = new();
    private readonly Dictionary<string, HashSet<ulong>> roomEndedTurns = new();
    private readonly HashSet<string> roomsInEnemyPhase = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        turnNumber.OnValueChanged    += (_, _) => OnTurnChanged?.Invoke(this, EventArgs.Empty);
        isPlayerPhase.OnValueChanged += OnPhaseChanged;

        if (IsServer && EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnsComplete += OnEnemyTurnsCompleteServer;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && EnemyManager.Instance != null)
            EnemyManager.Instance.OnEnemyTurnsComplete -= OnEnemyTurnsCompleteServer;
    }

    private void OnPhaseChanged(bool oldVal, bool newVal)
    {
        if (newVal) OnPlayerTurnBegin?.Invoke();
        else        OnEnemyPhaseBegin?.Invoke();
    }

    // ── Public API (called by TurnSystemUI) ────────────────────────────────

    public void RequestEndTurn()
    {
        var unit = UnitActionSystem.FindLocalOwnedUnit();
        if (unit == null) return;

        string roomName = unit.GetCurrentRoomGrid()?.gameObject.name ?? "";
        EndTurnServerRpc(NetworkManager.Singleton.LocalClientId, roomName);
    }

    // ── Room combat registration ───────────────────────────────────────────

    public void RegisterPlayerInRoomCombat(ulong clientId, string roomName)
    {
        if (!IsServer) return;

        if (!roomCombatants.ContainsKey(roomName))
        {
            roomCombatants[roomName] = new HashSet<ulong>();
            roomEndedTurns[roomName] = new HashSet<ulong>();
        }

        roomCombatants[roomName].Add(clientId);

        Debug.Log($"[NetworkedTurnSystem] Client {clientId} registered in '{roomName}' combat. " +
                  $"Total combatants: {roomCombatants[roomName].Count}");
    }

    /// <summary>
    /// Server-side: remove a player from a room's combat (they died, left, or room cleared).
    /// </summary>
    public void UnregisterPlayerFromRoomCombat(ulong clientId, string roomName)
    {
        if (!IsServer) return;
        if (!roomCombatants.ContainsKey(roomName)) return;

        roomCombatants[roomName].Remove(clientId);
        roomEndedTurns[roomName].Remove(clientId);

        if (roomCombatants[roomName].Count == 0)
        {
            roomCombatants.Remove(roomName);
            roomEndedTurns.Remove(roomName);
            roomsInEnemyPhase.Remove(roomName);
            Debug.Log($"[NetworkedTurnSystem] Room '{roomName}' combat fully cleared (no combatants).");
        }
        else
        {
            // Re-check in case remaining players had all already ended their turn
            CheckRoomTurnComplete(roomName);
        }
    }

    /// <summary>
    /// Server-side: called when a room is fully cleared of enemies.
    /// </summary>
    public void ClearRoomCombat(string roomName)
    {
        if (!IsServer) return;
        roomCombatants.Remove(roomName);
        roomEndedTurns.Remove(roomName);
        roomsInEnemyPhase.Remove(roomName);
        Debug.Log($"[NetworkedTurnSystem] Combat state cleared for room '{roomName}'.");
    }

    // ── Server RPCs ────────────────────────────────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void EndTurnServerRpc(ulong clientId, string roomName)
    {
        if (!string.IsNullOrEmpty(roomName) && roomCombatants.ContainsKey(roomName))
        {
            // Room has active combat — record this player's vote
            roomEndedTurns[roomName].Add(clientId);

            Debug.Log($"[NetworkedTurnSystem] Client {clientId} ended turn in '{roomName}'. " +
                      $"{roomEndedTurns[roomName].Count}/{roomCombatants[roomName].Count} players ready.");

            CheckRoomTurnComplete(roomName);
        }
        else
        {
            // No active combat in this room — global turn end (exploration)
            HandleGlobalEndTurn();
        }
    }

    // ── Per-room enemy phase ───────────────────────────────────────────────

    private void CheckRoomTurnComplete(string roomName)
    {
        if (!roomCombatants.ContainsKey(roomName)) return;
        if (roomsInEnemyPhase.Contains(roomName)) return;

        var combatants = roomCombatants[roomName];
        var ended      = roomEndedTurns[roomName];

        if (ended.Count >= combatants.Count && combatants.Count > 0)
        {
            StartCoroutine(RunRoomEnemyPhase(roomName));
        }
    }

    private IEnumerator RunRoomEnemyPhase(string roomName)
    {
        roomsInEnemyPhase.Add(roomName);
        roomEndedTurns[roomName].Clear(); // reset votes for next round

        Debug.Log($"[NetworkedTurnSystem] Starting enemy phase for room '{roomName}'.");

        // Tell all clients in this room to show enemy phase UI
        NotifyRoomPhaseClientRpc(roomName, false); // false = enemy phase

        // Find the room and enemies on the server
        var room = FindRoomGridByName(roomName);
        if (room == null)
        {
            Debug.LogWarning($"[NetworkedTurnSystem] RunRoomEnemyPhase: room '{roomName}' not found.");
            roomsInEnemyPhase.Remove(roomName);
            NotifyRoomPhaseClientRpc(roomName, true);
            yield break;
        }

        var enemies = EnemyManager.Instance?.GetEnemiesInRoom(room);
        if (enemies == null || enemies.Count == 0)
        {
            Debug.Log($"[NetworkedTurnSystem] No enemies in '{roomName}' — skipping enemy phase.");
            roomsInEnemyPhase.Remove(roomName);
            NotifyRoomPhaseClientRpc(roomName, true);
            RecoverStaminaForRoomClientRpc(roomName);
            yield break;
        }

        Debug.Log($"[NetworkedTurnSystem] Running {enemies.Count} enemy turns in '{roomName}'.");

        bool turnsComplete = false;
        Action onComplete  = () => turnsComplete = true;
        EnemyManager.Instance.OnEnemyTurnsComplete += onComplete;

        EnemyManager.Instance.RunEnemyTurnsForRoom(room, enemies);

        float timeout = 60f, elapsed = 0f;
        while (!turnsComplete && elapsed < timeout)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        EnemyManager.Instance.OnEnemyTurnsComplete -= onComplete;

        if (elapsed >= timeout)
            Debug.LogWarning($"[NetworkedTurnSystem] Enemy phase timed out for room '{roomName}'.");

        roomsInEnemyPhase.Remove(roomName);

        if (roomsInEnemyPhase.Count == 0)
        {
            turnNumber.Value++;
            Debug.Log($"[NetworkedTurnSystem] All rooms done. Turn {turnNumber.Value} begins.");
        }

        // Return clients in this room to player phase
        NotifyRoomPhaseClientRpc(roomName, true); // true = player phase
        RecoverStaminaForRoomClientRpc(roomName);
    }

    // ── Global end turn (exploration — no active combat) ───────────────────

    private void HandleGlobalEndTurn()
    {
        turnNumber.Value++;
        RecoverStaminaGlobalClientRpc();
        OnTurnChanged?.Invoke(this, EventArgs.Empty);
        Debug.Log($"[NetworkedTurnSystem] Global end turn. Turn {turnNumber.Value}.");
    }

    // ── Client RPCs ────────────────────────────────────────────────────────

    [ClientRpc]
    private void NotifyRoomPhaseClientRpc(string roomName, bool playerPhase)
    {
        var localUnit = UnitActionSystem.FindLocalOwnedUnit();
        if (localUnit == null) return;

        string localRoom = localUnit.GetCurrentRoomGrid()?.gameObject.name ?? "";
        if (localRoom != roomName) return;

        if (IsServer)
            isPlayerPhase.Value = playerPhase;

        if (playerPhase)
        {
            OnPlayerTurnBegin?.Invoke();
            OnTurnChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            OnEnemyPhaseBegin?.Invoke();
        }
    }

    [ClientRpc]
    private void RecoverStaminaForRoomClientRpc(string roomName)
    {
        var localUnit = UnitActionSystem.FindLocalOwnedUnit();
        if (localUnit == null) return;

        string localRoom = localUnit.GetCurrentRoomGrid()?.gameObject.name ?? "";
        if (localRoom != roomName) return;

        RecoverLocalPlayerStamina(localUnit);
        localUnit.GetMoveAction()?.InvalidateCache();
    }

    [ClientRpc]
    private void RecoverStaminaGlobalClientRpc()
    {
        var localUnit = UnitActionSystem.FindLocalOwnedUnit();
        if (localUnit == null) return;
        RecoverLocalPlayerStamina(localUnit);
        localUnit.GetMoveAction()?.InvalidateCache();
    }

    // ── EnemyManager callback (server) ─────────────────────────────────────

    private void OnEnemyTurnsCompleteServer()
    {
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static void RecoverLocalPlayerStamina(Unit unit)
    {
        var stats = unit.GetComponent<PlayerStats>();
        if (stats == null) return;
        int recovered = stats.RollStaminaRecovery();
        Debug.Log($"[NetworkedTurnSystem] Recovered {recovered} stamina for {unit.name}.");
    }

    private static RoomGrid FindRoomGridByName(string roomName)
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return null;
        foreach (var placed in gen.GetAllRooms())
            if (placed.roomGrid != null && placed.roomGrid.gameObject.name == roomName)
                return placed.roomGrid;
        return null;
    }
}
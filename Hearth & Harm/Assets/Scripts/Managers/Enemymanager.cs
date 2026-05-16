using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance { get; private set; }

    [SerializeField] private bool showDebugLogs;

    private readonly List<EnemyUnit> active = new();
    private bool running;

    public event Action OnEnemyTurnsComplete;
    public event Action OnEnemyListChanged;
    public event Action<RoomGrid> OnRoomCleared;

    [Header("Enemy Turn Pacing")]
    [SerializeField] private float delayBeforeEnemyTurn = 0.35f;
    [SerializeField] private float delayAfterEnemyTurn  = 0.45f;

    public event Action<EnemyUnit> OnEnemyTurnStarted;
    public event Action<EnemyUnit> OnEnemyTurnFinished;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // ── Registration ───────────────────────────────────────────────────────

    public void RegisterEnemy(EnemyUnit e)
    {
        if (active.Contains(e)) return;
        active.Add(e);
        if (showDebugLogs)
            Debug.Log($"[EnemyManager] +{e.Stats?.enemyName} total:{active.Count}");
        OnEnemyListChanged?.Invoke();
    }

    /// <summary>
    /// Primary death path — called from EnemyUnit.HandleDeath with the room
    /// captured before any cleanup. This guarantees OnRoomCleared fires even
    /// if the enemy's currentRoomGrid is nulled before Destroy completes.
    /// </summary>
    public void UnregisterEnemyFromRoom(EnemyUnit e, RoomGrid roomAtDeath)
    {
        if (!active.Remove(e)) return;

        EnemyTurnQueue.Instance?.RemoveEnemy(e);

        if (showDebugLogs)
            Debug.Log($"[EnemyManager] -{e.Stats?.enemyName} remain:{active.Count}");
        OnEnemyListChanged?.Invoke();

        if (roomAtDeath == null) return;

        if (GetEnemiesInRoom(roomAtDeath).Count == 0)
        {
            Debug.Log($"[EnemyManager] Room cleared: {roomAtDeath.gameObject.name}");
            roomAtDeath.MarkCleared();
            OnRoomCleared?.Invoke(roomAtDeath);

            ResetAllTriggersForRoom(roomAtDeath);
        }
    }

    public void UnregisterEnemy(EnemyUnit e)
    {
        var room = e.CurrentRoomGrid;
        if (!active.Remove(e)) return;

        EnemyTurnQueue.Instance?.RemoveEnemy(e);

        if (showDebugLogs)
            Debug.Log($"[EnemyManager] -{e.Stats?.enemyName} remain:{active.Count}");
        OnEnemyListChanged?.Invoke();

        if (room == null) return;

        if (GetEnemiesInRoom(room).Count == 0)
        {
            Debug.Log($"[EnemyManager] Room cleared: {room.gameObject.name}");
            room.MarkCleared();
            OnRoomCleared?.Invoke(room);
            ResetAllTriggersForRoom(room);
        }
    }

    private static void ResetAllTriggersForRoom(RoomGrid clearedRoom)
    {
        foreach (var et in FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None))
        {
            if (et == null) continue;
            var roomA = et.Hallway?.RoomA?.roomGrid;
            var roomB = et.Hallway?.RoomB?.roomGrid;
            bool aMatch = roomA != null && (roomA == clearedRoom ||
                roomA.gameObject.name == clearedRoom.gameObject.name);
            bool bMatch = roomB != null && (roomB == clearedRoom ||
                roomB.gameObject.name == clearedRoom.gameObject.name);
            if (aMatch || bMatch)
            {
                Debug.Log($"[EnemyManager] Resetting trigger → {et.DestinationRoom?.roomGrid?.name ?? "null"}");
                et.ResetTrigger();
            }
        }
    }

    public void ClearAllEnemies()
    {
        if (running) { StopAllCoroutines(); running = false; }
        foreach (var e in active) if (e != null) Destroy(e.gameObject);
        active.Clear();

        EnemyTurnQueue.Instance?.ClearQueue();

        OnEnemyListChanged?.Invoke();
        Debug.Log("[EnemyManager] All enemies cleared.");
    }

    // ── Queries ────────────────────────────────────────────────────────────

    public int             GetEnemyCount() => active.Count;
    public List<EnemyUnit> GetAllEnemies() => new(active);

    /// <summary>
    /// Returns only living (non-dead) enemies in the given room.
    /// Compares by GameObject name so references don't need to match exactly.
    /// </summary>
    public List<EnemyUnit> GetEnemiesInRoom(RoomGrid room)
    {
        var result = new List<EnemyUnit>();
        if (room == null) return result;

        string roomName = room.gameObject.name;
        foreach (var e in active)
        {
            if (e == null || e.IsDead) continue;
            if (e.CurrentRoomGrid == null) continue;
            if (e.CurrentRoomGrid == room ||
                e.CurrentRoomGrid.gameObject.name == roomName)
                result.Add(e);
        }
        return result;
    }

    // ── Turn execution ─────────────────────────────────────────────────────

    private void BuildQueueForCurrentRoom()
    {
        if (EnemyTurnQueue.Instance == null) return;

        RoomGrid room = null;

        var unit = FindLocalPlayerUnit();
        if (unit != null)
            room = unit.GetCurrentRoomGrid();

        if (room == null)
            room = RoomManager.Instance?.GetCurrentRoomGrid();

        if (room == null)
        {
            Debug.LogWarning("[EnemyManager] BuildQueueForCurrentRoom: no room found.");
            return;
        }

        var enemies = GetEnemiesInRoom(room);
        EnemyTurnQueue.Instance.BuildQueue(room, enemies);

        if (showDebugLogs)
            Debug.Log($"[EnemyManager] Queue built: {enemies.Count} enemies in " +
                      $"{room.gameObject.name}");
    }

    public void RunEnemyTurns()
    {
        if (running) return;
        BuildQueueForCurrentRoom();
        StartCoroutine(RunTurns());
    }

    private IEnumerator RunTurns()
    {
        running = true;

        List<EnemyUnit> queueSnapshot = EnemyTurnQueue.Instance != null
            ? EnemyTurnQueue.Instance.GetQueuedEnemies()
            : new List<EnemyUnit>();

        if (showDebugLogs)
            Debug.Log($"[EnemyManager] Running {queueSnapshot.Count} enemy turns.");

        foreach (var enemy in queueSnapshot)
        {
            if (enemy == null || enemy.IsDead) continue;

            var bossAI  = enemy.GetComponent<BossAI>();
            var ai      = enemy.GetComponent<EnemyAI>();
            var ranged  = enemy.GetComponent<RangedEnemyAI>();

            if (bossAI == null && ai == null && ranged == null) continue;

            if (showDebugLogs)
                Debug.Log($"[EnemyManager] Turn: {enemy.Stats?.enemyName ?? enemy.name}");

            OnEnemyTurnStarted?.Invoke(enemy);

            if (delayBeforeEnemyTurn > 0f)
                yield return new WaitForSeconds(delayBeforeEnemyTurn);

            bool done = false;

            if (bossAI != null)      bossAI.TakeTurn(() => done = true);
            else if (ranged != null) ranged.TakeTurn(() => done = true);
            else                     ai.TakeTurn(() => done = true);

            yield return new WaitUntil(() => done);

            if (delayAfterEnemyTurn > 0f)
                yield return new WaitForSeconds(delayAfterEnemyTurn);

            OnEnemyTurnFinished?.Invoke(enemy);

            if (enemy != null && !enemy.IsDead)
                EnemyTurnQueue.Instance?.RotateEnemyToBack(enemy);
        }

        running = false;
        if (showDebugLogs) Debug.Log("[EnemyManager] All turns done.");
        OnEnemyTurnsComplete?.Invoke();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Unit FindLocalPlayerUnit()
    {
        if (!GameManager.IsMultiplayer)
        {
            var pt = PlayerTarget.Instance;
            return pt?.GetUnit();
        }

        foreach (var u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var net = u.GetComponent<Unity.Netcode.NetworkObject>();
            if (net != null && net.IsOwner) return u;
        }
        return null;
    }
}
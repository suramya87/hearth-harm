using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks all living enemies, runs their turns, and fires events when rooms clear.
/// Single-player only — multiplayer extends this separately.
/// </summary>
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
    [SerializeField] private float delayAfterEnemyTurn = 0.45f;

    public event Action<EnemyUnit> OnEnemyTurnStarted;
    public event Action<EnemyUnit> OnEnemyTurnFinished;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // No DontDestroyOnLoad
    }

    // ── Registration ───────────────────────────────────────────────────────

    public void RegisterEnemy(EnemyUnit e)
    {
        if (active.Contains(e)) return;
        active.Add(e);
        if (showDebugLogs) Debug.Log($"[EnemyManager] +{e.Stats?.enemyName} total:{active.Count}");
        OnEnemyListChanged?.Invoke();
    }

    public void UnregisterEnemy(EnemyUnit e)
    {
        var room = e.CurrentRoomGrid;
        if (!active.Remove(e)) return;

        if (EnemyTurnQueue.Instance != null)
            EnemyTurnQueue.Instance.RemoveEnemy(e);

        if (showDebugLogs) Debug.Log($"[EnemyManager] -{e.Stats?.enemyName} remain:{active.Count}");
        OnEnemyListChanged?.Invoke();

        if (room != null && GetEnemiesInRoom(room).Count == 0)
        {
            Debug.Log($"[EnemyManager] Room cleared: {room.gameObject.name}");
            OnRoomCleared?.Invoke(room);
        }
    }

    public void ClearAllEnemies()
    {
        if (running) { StopAllCoroutines(); running = false; }
        foreach (var e in active) if (e != null) Destroy(e.gameObject);
        active.Clear();

        if (EnemyTurnQueue.Instance != null)
            EnemyTurnQueue.Instance.ClearQueue();

        OnEnemyListChanged?.Invoke();
        Debug.Log("[EnemyManager] All enemies cleared.");
    }

    // ── Queries ────────────────────────────────────────────────────────────

    public int                GetEnemyCount()          => active.Count;
    public List<EnemyUnit>    GetAllEnemies()           => new(active);
    public List<EnemyUnit>    GetEnemiesInRoom(RoomGrid room)
    {
        var result = new List<EnemyUnit>();
        foreach (var e in active)
            if (!e.IsDead && e.CurrentRoomGrid == room) result.Add(e);
        return result;
    }

    // ── Turn execution ─────────────────────────────────────────────────────
    private void BuildQueueForCurrentRoom()
    {
        RoomGrid room = RoomManager.Instance?.GetCurrentRoomGrid();
        if (room == null || EnemyTurnQueue.Instance == null) return;

        EnemyTurnQueue.Instance.BuildQueue(room, GetEnemiesInRoom(room));
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

        if (showDebugLogs) Debug.Log($"[EnemyManager] Running {queueSnapshot.Count} enemy turns.");

        foreach (var enemy in queueSnapshot)
        {
            if (enemy == null || enemy.IsDead) continue;

            var ai = enemy.GetComponent<EnemyAI>();
            var ranged = enemy.GetComponent<RangedEnemyAI>();
            if (ai == null && ranged == null) continue;

            if (showDebugLogs)
                Debug.Log($"[EnemyManager] Enemy turn started: {enemy.Stats?.enemyName ?? enemy.name}");

            OnEnemyTurnStarted?.Invoke(enemy);

            if (delayBeforeEnemyTurn > 0f)
                yield return new WaitForSeconds(delayBeforeEnemyTurn);

            bool done = false;
            if (ranged != null) ranged.TakeTurn(() => done = true);
            else ai.TakeTurn(() => done = true);

            yield return new WaitUntil(() => done);

            if (delayAfterEnemyTurn > 0f)
                yield return new WaitForSeconds(delayAfterEnemyTurn);

            OnEnemyTurnFinished?.Invoke(enemy);

            if (showDebugLogs)
                Debug.Log($"[EnemyManager] Enemy turn finished: {enemy.Stats?.enemyName ?? enemy.name}");

            if (enemy != null && !enemy.IsDead && EnemyTurnQueue.Instance != null)
                EnemyTurnQueue.Instance.RotateEnemyToBack(enemy);
        }

        running = false;
        if (showDebugLogs) Debug.Log("[EnemyManager] All turns done.");
        OnEnemyTurnsComplete?.Invoke();
    }
}
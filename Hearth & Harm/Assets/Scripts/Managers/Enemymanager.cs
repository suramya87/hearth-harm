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

    public event Action          OnEnemyTurnsComplete;
    public event Action          OnEnemyListChanged;
    public event Action<RoomGrid> OnRoomCleared;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
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

    public void RunEnemyTurns()
    {
        if (running) return;
        StartCoroutine(RunTurns());
    }

    private IEnumerator RunTurns()
    {
        running = true;
        if (showDebugLogs) Debug.Log($"[EnemyManager] Running {active.Count} enemy turns.");

        var snapshot = new List<EnemyUnit>(active);
        foreach (var enemy in snapshot)
        {
            if (enemy == null || enemy.IsDead) continue;

            var ai      = enemy.GetComponent<EnemyAI>();
            var ranged  = enemy.GetComponent<RangedEnemyAI>();
            if (ai == null && ranged == null) continue;

            bool done = false;
            if (ranged != null) ranged.TakeTurn(() => done = true);
            else                ai.TakeTurn(()    => done = true);

            yield return new WaitUntil(() => done);
        }

        running = false;
        if (showDebugLogs) Debug.Log("[EnemyManager] All turns done.");
        OnEnemyTurnsComplete?.Invoke();
    }
}
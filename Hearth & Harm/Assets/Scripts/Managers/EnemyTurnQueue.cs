using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maintains the ordered enemy turn queue for the current room.
/// This is the source of truth for both enemy turn execution and turn-order UI.
/// </summary>
public class EnemyTurnQueue : MonoBehaviour
{
    public static EnemyTurnQueue Instance { get; private set; }

    [SerializeField] private bool showDebugLogs;

    private readonly List<EnemyUnit> queuedEnemies = new();
    private RoomGrid currentRoom;

    public event Action OnQueueChanged;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void BuildQueue(RoomGrid room, List<EnemyUnit> enemies)
    {
        currentRoom = room;
        queuedEnemies.Clear();

        foreach (var enemy in enemies)
        {
            if (enemy == null || enemy.IsDead) continue;
            if (enemy.CurrentRoomGrid != room) continue;

            queuedEnemies.Add(enemy);
        }

        if (showDebugLogs)
            Debug.Log($"[EnemyTurnQueue] Built queue with {queuedEnemies.Count} enemies.");

        OnQueueChanged?.Invoke();
    }

    public void ClearQueue()
    {
        currentRoom = null;
        queuedEnemies.Clear();
        OnQueueChanged?.Invoke();
    }

    public List<EnemyUnit> GetQueuedEnemies() => new(queuedEnemies);

    public EnemyUnit PeekNext()
    {
        return queuedEnemies.Count > 0 ? queuedEnemies[0] : null;
    }

    public bool Contains(EnemyUnit enemy) => queuedEnemies.Contains(enemy);

    public void RemoveEnemy(EnemyUnit enemy)
    {
        if (enemy == null) return;

        if (queuedEnemies.Remove(enemy))
        {
            if (showDebugLogs)
                Debug.Log($"[EnemyTurnQueue] Removed {enemy.name} from queue.");

            OnQueueChanged?.Invoke();
        }
    }

    public void RotateEnemyToBack(EnemyUnit enemy)
    {
        if (enemy == null) return;
        if (queuedEnemies.Count <= 1) return;
        if (!queuedEnemies.Contains(enemy)) return;

        queuedEnemies.Remove(enemy);
        queuedEnemies.Add(enemy);

        if (showDebugLogs)
            Debug.Log($"[EnemyTurnQueue] Rotated {enemy.name} to back.");

        OnQueueChanged?.Invoke();
    }

    public RoomGrid GetCurrentRoom() => currentRoom;
}
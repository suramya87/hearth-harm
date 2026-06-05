// ─────────────────────────────────────────────────────────────────────────────
// AnalyticsRoomTracker.cs
//
// SETUP
//   1. Attach to each room prefab (or the room root GameObject).
//   2. Set roomName in the Inspector, e.g. "TreasureRoom", "BossRoom".
//      If left blank it falls back to the GameObject's name.
//   3. Call MarkVisited() and MarkCleared() from wherever your room system
//      already handles those events — OR enable the auto-detect options below
//      to have this component listen to existing events itself.
//
// ZERO game logic is touched. This is observe-only.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

public class AnalyticsRoomTracker : MonoBehaviour
{
    [Tooltip("Human-readable room name shown in the dashboard.\n" +
             "Leave blank to use the GameObject name.")]
    [SerializeField] private string roomName = "";

    [Tooltip("If true, fires room_visited automatically when this GameObject becomes active.\n" +
             "Useful if rooms are activated/deactivated as the player moves between them.")]
    [SerializeField] private bool autoTrackOnEnable = false;

    [Tooltip("If true, subscribes to EnemyManager to detect when all enemies in this\n" +
             "room are gone and fires room_cleared automatically.")]
    [SerializeField] private bool autoTrackCleared = false;

    private bool visited;
    private bool cleared;

    private string RoomName => string.IsNullOrEmpty(roomName) ? gameObject.name : roomName;

    private void OnEnable()
    {
        if (autoTrackOnEnable) MarkVisited();
    }

    private void Start()
    {
        if (autoTrackCleared)
            StartCoroutine(WatchForClear());
    }

    // ── Public API — call these from your room system ──────────────────────

    /// <summary>Call when the player enters this room.</summary>
    public void MarkVisited()
    {
        if (visited) return; // only track first visit
        visited = true;
        AnalyticsEvents.RoomVisited(RoomName);
    }

    /// <summary>Call when all enemies in this room are defeated.</summary>
    public void MarkCleared()
    {
        if (cleared) return;
        cleared = true;
        AnalyticsEvents.RoomCleared(RoomName);
    }

    // ── Auto-clear detection ───────────────────────────────────────────────

    private System.Collections.IEnumerator WatchForClear()
    {
        // Wait until EnemyManager exists and the room has enemies
        while (EnemyManager.Instance == null) yield return null;

        // Find the RoomGrid on this GameObject or a parent
        var roomGrid = GetComponent<RoomGrid>() ?? GetComponentInParent<RoomGrid>();
        if (roomGrid == null)
        {
            Debug.LogWarning($"[AnalyticsRoomTracker] autoTrackCleared=true but no RoomGrid " +
                             $"found on {gameObject.name} or its parents. Disable autoTrackCleared " +
                             $"and call MarkCleared() manually instead.");
            yield break;
        }

        // Wait until the room actually has enemies (give spawners time to run)
        yield return new WaitForSeconds(1f);

        bool hadEnemies = false;

        while (!cleared)
        {
            var enemies = EnemyManager.Instance.GetEnemiesInRoom(roomGrid);

            if (enemies != null && enemies.Count > 0)
                hadEnemies = true;

            // Room is cleared only if it had enemies and now has none
            if (hadEnemies && (enemies == null || enemies.Count == 0))
            {
                MarkCleared();
                yield break;
            }

            yield return new WaitForSeconds(0.5f); // poll every half second
        }
    }
}
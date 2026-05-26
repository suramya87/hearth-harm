using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnalyticsRoomTracker : MonoBehaviour
{
    [Tooltip("Human-readable room name shown in the dashboard.\n" +
             "Leave blank to use the GameObject name.")]
    [SerializeField] private string roomName = "";

    [Tooltip("If true, fires room_visited automatically when this GameObject becomes active.")]
    [SerializeField] private bool autoTrackOnEnable = false;

    [Tooltip("If true, subscribes to EnemyManager to detect when all enemies are gone.")]
    [SerializeField] private bool autoTrackCleared = false;

    [Tooltip("How often (seconds) to sample FPS while the player is in this room.")]
    [SerializeField] private float fpsSampleInterval = 0.5f;

    private bool visited;
    private bool cleared;

    // ── Time tracking ──────────────────────────────────────────────────────
    private float enterTime    = -1f;
    private bool  timerRunning = false;

    // ── FPS sampling ───────────────────────────────────────────────────────
    private List<float> fpsSamples   = new List<float>();
    private Coroutine   fpsCoroutine = null;

    // ── Load time ──────────────────────────────────────────────────────────
    private float activateTime = -1f;
    private bool  loadTracked  = false;

    private string RoomName => string.IsNullOrEmpty(roomName) ? gameObject.name : roomName;
    private bool   IsWeb    => Application.platform == RuntimePlatform.WebGLPlayer;

    // ── Unity lifecycle ────────────────────────────────────────────────────

    private void OnEnable()
    {
        activateTime = Time.realtimeSinceStartup;
        if (autoTrackOnEnable) MarkVisited();
    }

    private void Start()
    {
        if (!loadTracked)
        {
            loadTracked = true;
            float loadSeconds = Time.realtimeSinceStartup - activateTime;
            AnalyticsEvents.RoomLoadTime(RoomName, loadSeconds, IsWeb);
        }

        if (autoTrackCleared)
            StartCoroutine(WatchForClear());
    }

    private void OnDisable()  => StopTimer();
    private void OnDestroy()  => StopTimer();

    // ── Public API ─────────────────────────────────────────────────────────

    public void MarkVisited()
    {
        if (visited) return;
        visited = true;

        enterTime    = Time.time;
        timerRunning = true;

        fpsSamples.Clear();
        fpsCoroutine = StartCoroutine(SampleFps());

        AnalyticsEvents.RoomVisited(RoomName);
    }

    public void MarkCleared()
    {
        if (cleared) return;
        cleared = true;
        AnalyticsEvents.RoomCleared(RoomName);
    }

    public void StopTimer()
    {
        if (!timerRunning) return;
        timerRunning = false;

        if (fpsCoroutine != null)
        {
            StopCoroutine(fpsCoroutine);
            fpsCoroutine = null;
        }

        float secondsSpent = Time.time - enterTime;
        float avgFps       = ComputeAvgFps();
        float minFps       = ComputeMinFps();

        AnalyticsEvents.RoomTimeSpent(RoomName, secondsSpent, cleared, avgFps, minFps);
    }

    // ── FPS sampling ───────────────────────────────────────────────────────

    private IEnumerator SampleFps()
    {
        while (true)
        {
            if (Time.deltaTime > 0f)
                fpsSamples.Add(1f / Time.deltaTime);
            yield return new WaitForSeconds(fpsSampleInterval);
        }
    }

    private float ComputeAvgFps()
    {
        if (fpsSamples.Count == 0) return 0f;
        float sum = 0f;
        foreach (float s in fpsSamples) sum += s;
        return sum / fpsSamples.Count;
    }

    private float ComputeMinFps()
    {
        if (fpsSamples.Count == 0) return 0f;
        float min = float.MaxValue;
        foreach (float s in fpsSamples) if (s < min) min = s;
        return min;
    }

    // ── Auto-clear detection ───────────────────────────────────────────────

    private IEnumerator WatchForClear()
    {
        while (EnemyManager.Instance == null) yield return null;

        var roomGrid = GetComponent<RoomGrid>() ?? GetComponentInParent<RoomGrid>();
        if (roomGrid == null)
        {
            Debug.LogWarning($"[AnalyticsRoomTracker] autoTrackCleared=true but no RoomGrid " +
                             $"found on {gameObject.name}. Call MarkCleared() manually instead.");
            yield break;
        }

        yield return new WaitForSeconds(1f);

        bool hadEnemies = false;

        while (!cleared)
        {
            var enemies = EnemyManager.Instance.GetEnemiesInRoom(roomGrid);

            if (enemies != null && enemies.Count > 0)
                hadEnemies = true;

            if (hadEnemies && (enemies == null || enemies.Count == 0))
            {
                MarkCleared();
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }
    }
}
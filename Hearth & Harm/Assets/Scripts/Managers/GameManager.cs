using System.Collections;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Analytics;

/// <summary>
/// Game mode enum used across all systems.
/// </summary>
public enum GameMode
{
    Offline,
    Host,
    Client,
    None
}

/// <summary>
/// Central entry point. Controls game mode so the same scene runs SP or MP.
/// Also initializes UGS Analytics on startup — no game logic is affected.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Mode")]
    [Tooltip("Starting mode. Overridden at runtime by NetworkBootstrapper or MainMenuController.")]
    [SerializeField] private GameMode defaultMode = GameMode.Offline;

    // ── Static accessors ───────────────────────────────────────────────────

    public static GameMode Mode       => Instance != null ? Instance._mode : GameMode.Offline;
    public static bool IsMultiplayer  => Mode != GameMode.Offline;
    public static bool IsAuthority    => Mode == GameMode.Offline || Mode == GameMode.Host;
    public static bool IsClient       => Mode == GameMode.Client;

    /// <summary>True once UGS has initialized successfully.</summary>
    public static bool AnalyticsReady { get; private set; }

    // ── Internal ───────────────────────────────────────────────────────────

    private GameMode _mode;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        _mode = defaultMode;
        Debug.Log($"[GameManager] Mode = {_mode}");

        StartCoroutine(InitAnalytics());
    }

    // ── UGS Analytics init ─────────────────────────────────────────────────

    private IEnumerator InitAnalytics()
    {
        // UGS requires async init — wrap in a coroutine so it doesn't block anything
        var task = UnityServices.InitializeAsync();
        yield return new WaitUntil(() => task.IsCompleted);

        if (task.IsFaulted)
        {
            Debug.LogWarning($"[GameManager] UGS init failed: {task.Exception?.Message}");
            yield break;
        }

        AnalyticsService.Instance.StartDataCollection();
        AnalyticsReady = true;
        Debug.Log("[GameManager] UGS Analytics ready.");
    }

    // ── Mode control ───────────────────────────────────────────────────────

    public static void SetMode(GameMode mode)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[GameManager] SetMode called before Instance is ready.");
            return;
        }
        Instance._mode = mode;
        Debug.Log($"[GameManager] Mode → {mode}");
    }

    public static void SetMultiplayer(bool multiplayer)
    {
        if (!multiplayer) { SetMode(GameMode.Offline); return; }

        bool isHost = Unity.Netcode.NetworkManager.Singleton != null
                   && Unity.Netcode.NetworkManager.Singleton.IsHost;
        SetMode(isHost ? GameMode.Host : GameMode.Client);
    }
}
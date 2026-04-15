using UnityEngine;

/// <summary>
/// Game mode enum used across all systems.
/// </summary>
public enum GameMode
{
    Offline,  // Single-player, no network
    Host,     // Multiplayer host (also runs server logic)
    Client    // Multiplayer client (non-authoritative)
}

/// <summary>
/// Central entry point. Controls game mode so the same scene runs SP or MP.
///
/// HOW TO USE:
///   - Single-player: leave defaultMode = Offline
///   - Multiplayer: NetworkBootstrapper calls SetMode(Host/Client) before level loads
///
/// Systems read GameManager.Mode to decide whether to activate networked behaviour.
/// This replaces the old bool isMultiplayer flag (fully backwards compatible via IsMultiplayer).
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Mode")]
    [Tooltip("Starting mode. Overridden at runtime by NetworkBootstrapper.")]
    [SerializeField] private GameMode defaultMode = GameMode.Offline;

    // ── Static accessors ───────────────────────────────────────────────────

    public static GameMode Mode => Instance != null ? Instance._mode : GameMode.Offline;

    /// <summary>True if running in any networked mode (Host or Client).</summary>
    public static bool IsMultiplayer => Mode != GameMode.Offline;

    /// <summary>True if this peer is authoritative (host or offline SP).</summary>
    public static bool IsAuthority => Mode == GameMode.Offline || Mode == GameMode.Host;

    /// <summary>True only when running as a non-host client.</summary>
    public static bool IsClient => Mode == GameMode.Client;

    // ── Internal ───────────────────────────────────────────────────────────

    private GameMode _mode;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        _mode = defaultMode;
        Debug.Log($"[GameManager] Mode = {_mode}");
    }

    /// <summary>
    /// Called by NetworkBootstrapper when a connection is established.
    /// Can also be called manually for testing.
    /// </summary>
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
}
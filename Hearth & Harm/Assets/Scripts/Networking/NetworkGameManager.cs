using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// Manages UGS sign-in, session creation/joining, and player state.
///
/// STABILITY IMPROVEMENTS
/// ──────────────────────
/// • Exponential back-off with jitter on "Too Many Requests" (HTTP 429) from UGS Lobby.
/// • Every error path resets isConnecting so buttons never stay permanently locked.
/// • Widget-session watcher stops cleanly once a session is found.
/// • SpawnLobbySyncAsHost waits properly for NGO to be fully listening before acting.
/// • LeaveSessionAsync is idempotent and guards against double-shutdown.
/// </summary>
public class NetworkGameManager : MonoBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    [Header("Session Settings")]
    [SerializeField] private int maxPlayers = 4;

    [Header("Lobby Sync")]
    [SerializeField] private GameObject lobbySyncPrefab;

    // ── Public state ───────────────────────────────────────────────────────
    public ISession CurrentSession      { get; private set; }
    public bool     IsHost              => NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
    public string   LocalPlayerId       { get; private set; }
    public string   LocalPlayerName     { get; set; } = "Player";
    public int      LocalCharacterIndex { get; private set; } = 0;

    // ── Events ─────────────────────────────────────────────────────────────
    public event Action                          OnSignedIn;
    public event Action<string>                  OnSignInFailed;
    public event Action                          OnSessionCreated;
    public event Action                          OnSessionJoined;
    public event Action                          OnSessionLeft;
    public event Action<string>                  OnSessionError;
    public event Action<List<SessionPlayerInfo>> OnPlayersUpdated;

    // ── Private state ──────────────────────────────────────────────────────
    private int  localCharacterIndex  = 0;
    private bool localIsReady         = false;
    private bool sessionEventFired    = false;
    private bool isLeavingSession     = false;

    private Dictionary<string, int> characterSelections = new();
    private List<SessionPlayerInfo> cachedPlayerList    = new();

    // Back-off constants for UGS rate-limit (HTTP 429) retries
    private const int   MaxRetries       = 4;
    private const float BaseDelaySeconds = 2f;   // doubles each attempt
    private const float JitterSeconds    = 0.5f; // random ± jitter

    // Widget reflection — fallback if session was created by Unity Multiplayer Widget
    private System.Reflection.PropertyInfo widgetInstanceProp;
    private System.Reflection.PropertyInfo widgetActiveSessionProp;
    private bool                           widgetReflectionResolved = false;
    private Coroutine                      widgetWatchCoroutine;

    // ─────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        _ = InitializeAsync();
        widgetWatchCoroutine = StartCoroutine(WatchForWidgetSession());
    }

    private void OnDestroy() => _ = LeaveSessionAsync();

    // ─────────────────────────────────────────────────────────────────────
    // UGS initialisation
    // ─────────────────────────────────────────────────────────────────────

    private async Task InitializeAsync()
    {
        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                try   { await AuthenticationService.Instance.SignInAnonymouslyAsync(); }
                catch (Exception signInEx)
                {
                    if (!AuthenticationService.Instance.IsSignedIn) throw signInEx;
                }
            }

            LocalPlayerId = AuthenticationService.Instance.PlayerId;

            string nameKey   = $"PlayerName_{LocalPlayerId}";
            string savedName = PlayerPrefs.GetString(nameKey, "Player" + UnityEngine.Random.Range(100, 999));
            LocalPlayerName  = savedName;
            PlayerPrefs.SetString(nameKey, savedName);

            await SetAuthDisplayNameAsync(savedName);

            Debug.Log($"[NetworkGameManager] Signed in as {LocalPlayerId}");
            OnSignedIn?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkGameManager] Sign-in failed: {e.Message}");

            if (AuthenticationService.Instance.IsSignedIn)
            {
                LocalPlayerId = AuthenticationService.Instance.PlayerId;
                Debug.Log($"[NetworkGameManager] Recovered — already signed in: {LocalPlayerId}");
                OnSignedIn?.Invoke();
            }
            else
            {
                OnSignInFailed?.Invoke(e.Message);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Session creation — with exponential back-off on HTTP 429
    // ─────────────────────────────────────────────────────────────────────

    public async Task CreateSessionAsync()
    {
        if (string.IsNullOrEmpty(LocalPlayerId))
        {
            OnSessionError?.Invoke("Not signed in yet. Please wait.");
            return;
        }

        Exception lastException = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                // Exponential back-off: 2s, 4s, 8s, 16s  ± up to 0.5s jitter
                float delay = BaseDelaySeconds * Mathf.Pow(2f, attempt - 1)
                              + UnityEngine.Random.Range(-JitterSeconds, JitterSeconds);
                delay = Mathf.Max(0.1f, delay);

                Debug.Log($"[NetworkGameManager] Rate-limited — retrying CreateSession in {delay:F1}s " +
                          $"(attempt {attempt + 1}/{MaxRetries + 1})");

                OnSessionError?.Invoke($"Service busy. Retrying in {Mathf.CeilToInt(delay)}s…");
                await Task.Delay(Mathf.RoundToInt(delay * 1000));
            }

            try
            {
                var options = new SessionOptions
                {
                    MaxPlayers = maxPlayers,
                    Name       = $"{LocalPlayerName}'s Game"
                }.WithRelayNetwork();

                CurrentSession    = await MultiplayerService.Instance.CreateSessionAsync(options);
                sessionEventFired = true;

                SubscribeToSessionEvents();
                characterSelections[LocalPlayerId] = localCharacterIndex;

                Debug.Log($"[NetworkGameManager] Session created. Code: {CurrentSession.Code}");
                OnSessionCreated?.Invoke();
                RefreshPlayerList();

                StartCoroutine(SpawnLobbySyncAsHost());
                return; // success
            }
            catch (Exception e)
            {
                lastException = e;
                bool isRateLimit = e.Message.Contains("429") ||
                                   e.Message.Contains("Too Many Requests");

                if (!isRateLimit || attempt == MaxRetries)
                {
                    // Non-retryable error OR we've exhausted retries
                    Debug.LogError($"[NetworkGameManager] CreateSession failed: {e.Message}");
                    OnSessionError?.Invoke($"Failed to create session: {e.Message}");
                    return;
                }
                // else: rate-limited — loop around for back-off
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Session joining — with exponential back-off on HTTP 429
    // ─────────────────────────────────────────────────────────────────────

    public async Task JoinSessionAsync(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            OnSessionError?.Invoke("Please enter a join code.");
            return;
        }

        if (string.IsNullOrEmpty(LocalPlayerId))
        {
            OnSessionError?.Invoke("Not signed in yet. Please wait.");
            return;
        }

        string code = joinCode.Trim().ToUpper();

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                float delay = BaseDelaySeconds * Mathf.Pow(2f, attempt - 1)
                              + UnityEngine.Random.Range(-JitterSeconds, JitterSeconds);
                delay = Mathf.Max(0.1f, delay);

                Debug.Log($"[NetworkGameManager] Rate-limited — retrying JoinSession in {delay:F1}s " +
                          $"(attempt {attempt + 1}/{MaxRetries + 1})");

                OnSessionError?.Invoke($"Service busy. Retrying in {Mathf.CeilToInt(delay)}s…");
                await Task.Delay(Mathf.RoundToInt(delay * 1000));
            }

            try
            {
                CurrentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                sessionEventFired = true;

                SubscribeToSessionEvents();
                characterSelections[LocalPlayerId] = localCharacterIndex;

                Debug.Log($"[NetworkGameManager] Joined session: {CurrentSession.Id}");
                OnSessionJoined?.Invoke();
                RefreshPlayerList();
                return; // success
            }
            catch (Exception e)
            {
                bool isRateLimit = e.Message.Contains("429") ||
                                   e.Message.Contains("Too Many Requests");

                // UGS returns "already a member" when the Widget joined the lobby
                // in the background while we were retrying.  Treat it as success:
                // the session is already live — just sync from the Widget path.
                bool alreadyMember = e.Message.Contains("already a member") ||
                                     e.Message.Contains("already in lobby");
                if (alreadyMember)
                {
                    Debug.Log("[NetworkGameManager] Already a member — syncing from Widget session.");
                    var widgetSession = TryGetWidgetSession();
                    if (widgetSession != null)
                    {
                        SyncExternalSession(widgetSession);
                    }
                    else
                    {
                        // Widget session not available yet — fire OnSessionJoined so
                        // MainMenuController enters the lobby panel and keeps waiting
                        // for LobbySync to arrive via NGO replication.
                        sessionEventFired = true;
                        OnSessionJoined?.Invoke();
                    }
                    return;
                }

                if (!isRateLimit || attempt == MaxRetries)
                {
                    Debug.LogError($"[NetworkGameManager] JoinSession failed: {e.Message}");
                    OnSessionError?.Invoke($"Failed to join: {e.Message}");
                    return;
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // LobbySync spawning — host only
    //
    // Waits for NGO to be fully listening (IsListening + IsHost) before
    // instantiating and network-spawning the LobbySync prefab.
    // ─────────────────────────────────────────────────────────────────────

    private IEnumerator SpawnLobbySyncAsHost()
    {
        float elapsed = 0f;
        const float timeout = 20f;

        while (NetworkManager.Singleton == null  ||
               !NetworkManager.Singleton.IsListening ||
               !NetworkManager.Singleton.IsHost)
        {
            elapsed += Time.deltaTime;
            if (elapsed > timeout)
            {
                Debug.LogError("[NetworkGameManager] Timed out waiting for NGO host. " +
                               "LobbySync NOT spawned. Check NetworkManager configuration.");
                yield break;
            }
            yield return null;
        }

        // Another route (Widget) may have already spawned LobbySync
        if (LobbySync.Instance != null)
        {
            Debug.Log("[NetworkGameManager] LobbySync already exists — skipping spawn.");
            yield break;
        }

        if (lobbySyncPrefab == null)
        {
            Debug.LogError("[NetworkGameManager] lobbySyncPrefab not assigned in Inspector! " +
                           "Character select will not work.");
            yield break;
        }

        var go     = Instantiate(lobbySyncPrefab);
        var netObj = go.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError("[NetworkGameManager] LobbySync prefab is missing a NetworkObject!");
            Destroy(go);
            yield break;
        }

        netObj.Spawn();
        Debug.Log("[NetworkGameManager] LobbySync spawned by host.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Widget session fallback — stops once a session is confirmed
    // ─────────────────────────────────────────────────────────────────────

    private IEnumerator WatchForWidgetSession()
    {
        // Wait until signed in
        while (string.IsNullOrEmpty(LocalPlayerId))
            yield return null;

        while (true)
        {
            yield return new WaitForSeconds(0.25f);

            // Stop watching once we have a session through any path
            if (CurrentSession != null || sessionEventFired)
            {
                widgetWatchCoroutine = null;
                yield break;
            }

            var session = TryGetWidgetSession();
            if (session == null) continue;

            Debug.Log($"[NetworkGameManager] Widget session detected: {session.Id} Code={session.Code}");
            SyncExternalSession(session);

            widgetWatchCoroutine = null;
            yield break;
        }
    }

    private ISession TryGetWidgetSession()
    {
        if (!widgetReflectionResolved)
        {
            widgetReflectionResolved = true;

            var managerType = Type.GetType(
                "Unity.Multiplayer.Widgets.SessionManager, Unity.Multiplayer.Widgets");
            if (managerType == null) return null;

            widgetInstanceProp = managerType.GetProperty(
                "Instance",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            widgetActiveSessionProp = managerType.GetProperty(
                "ActiveSession",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        }

        if (widgetInstanceProp == null || widgetActiveSessionProp == null) return null;

        var managerInstance = widgetInstanceProp.GetValue(null);
        if (managerInstance == null) return null;

        return widgetActiveSessionProp.GetValue(managerInstance) as ISession;
    }

    // ─────────────────────────────────────────────────────────────────────
    // External session sync (Widget fallback path)
    // ─────────────────────────────────────────────────────────────────────

    public void SyncExternalSession(ISession session)
    {
        if (CurrentSession != null || sessionEventFired) return;

        CurrentSession    = session;
        sessionEventFired = true;

        SubscribeToSessionEvents();

        if (LocalPlayerId != null)
            characterSelections[LocalPlayerId] = localCharacterIndex;

        Debug.Log($"[NetworkGameManager] Session synced. Code={session.Code} IsHost={IsHost}");

        if (IsHost)
        {
            OnSessionCreated?.Invoke();
            StartCoroutine(SpawnLobbySyncAsHost());
        }
        else
        {
            OnSessionJoined?.Invoke();
        }

        RefreshPlayerList();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Leave session — idempotent, guards against double-shutdown
    // ─────────────────────────────────────────────────────────────────────

    public async Task LeaveSessionAsync()
    {
        if (CurrentSession == null || isLeavingSession) return;

        isLeavingSession = true;
        UnsubscribeFromSessionEvents();
        sessionEventFired = false;

        var sessionToLeave = CurrentSession;
        CurrentSession = null;

        try   { await sessionToLeave.LeaveAsync(); }
        catch (Exception e)
        {
            Debug.LogWarning($"[NetworkGameManager] Leave error (non-fatal): {e.Message}");
        }

        // Shut down NGO only if it was started
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        isLeavingSession = false;
        OnSessionLeft?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Local player data
    // ─────────────────────────────────────────────────────────────────────

    public void SetLocalPlayerName(string name)
    {
        LocalPlayerName = string.IsNullOrWhiteSpace(name) ? "Player" : name;
        string nameKey  = $"PlayerName_{LocalPlayerId}";
        PlayerPrefs.SetString(nameKey, LocalPlayerName);
        _ = SyncPlayerDataAsync();
        _ = SetAuthDisplayNameAsync(LocalPlayerName);
    }

    public void SetLocalCharacterSelection(int index)
    {
        localCharacterIndex = index;
        LocalCharacterIndex = index;
        if (LocalPlayerId != null)
            characterSelections[LocalPlayerId] = index;
        _ = SyncPlayerDataAsync();
        RefreshPlayerList();
    }

    public void SetLocalReadyState(bool ready)
    {
        localIsReady = ready;
        _ = SyncPlayerDataAsync();
        RefreshPlayerList();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Player list
    // ─────────────────────────────────────────────────────────────────────

    public List<SessionPlayerInfo> GetPlayerList() => cachedPlayerList;
    public string                  GetJoinCode()   => CurrentSession?.Code ?? "---";
    public int                     GetMaxPlayers() => maxPlayers;

    public bool AllPlayersReady()
    {
        if (cachedPlayerList.Count == 0) return false;
        foreach (var p in cachedPlayerList)
            if (!p.IsReady) return false;
        return true;
    }

    public Dictionary<string, int> GetCharacterSelections() => new(characterSelections);

    private void RefreshPlayerList()
    {
        cachedPlayerList.Clear();

        if (CurrentSession == null)
        {
            OnPlayersUpdated?.Invoke(cachedPlayerList);
            return;
        }

        foreach (var player in CurrentSession.Players)
        {
            bool   isLocal = player.Id == LocalPlayerId;
            int    charIdx = isLocal ? localCharacterIndex : 0;
            bool   ready   = isLocal ? localIsReady : false;
            string name    = isLocal ? LocalPlayerName : "Player";

            if (player.Properties != null)
            {
                if (player.Properties.TryGetValue("CharIdx", out var cp)) int.TryParse(cp.Value, out charIdx);
                if (player.Properties.TryGetValue("IsReady", out var rp)) bool.TryParse(rp.Value, out ready);
                if (player.Properties.TryGetValue("Name",    out var np) && !string.IsNullOrEmpty(np.Value))
                    name = np.Value;
            }

            cachedPlayerList.Add(new SessionPlayerInfo(
                player.Id, name, charIdx, ready, isLocal, isLocal && IsHost));
        }

        OnPlayersUpdated?.Invoke(cachedPlayerList);
    }

    public void RegisterCharacterSelection(string playerId, int index)
        => characterSelections[playerId] = index;

    // ─────────────────────────────────────────────────────────────────────
    // UGS data sync
    // ─────────────────────────────────────────────────────────────────────

    private async Task SyncPlayerDataAsync()
    {
        if (CurrentSession == null || string.IsNullOrEmpty(LocalPlayerId)) return;
        try
        {
            CurrentSession.CurrentPlayer.SetProperty("CharIdx", new PlayerProperty(localCharacterIndex.ToString()));
            CurrentSession.CurrentPlayer.SetProperty("IsReady", new PlayerProperty(localIsReady.ToString()));
            CurrentSession.CurrentPlayer.SetProperty("Name",    new PlayerProperty(LocalPlayerName ?? "Player"));
            await CurrentSession.SaveCurrentPlayerDataAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[NetworkGameManager] SyncPlayerData failed: {e.Message}");
        }
    }

    private async Task SetAuthDisplayNameAsync(string name)
    {
        try   { await AuthenticationService.Instance.UpdatePlayerNameAsync(name); }
        catch (Exception e)
        {
            Debug.LogWarning($"[NetworkGameManager] Could not set display name: {e.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Session events
    // ─────────────────────────────────────────────────────────────────────

    private void SubscribeToSessionEvents()
    {
        if (CurrentSession == null) return;
        CurrentSession.PlayerJoined  += HandlePlayerJoined;
        CurrentSession.PlayerLeaving += HandlePlayerLeft;
        CurrentSession.Changed       += HandleSessionChanged;
    }

    private void UnsubscribeFromSessionEvents()
    {
        if (CurrentSession == null) return;
        CurrentSession.PlayerJoined  -= HandlePlayerJoined;
        CurrentSession.PlayerLeaving -= HandlePlayerLeft;
        CurrentSession.Changed       -= HandleSessionChanged;
    }

    private void HandlePlayerJoined(string id)
    {
        Debug.Log($"[NetworkGameManager] Player joined: {id}");
        StartCoroutine(DelayedRefreshPlayerList());
    }

    private void HandlePlayerLeft(string id)
    {
        Debug.Log($"[NetworkGameManager] Player left: {id}");
        characterSelections.Remove(id);
        StartCoroutine(DelayedRefreshPlayerList());
    }

    private void HandleSessionChanged()
        => StartCoroutine(DelayedRefreshPlayerList());

    /// <summary>
    /// Yields one frame so UGS internal player list is consistent
    /// before iterating CurrentSession.Players.
    /// </summary>
    private IEnumerator DelayedRefreshPlayerList()
    {
        yield return null;
        RefreshPlayerList();
    }
}
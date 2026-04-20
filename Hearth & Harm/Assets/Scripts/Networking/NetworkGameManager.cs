using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

public class NetworkGameManager : MonoBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    [Header("Session Settings")]
    [SerializeField] private int maxPlayers = 4;

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
    private int  localCharacterIndex = 0;
    private bool localIsReady        = false;
    private bool sessionEventFired   = false;

    private Dictionary<string, int> characterSelections = new();
    private List<SessionPlayerInfo> cachedPlayerList    = new();

    // Widget reflection — fallback if session was created by Unity Multiplayer Widget
    private System.Reflection.PropertyInfo widgetInstanceProp;
    private System.Reflection.PropertyInfo widgetActiveSessionProp;
    private bool                           widgetReflectionResolved = false;

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
        StartCoroutine(WatchForWidgetSession());
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
    // Explicit session creation — used by Host / Join buttons
    // ─────────────────────────────────────────────────────────────────────

    public async Task CreateSessionAsync()
    {
        if (string.IsNullOrEmpty(LocalPlayerId))
        {
            OnSessionError?.Invoke("Not signed in yet. Please wait.");
            return;
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
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkGameManager] CreateSession failed: {e.Message}");
            OnSessionError?.Invoke($"Failed to create session: {e.Message}");
        }
    }

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

        try
        {
            CurrentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(
                joinCode.Trim().ToUpper());

            sessionEventFired = true;

            SubscribeToSessionEvents();
            characterSelections[LocalPlayerId] = localCharacterIndex;

            Debug.Log($"[NetworkGameManager] Joined session: {CurrentSession.Id}");
            OnSessionJoined?.Invoke();
            RefreshPlayerList();
        }
        catch (Exception e)
        {
            Debug.LogError($"[NetworkGameManager] JoinSession failed: {e.Message}");
            OnSessionError?.Invoke($"Failed to join: {e.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Widget session fallback — polls for sessions created by the UGS Widget
    // ─────────────────────────────────────────────────────────────────────

    private IEnumerator WatchForWidgetSession()
    {
        // Wait for sign-in before polling
        while (string.IsNullOrEmpty(LocalPlayerId))
            yield return null;

        while (true)
        {
            yield return new WaitForSeconds(0.25f);

            // Already have a session from explicit host/join — don't interfere
            if (CurrentSession != null || sessionEventFired) continue;

            var session = TryGetWidgetSession();
            if (session == null) continue;

            Debug.Log($"[NetworkGameManager] Widget session detected: {session.Id} Code={session.Code}");
            SyncExternalSession(session);
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

        if (IsHost) OnSessionCreated?.Invoke();
        else        OnSessionJoined?.Invoke();

        RefreshPlayerList();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Leave session
    // ─────────────────────────────────────────────────────────────────────

    public async Task LeaveSessionAsync()
    {
        if (CurrentSession == null) return;

        UnsubscribeFromSessionEvents();
        sessionEventFired = false;

        try { await CurrentSession.LeaveAsync(); }
        catch (Exception e) { Debug.LogWarning($"[NetworkGameManager] Leave error: {e.Message}"); }

        CurrentSession = null;
        NetworkManager.Singleton?.Shutdown();
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
        catch (Exception e) { Debug.LogWarning($"[NetworkGameManager] Could not set display name: {e.Message}"); }
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
        RefreshPlayerList();
    }

    private void HandlePlayerLeft(string id)
    {
        Debug.Log($"[NetworkGameManager] Player left: {id}");
        characterSelections.Remove(id);
        RefreshPlayerList();
    }

    private void HandleSessionChanged() => RefreshPlayerList();
}
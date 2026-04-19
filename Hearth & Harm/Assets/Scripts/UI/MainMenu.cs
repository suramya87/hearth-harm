using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum MenuState
{
    Main,
    ModeSelect,
    MultiplayerConnect,
    Lobby,
    CharSelect,
    Loading
}

public class MenuFlowController : MonoBehaviour
{
    public static MenuFlowController Instance { get; private set; }

    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject modeSelectorPanel;
    [SerializeField] private GameObject multiplayerPanel;
    [SerializeField] private GameObject charSelectPanel;
    [SerializeField] private GameObject loadingPanel;

    [Header("Multiplayer Panel")]
    [SerializeField] private TMP_InputField  joinCodeInput;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI sessionCodeText;
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private TextMeshProUGUI feedbackText;
    [SerializeField] private Button          beginCharSelectButton;

    [Header("Character Select")]
    [SerializeField] private List<Button>     characterButtons;
    [SerializeField] private TextMeshProUGUI  selectedNameText;
    [SerializeField] private TextMeshProUGUI  csStatusText;

    [Header("Character Data")]
    [SerializeField] private List<string>     characterNames  = new() { "SmokeStack", "Sconstance" };
    [SerializeField] private List<Sprite>     characterSprites;
    [SerializeField] private List<GameObject> characterPrefabs;
    [SerializeField] private Color            selectedTint   = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private Color            deselectedTint = new Color(1f, 1f, 1f, 0.4f);

    [Header("Single Player")]
    [SerializeField] private Button spStartButton;
    [SerializeField] private string singlePlayerScene = "SinglePlayerScene";

    [Header("Multiplayer Char Select")]
    [SerializeField] private Button readyButton;
    [SerializeField] private Button mpStartButton;

    [Header("Scenes")]
    [SerializeField] private string multiplayerScene = "MultiplayerTest";

    // ── Runtime ────────────────────────────────────────────────────────────
    private MenuState             _state        = MenuState.Main;
    private readonly Stack<MenuState> _history  = new();
    private List<GameObject>      _allPanels;

    private int  _selectedChar   = 0;
    private bool _isReady        = false;
    private bool _isSinglePlayer = false;

    private readonly HashSet<ulong> _readyClients = new();
    private readonly List<ulong>    _connectedIds = new();
    private Coroutine               _feedbackCoroutine;

    private bool _networkConnectedHandled = false;

    // ══════════════════════════════════════════════════════════════════════
    // Lifecycle
    // ══════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _allPanels = new List<GameObject>
        {
            mainMenuPanel, modeSelectorPanel, multiplayerPanel,
            charSelectPanel, loadingPanel
        };

        WireCharacterButtons();
        WireActionButtons();
        HideAll();
        ShowPanel(mainMenuPanel);

        StartCoroutine(SubscribeToLobbyNetwork());
        StartCoroutine(SubscribeToBootstrapper());
        StartCoroutine(WatchForWidgetConnection());
    }

    private void OnDestroy()
    {
        if (LobbyNetwork.Instance != null)
        {
            LobbyNetwork.Instance.OnReadyChanged    -= HandleReadyChanged;
            LobbyNetwork.Instance.OnBeginCharSelect -= OnBeginCharSelectReceived;
        }
        if (NetworkBootstrapper.Instance != null)
        {
            NetworkBootstrapper.Instance.OnJoinCodeReady    -= OnJoinCodeReady;
            NetworkBootstrapper.Instance.OnConnected        -= OnNetworkConnected;
            NetworkBootstrapper.Instance.OnConnectionFailed -= OnNetworkConnectionFailed;
        }
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subscriptions
    // ══════════════════════════════════════════════════════════════════════

    private IEnumerator SubscribeToLobbyNetwork()
    {
        LobbyNetwork lastSeen = null;
        while (true)
        {
            var current = LobbyNetwork.Instance;
            if (current != null && current != lastSeen)
            {
                if (lastSeen != null)
                {
                    lastSeen.OnReadyChanged    -= HandleReadyChanged;
                    lastSeen.OnBeginCharSelect -= OnBeginCharSelectReceived;
                }
                current.OnReadyChanged    -= HandleReadyChanged;
                current.OnBeginCharSelect -= OnBeginCharSelectReceived;
                current.OnReadyChanged    += HandleReadyChanged;
                current.OnBeginCharSelect += OnBeginCharSelectReceived;
                lastSeen = current;
                Debug.Log("[MenuFlow] Subscribed to LobbyNetwork.");
            }
            yield return new WaitForSeconds(0.2f);
        }
    }

    private IEnumerator SubscribeToBootstrapper()
    {
        while (NetworkBootstrapper.Instance == null) yield return null;
        NetworkBootstrapper.Instance.OnJoinCodeReady    += OnJoinCodeReady;
        NetworkBootstrapper.Instance.OnConnected        += OnNetworkConnected;
        NetworkBootstrapper.Instance.OnConnectionFailed += OnNetworkConnectionFailed;
        Debug.Log("[MenuFlow] Subscribed to NetworkBootstrapper.");
    }

    // Watches for Widget-based connections where NetworkBootstrapper
    // OnConnected never fires — detects IsListening becoming true instead
    private IEnumerator WatchForWidgetConnection()
    {
        bool wasListening = false;
        while (true)
        {
            bool isListening = NetworkManager.Singleton != null
                            && NetworkManager.Singleton.IsListening;

            if (isListening && !wasListening && !_networkConnectedHandled
                && _state == MenuState.MultiplayerConnect)
            {
                Debug.Log("[MenuFlow] Widget connection detected.");
                OnNetworkConnected();
            }

            if (!isListening && wasListening)
                _networkConnectedHandled = false;

            wasListening = isListening;
            yield return null;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Button wiring
    // ══════════════════════════════════════════════════════════════════════

    private void WireCharacterButtons()
    {
        for (int i = 0; i < characterButtons.Count; i++)
        {
            int idx = i;
            characterButtons[i]?.onClick.AddListener(() => OnCharacterSelected(idx));

            if (i < characterSprites.Count && characterSprites[i] != null)
            {
                var imgs = characterButtons[i]?.GetComponentsInChildren<Image>();
                if (imgs != null && imgs.Length > 1)
                    imgs[1].sprite = characterSprites[i];
            }
        }
    }

    private void WireActionButtons()
    {
        spStartButton        ?.onClick.AddListener(OnSPStartClicked);
        readyButton          ?.onClick.AddListener(OnReadyClicked);
        mpStartButton        ?.onClick.AddListener(OnMPStartClicked);
        beginCharSelectButton?.onClick.AddListener(OnBeginCharSelectClicked);

        spStartButton        ?.gameObject.SetActive(false);
        readyButton          ?.gameObject.SetActive(false);
        mpStartButton        ?.gameObject.SetActive(false);
        beginCharSelectButton?.gameObject.SetActive(false);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Public navigation
    // ══════════════════════════════════════════════════════════════════════

    public void OnNewGameClicked()      => GoTo(MenuState.ModeSelect);
    public void OnSinglePlayerClicked() => EnterSinglePlayerCharSelect();
    public void OnMultiplayerClicked()
    {
        _networkConnectedHandled = false;
        GoTo(MenuState.MultiplayerConnect);
    }
    public void OnBackClicked() => GoBack();

    public void OnExitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public async void OnHostClicked()
    {
        SetStatus("Creating session...");
        await NetworkBootstrapper.Instance.HostGame();
    }

    public async void OnJoinClicked()
    {
        string code = joinCodeInput != null ? joinCodeInput.text.Trim() : "";
        if (string.IsNullOrEmpty(code) || code.Length < 6)
        {
            SetStatus("Enter a valid 6-character code.");
            return;
        }
        SetStatus($"Joining {code.ToUpper()}...");
        await NetworkBootstrapper.Instance.JoinGame(code);
    }

    public void OnDisconnectClicked()
    {
        _networkConnectedHandled = false;
        NetworkBootstrapper.Instance?.Disconnect();
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        GoTo(MenuState.Main, clearHistory: true);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Bootstrapper callbacks
    // ══════════════════════════════════════════════════════════════════════

    private void OnJoinCodeReady(string code)
    {
        if (sessionCodeText != null) sessionCodeText.text = $"{code}";
        SetStatus($"{code}");
    }

    private void OnNetworkConnected()
    {
        if (_networkConnectedHandled) return;
        _networkConnectedHandled = true;

        Debug.Log($"[MenuFlow] Connected. IsHost={NetworkManager.Singleton?.IsHost}");

        _connectedIds.Clear();
        _readyClients.Clear();

        if (NetworkManager.Singleton != null)
            foreach (ulong id in NetworkManager.Singleton.ConnectedClientsIds)
                if (!_connectedIds.Contains(id)) _connectedIds.Add(id);

        GoTo(MenuState.Lobby);
        RefreshPlayerCount();
        SetStatus("Connected! Waiting for players...");

        bool isHost = NetworkManager.Singleton?.IsHost ?? false;
        beginCharSelectButton?.gameObject.SetActive(isHost);

        if (isHost)
        {
            NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
        else
        {
            StartCoroutine(WaitForClientHandshake());
        }
    }

    private IEnumerator WaitForClientHandshake()
    {
        float timeout = 15f;
        float elapsed = 0f;
        while (elapsed < timeout)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
            {
                Debug.Log("[MenuFlow] Client fully connected.");
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        Debug.LogError("[MenuFlow] Client connection timed out.");
        OnNetworkConnectionFailed("Connection timed out.");
    }

    private void OnNetworkConnectionFailed(string error)
    {
        _networkConnectedHandled = false;
        SetStatus($"Failed: {error}");
        GoTo(MenuState.MultiplayerConnect);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Player join / leave
    // ══════════════════════════════════════════════════════════════════════

    private void OnClientConnected(ulong clientId)
    {
        if (!_connectedIds.Contains(clientId)) _connectedIds.Add(clientId);
        _readyClients.Remove(clientId);
        RefreshPlayerCount();
        ShowFeedback($"Player {clientId} joined!");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        _connectedIds.Remove(clientId);
        _readyClients.Remove(clientId);
        RefreshPlayerCount();
        ShowFeedback($"Player {clientId} left.");
        EvaluateMPStartButton();
        UpdateCSStatus();
    }

    private void RefreshPlayerCount()
    {
        if (playerCountText == null) return;
        int count = NetworkManager.Singleton?.ConnectedClientsIds.Count ?? _connectedIds.Count;
        playerCountText.text = $"{count} / 4 players";
    }

    private void ShowFeedback(string msg)
    {
        if (feedbackText == null) return;
        if (_feedbackCoroutine != null) StopCoroutine(_feedbackCoroutine);
        _feedbackCoroutine = StartCoroutine(FeedbackRoutine(msg));
    }

    private IEnumerator FeedbackRoutine(string msg)
    {
        feedbackText.text  = msg;
        feedbackText.color = Color.white;
        yield return new WaitForSeconds(2f);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime;
            feedbackText.color = new Color(1f, 1f, 1f, 1f - t);
            yield return null;
        }
        feedbackText.text = "";
    }

    // ══════════════════════════════════════════════════════════════════════
    // Begin Char Select
    // ══════════════════════════════════════════════════════════════════════

    private void OnBeginCharSelectClicked()
    {
        if (!(NetworkManager.Singleton?.IsHost ?? false)) return;
        beginCharSelectButton?.gameObject.SetActive(false);
        LobbyNetwork.Instance?.BroadcastBeginCharSelect();
        EnterMultiplayerCharSelect();
    }

    // Public so LobbyNetwork.OnNetworkSpawn can re-subscribe to it by name
    public void OnBeginCharSelectReceived()
    {
        Debug.Log("[MenuFlow] OnBeginCharSelectReceived fired.");
        if (_state != MenuState.CharSelect)
            EnterMultiplayerCharSelect();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Single player char select
    // ══════════════════════════════════════════════════════════════════════

    private void EnterSinglePlayerCharSelect()
    {
        _isSinglePlayer = true;
        _selectedChar   = 0;

        GoTo(MenuState.CharSelect);
        RefreshCharacterButtons();

        spStartButton ?.gameObject.SetActive(true);
        readyButton   ?.gameObject.SetActive(false);
        mpStartButton ?.gameObject.SetActive(false);

        if (csStatusText != null) csStatusText.text = "";
    }

    private void OnSPStartClicked()
    {
        CharacterSelection.Index  = _selectedChar;
        CharacterSelection.Prefab = GetSelectedPrefab();
        GameManager.SetMode(GameMode.Offline);
        GoTo(MenuState.Loading, clearHistory: true);
        SceneManager.LoadScene(singlePlayerScene);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Multiplayer char select
    // ══════════════════════════════════════════════════════════════════════

    private void EnterMultiplayerCharSelect()
    {
        _isSinglePlayer = false;
        _isReady        = false;
        _selectedChar   = 0;
        _readyClients.Clear();

        GoTo(MenuState.CharSelect);
        RefreshCharacterButtons();
        UpdateReadyVisual();
        UpdateCSStatus();

        readyButton  ?.gameObject.SetActive(true);
        spStartButton?.gameObject.SetActive(false);

        bool isHost = NetworkManager.Singleton?.IsHost ?? false;
        mpStartButton?.gameObject.SetActive(isHost);
        if (mpStartButton != null) mpStartButton.interactable = false;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Character selection
    // ══════════════════════════════════════════════════════════════════════

    private void OnCharacterSelected(int index)
    {
        _selectedChar = index;
        RefreshCharacterButtons();

        if (!_isSinglePlayer)
        {
            CharacterSelectionSync.Instance?.SubmitCharacterIndex(index);
            LobbyNetwork.Instance?.SetReadyServerRpc(
                NetworkManager.Singleton.LocalClientId, true);
        }
    }

    private void RefreshCharacterButtons()
    {
        for (int i = 0; i < characterButtons.Count; i++)
        {
            if (characterButtons[i] == null) continue;
            bool sel = (i == _selectedChar);
            var img = characterButtons[i].GetComponent<Image>();
            if (img != null) img.color = sel ? selectedTint : deselectedTint;
            characterButtons[i].transform.localScale =
                sel ? new Vector3(1.1f, 1.1f, 1f) : Vector3.one;
        }
        if (selectedNameText != null && _selectedChar < characterNames.Count)
            selectedNameText.text = characterNames[_selectedChar];
    }

    // ══════════════════════════════════════════════════════════════════════
    // Ready system
    // ══════════════════════════════════════════════════════════════════════

    private void OnReadyClicked()
    {
        _isReady = !_isReady;
        LobbyNetwork.Instance?.SetReadyServerRpc(
            NetworkManager.Singleton.LocalClientId, _isReady);
        UpdateReadyVisual();
    }

    // Public so LobbyNetwork.OnNetworkSpawn can re-subscribe to it by name
    public void HandleReadyChanged(ulong clientId, bool isReady)
    {
        if (isReady) _readyClients.Add(clientId);
        else         _readyClients.Remove(clientId);
        UpdateCSStatus();
        EvaluateMPStartButton();
    }

    private void UpdateReadyVisual()
    {
        var txt = readyButton?.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) txt.text = _isReady ? "Not Ready" : "Ready!";
        var img = readyButton?.GetComponent<Image>();
        if (img != null) img.color = _isReady
            ? new Color(0.2f, 0.85f, 0.3f, 1f) : Color.white;
    }

    private void UpdateCSStatus()
    {
        if (csStatusText == null || NetworkManager.Singleton == null) return;
        int  total    = NetworkManager.Singleton.ConnectedClientsIds.Count;
        int  ready    = _readyClients.Count;
        bool allReady = ready == total && total > 0;
        csStatusText.text = allReady
            ? "<color=#44DD66>All players ready!</color>"
            : $"Waiting... ({ready}/{total} ready)";
    }

    private void EvaluateMPStartButton()
    {
        if (mpStartButton == null) return;
        if (!(NetworkManager.Singleton?.IsHost ?? false)) return;

        int  total    = NetworkManager.Singleton.ConnectedClientsIds.Count;
        bool allReady = _readyClients.Count == total && total > 0;

        mpStartButton.interactable = allReady;

        var txt = mpStartButton.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null)
        {
            txt.text  = allReady ? "START!" : $"({_readyClients.Count}/{total} ready)";
            txt.color = allReady ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        }
    }

    private void OnMPStartClicked()
    {
        if (!(NetworkManager.Singleton?.IsHost ?? false)) return;

        CharacterSelection.Index  = _selectedChar;
        CharacterSelection.Prefab = GetSelectedPrefab();

        GoTo(MenuState.Loading, clearHistory: true);

        var status = NetworkManager.Singleton.SceneManager.LoadScene(
            multiplayerScene, LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"[MenuFlow] Scene load failed: {status}");
            EnterMultiplayerCharSelect();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // State machine
    // ══════════════════════════════════════════════════════════════════════

    private void GoTo(MenuState next, bool clearHistory = false)
    {
        if (clearHistory) _history.Clear();
        else              _history.Push(_state);
        _state = next;
        ApplyState(next);
    }

    private void GoBack()
    {
        if (_history.Count == 0) return;
        _state = _history.Pop();
        ApplyState(_state);
    }

    private void ApplyState(MenuState state)
    {
        HideAll();
        switch (state)
        {
            case MenuState.Main:              ShowPanel(mainMenuPanel);     break;
            case MenuState.ModeSelect:        ShowPanel(modeSelectorPanel); break;
            case MenuState.MultiplayerConnect:
            case MenuState.Lobby:             ShowPanel(multiplayerPanel);  break;
            case MenuState.CharSelect:        ShowPanel(charSelectPanel);   break;
            case MenuState.Loading:           ShowPanel(loadingPanel);      break;
        }
        Debug.Log($"[MenuFlow] → {state}");
    }

    private void HideAll()
    {
        foreach (var p in _allPanels)
            if (p != null) p.SetActive(false);
    }

    public void TransitionToLoading() => GoTo(MenuState.Loading, clearHistory: true);

    private void ShowPanel(GameObject p) => p?.SetActive(true);

    public void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    private GameObject GetSelectedPrefab()
    {
        if (characterPrefabs == null || _selectedChar >= characterPrefabs.Count) return null;
        return characterPrefabs[_selectedChar];
    }
}
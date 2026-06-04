using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public class MainMenuController : MonoBehaviour
{
    // ── Panels ────────────────────────────────────────────────────────────
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject modePanel;
    [SerializeField] private GameObject multiplayerPanel;
    [SerializeField] private GameObject waitingLobbyPanel;
    [SerializeField] private GameObject characterSelectPanel;
    [SerializeField] private GameObject loadingPanel;
    [SerializeField] private GameObject creditsPanel;
    [SerializeField] private GameObject settingsPanel;

    // ── Main menu buttons ─────────────────────────────────────────────────
    [Header("Main Menu Buttons")]
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button multiplayerButton;
    [SerializeField] private Button creditsButton;
    [SerializeField] private Button creditsBackButton;
    [SerializeField] private Button optionsButton;

    // ── Mode panel ────────────────────────────────────────────────────────
    [Header("Mode Panel")]
    [SerializeField] private Button startSinglePlayerButton;
    [SerializeField] private Button backToMainButton;

    // ── Multiplayer panel ─────────────────────────────────────────────────
    [Header("Multiplayer Panel")]
    [SerializeField] private Button          hostButton;
    [SerializeField] private Button          joinButton;
    [SerializeField] private TMP_InputField  joinCodeInput;
    [SerializeField] private TextMeshProUGUI ugsStatusText;
    [SerializeField] private TextMeshProUGUI multiplayerErrorText;
    [SerializeField] private Button          backToModeButton;

    // ── Player name ───────────────────────────────────────────────────────
    [Header("Player Name")]
    [SerializeField] private TMP_InputField playerNameInput;

    // ── Waiting lobby panel ───────────────────────────────────────────────
    [Header("Waiting Lobby Panel")]
    [SerializeField] private TextMeshProUGUI sessionCodeText;   
    [SerializeField] private Button          copyCodeButton;    
    [SerializeField] private TextMeshProUGUI waitingPlayerCount;
    [SerializeField] private Transform       waitingPlayerList;
    [SerializeField] private GameObject      playerSlotPrefab;
    [SerializeField] private Button          beginCharSelectButton;
    [SerializeField] private Button          waitingLeaveButton;

    // ── Character select panel ────────────────────────────────────────────
    [Header("Character Select Panel")]
    [SerializeField] private Transform        charSelectPlayerList;
    [SerializeField] private List<Button>     characterButtons;
    [SerializeField] private List<string>     characterNames;
    [SerializeField] private List<Sprite>     characterSprites;
    [SerializeField] private List<GameObject> characterPrefabs;
    [SerializeField] private TextMeshProUGUI  selectedCharacterName;
    [SerializeField] private Color            selectedTint   = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private Color            deselectedTint = new Color(1f, 1f, 1f, 0.4f);
    [SerializeField] private Button           readyButton;
    [SerializeField] private Button           startButton;
    [SerializeField] private Button           singlePlayerStartButton;
    [SerializeField] private Button           charSelectLeaveButton;

    // ── Scenes ────────────────────────────────────────────────────────────
    [Header("Scenes")]
    [SerializeField] private string singlePlayerSceneName = "SinglePlayerScene";
    [SerializeField] private string multiplayerSceneName  = "MultiplayerScene";
    [SerializeField] private string PartyModeSceneName = "PartyModeScene";


    // ── Runtime state ─────────────────────────────────────────────────────
    private int    selectedCharIndex        = 0;
    private bool   isReady                  = false;
    private bool   inCharSelectPhase        = false;
    private bool   isSinglePlayer           = false;
    private bool   isConnecting             = false;
    private bool   lobbySyncCoroutineActive      = false;
    private bool   alreadySubscribedToLobbySync  = false;
    private string currentJoinCode          = "---";

    // ─────────────────────────────────────────────────────────────────────
    // Awake — wire up all button listeners
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        newGameButton    ?.onClick.AddListener(() => ShowPanel(modePanel));
        multiplayerButton?.onClick.AddListener(() => ShowPanel(multiplayerPanel));
        creditsButton    ?.onClick.AddListener(OpenCredits);
        creditsBackButton?.onClick.AddListener(CloseCredits);
        optionsButton?.onClick.AddListener(OpenSettings);

        startSinglePlayerButton?.onClick.AddListener(GoToSinglePlayerCharSelect);
        backToMainButton       ?.onClick.AddListener(() => ShowPanel(mainMenuPanel));
        backToModeButton       ?.onClick.AddListener(OnBackToModeClicked);

        hostButton?.onClick.AddListener(OnHostClicked);
        joinButton?.onClick.AddListener(OnJoinClicked);

        playerNameInput?.onEndEdit.AddListener(OnPlayerNameChanged);

        copyCodeButton?.onClick.AddListener(OnCopyCodeClicked);

        beginCharSelectButton?.onClick.AddListener(OnBeginCharSelectClicked);
        waitingLeaveButton   ?.onClick.AddListener(OnLeaveClicked);

        for (int i = 0; i < characterButtons.Count; i++)
        {
            int idx = i;
            characterButtons[i]?.onClick.AddListener(() => SelectCharacter(idx));

            if (i < characterSprites.Count && characterSprites[i] != null)
            {
                var imgs = characterButtons[i]?.GetComponentsInChildren<Image>();
                if (imgs != null && imgs.Length > 1)
                    imgs[1].sprite = characterSprites[i];
            }
        }

        readyButton            ?.onClick.AddListener(OnReadyClicked);
        startButton            ?.onClick.AddListener(OnStartClicked);
        singlePlayerStartButton?.onClick.AddListener(OnSinglePlayerStartClicked);
        charSelectLeaveButton  ?.onClick.AddListener(OnLeaveClicked);
    }

    private void OnDestroy()
    {
        UnsubscribeFromNetworkGameManager();
        UnsubscribeFromLobbySync();
    }

    public void QuitGame()
    {
    #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
    #else
            Application.Quit();
    #endif
    }

    // ─────────────────────────────────────────────────────────────────────
    // Start — initial panel state
    // ─────────────────────────────────────────────────────────────────────

    private void Start()
    {
        creditsPanel         ?.SetActive(false);
        waitingLobbyPanel    ?.SetActive(false);
        characterSelectPanel ?.SetActive(false);
        loadingPanel         ?.SetActive(false);
        modePanel            ?.SetActive(false);
        multiplayerPanel     ?.SetActive(false);

        beginCharSelectButton?.gameObject.SetActive(false);

        SetSessionCode("---");
        SetUgsStatus("Signing in…");
        SetMultiplayerError(string.Empty);
        SetMultiplayerButtonsInteractable(false);

        RefreshCharacterButtons();
        ShowPanel(mainMenuPanel);

        StartCoroutine(LoadPlayerName());
        StartCoroutine(SubscribeWhenReady());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Coroutines
    // ─────────────────────────────────────────────────────────────────────

    private IEnumerator LoadPlayerName()
    {
        while (NetworkGameManager.Instance == null ||
               string.IsNullOrEmpty(NetworkGameManager.Instance.LocalPlayerId))
            yield return null;

        string key   = $"PlayerName_{NetworkGameManager.Instance.LocalPlayerId}";
        string saved = PlayerPrefs.GetString(key, NetworkGameManager.Instance.LocalPlayerName);
        if (playerNameInput != null) playerNameInput.text = saved;
    }

    private IEnumerator SubscribeWhenReady()
    {
        while (NetworkGameManager.Instance == null) yield return null;
        SubscribeToNetworkGameManager();

        while (string.IsNullOrEmpty(NetworkGameManager.Instance.LocalPlayerId))
            yield return null;

        SetUgsStatus(string.Empty);
        SetMultiplayerButtonsInteractable(true);
    }

    /// <summary>
    /// Waits for LobbySync to be spawned by NGO after a session is established,
    /// then subscribes to its events. Handles late-join char-select catch-up.
    /// </summary>
    private IEnumerator SubscribeToLobbySyncWhenReady()
    {
        lobbySyncCoroutineActive = true;
        Debug.Log("[MainMenuController] Waiting for LobbySync…");

        float elapsed = 0f;
        while (LobbySync.Instance == null)
        {
            elapsed += Time.deltaTime;
            if (elapsed > 30f)
            {
                Debug.LogError("[MainMenuController] Timed out waiting for LobbySync.");
                lobbySyncCoroutineActive        = false;
        alreadySubscribedToLobbySync    = false;
                yield break;
            }
            yield return null;
        }

        Debug.Log("[MainMenuController] LobbySync ready — subscribing.");
        UnsubscribeFromLobbySync();
        SubscribeToLobbySync();

        bool isHost = NetworkGameManager.Instance?.IsHost ?? false;

        // For Widget-path clients/hosts: if the waiting lobby panel isn't showing yet,
        // enter it now. This covers the case where HandleSessionCreated/Joined never fired.
        if (waitingLobbyPanel != null && !waitingLobbyPanel.activeSelf &&
            (characterSelectPanel == null || !characterSelectPanel.activeSelf))
        {
            EnterWaitingLobby(isHost);
        }

        // Always refresh the begin-char-select button after subscribing.
        // EnterWaitingLobby sets it active but disabled; we enable it here.
        if (beginCharSelectButton != null)
        {
            beginCharSelectButton.gameObject.SetActive(isHost);
            beginCharSelectButton.interactable = isHost;
        }

        yield return null;

        if (!inCharSelectPhase && LobbySync.Instance.IsCharSelectPhaseActive)
        {
            Debug.Log("[MainMenuController] Char select already active — catching up immediately.");
            SwitchToCharSelectPhase();
        }
        else if (!inCharSelectPhase)
        {
            float pollElapsed = 0f;
            while (pollElapsed < 3f && !inCharSelectPhase)
            {
                pollElapsed += Time.deltaTime;
                if (LobbySync.Instance != null && LobbySync.Instance.IsCharSelectPhaseActive)
                {
                    Debug.Log("[MainMenuController] Char select detected during poll — catching up.");
                    SwitchToCharSelectPhase();
                    break;
                }
                yield return null;
            }
        }

        lobbySyncCoroutineActive        = false;
        alreadySubscribedToLobbySync    = false;
    }

    private void Update()
    {
        if (!lobbySyncCoroutineActive &&
            !alreadySubscribedToLobbySync &&
            !inCharSelectPhase &&
            LobbySync.Instance != null &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsConnectedClient &&
            !NetworkManager.Singleton.IsHost)
        {
            Debug.Log("[MainMenuController] Fallback: Widget-client detected LobbySync — subscribing.");
            StartCoroutine(SubscribeToLobbySyncWhenReady());
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Event subscriptions
    // ─────────────────────────────────────────────────────────────────────

    private void SubscribeToNetworkGameManager()
    {
        var mgr = NetworkGameManager.Instance;
        if (mgr == null) return;
        mgr.OnSignedIn       += OnUgsSignedIn;
        mgr.OnSignInFailed   += OnUgsSignInFailed;
        mgr.OnSessionCreated += HandleSessionCreated;
        mgr.OnSessionJoined  += HandleSessionJoined;
        mgr.OnSessionLeft    += HandleSessionLeft;
        mgr.OnSessionError   += HandleSessionError;
        mgr.OnPlayersUpdated += HandlePlayersUpdated;
    }

    private void UnsubscribeFromNetworkGameManager()
    {
        var mgr = NetworkGameManager.Instance;
        if (mgr == null) return;
        mgr.OnSignedIn       -= OnUgsSignedIn;
        mgr.OnSignInFailed   -= OnUgsSignInFailed;
        mgr.OnSessionCreated -= HandleSessionCreated;
        mgr.OnSessionJoined  -= HandleSessionJoined;
        mgr.OnSessionLeft    -= HandleSessionLeft;
        mgr.OnSessionError   -= HandleSessionError;
        mgr.OnPlayersUpdated -= HandlePlayersUpdated;
    }

    private void SubscribeToLobbySync()
    {
        if (LobbySync.Instance == null) return;
        LobbySync.Instance.OnCharSelectPhaseStarted += SwitchToCharSelectPhase;
        LobbySync.Instance.OnPlayerDataUpdated      += HandlePlayerDataUpdated;
        alreadySubscribedToLobbySync = true;
        Debug.Log("[MainMenuController] Subscribed to LobbySync.");
    }

    private void UnsubscribeFromLobbySync()
    {
        if (LobbySync.Instance == null) return;
        LobbySync.Instance.OnCharSelectPhaseStarted -= SwitchToCharSelectPhase;
        LobbySync.Instance.OnPlayerDataUpdated      -= HandlePlayerDataUpdated;
    }



    // ─────────────────────────────────────────────────────────────────────
    // UGS callbacks
    // ─────────────────────────────────────────────────────────────────────

    private void OnUgsSignedIn()
    {
        SetUgsStatus(string.Empty);
        SetMultiplayerButtonsInteractable(true);
    }

    private void OnUgsSignInFailed(string error)
    {
        SetUgsStatus("Sign-in failed. Check connection.");
        SetMultiplayerButtonsInteractable(false);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Panel navigation
    // ─────────────────────────────────────────────────────────────────────

    private void ShowPanel(GameObject target)
    {
        mainMenuPanel       ?.SetActive(false);
        modePanel           ?.SetActive(false);
        multiplayerPanel    ?.SetActive(false);
        waitingLobbyPanel   ?.SetActive(false);
        characterSelectPanel?.SetActive(false);
        creditsPanel        ?.SetActive(false);
        settingsPanel       ?.SetActive(false);
        if (target != null) target.SetActive(true);
    }

    private void OpenCredits()  => ShowPanel(creditsPanel);
    private void CloseCredits() => ShowPanel(mainMenuPanel);
    private void OpenSettings()
    {
        ShowPanel(settingsPanel);
    }
    private void GoToSinglePlayerCharSelect()
    {
        isSinglePlayer = true;
        SwitchToCharSelectPhase();
    }

    private void OnBackToModeClicked()
    {
        if (NetworkGameManager.Instance?.CurrentSession != null)
            _ = NetworkGameManager.Instance.LeaveSessionAsync();

        isConnecting = false;
        SetMultiplayerError(string.Empty);
        SetMultiplayerButtonsInteractable(true);
        ShowPanel(modePanel);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Host / Join
    // ─────────────────────────────────────────────────────────────────────

    private void OnHostClicked()
    {
        if (isConnecting) return;
        SetMultiplayerError(string.Empty);
        isConnecting = true;
        SetMultiplayerButtonsInteractable(false);
        _ = NetworkGameManager.Instance?.CreateSessionAsync();
    }

    private void OnJoinClicked()
    {
        if (isConnecting) return;

        string code = joinCodeInput != null ? joinCodeInput.text.Trim().ToUpper() : string.Empty;
        if (string.IsNullOrEmpty(code))
        {
            SetMultiplayerError("Please enter a join code.");
            return;
        }

        SetMultiplayerError(string.Empty);
        isConnecting = true;
        SetMultiplayerButtonsInteractable(false);
        _ = NetworkGameManager.Instance?.JoinSessionAsync(code);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Copy code button — copies the 6-char code to the system clipboard
    // ─────────────────────────────────────────────────────────────────────

    private void OnCopyCodeClicked()
    {
        if (currentJoinCode == "---" || string.IsNullOrEmpty(currentJoinCode)) return;
        GUIUtility.systemCopyBuffer = currentJoinCode;

        // Brief visual confirmation
        var txt = copyCodeButton?.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) StartCoroutine(FlashCopyConfirmation(txt));
    }

    private IEnumerator FlashCopyConfirmation(TextMeshProUGUI label)
    {
        string original = label.text;
        label.text = "Copied!";
        yield return new WaitForSeconds(1.5f);
        label.text = original;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Session callbacks
    // ─────────────────────────────────────────────────────────────────────

    private void HandleSessionCreated()
    {
        isConnecting = false;
        GameManager.SetMode(GameMode.Host);

        currentJoinCode = NetworkGameManager.Instance?.GetJoinCode() ?? "---";
        SetSessionCode(currentJoinCode);

        // Hosts see the code in the waiting lobby — no need to check the terminal
        EnterWaitingLobby(isHost: true);

        if (!lobbySyncCoroutineActive)
            StartCoroutine(SubscribeToLobbySyncWhenReady());
    }

    private void HandleSessionJoined()
    {
        isConnecting = false;
        GameManager.SetMode(GameMode.Client);

        // Clients don't need to display the host's code
        currentJoinCode = "---";
        SetSessionCode("---");

        EnterWaitingLobby(isHost: false);

        if (!lobbySyncCoroutineActive)
            StartCoroutine(SubscribeToLobbySyncWhenReady());
    }

    private void HandleSessionLeft()
    {
        isConnecting             = false;
        inCharSelectPhase        = false;
        isReady                  = false;
        isSinglePlayer           = false;
        lobbySyncCoroutineActive        = false;
        alreadySubscribedToLobbySync    = false;
        currentJoinCode          = "---";

        UnsubscribeFromLobbySync();

        beginCharSelectButton?.gameObject.SetActive(false);
        if (copyCodeButton != null) copyCodeButton.gameObject.SetActive(false);

        SetSessionCode("---");
        SetMultiplayerError(string.Empty);
        SetMultiplayerButtonsInteractable(true);
        ShowPanel(mainMenuPanel);
    }

    private void HandleSessionError(string error)
    {
        isConnecting = false;
        SetMultiplayerButtonsInteractable(true);
        SetMultiplayerError(error);
    }

    private void HandlePlayersUpdated(List<SessionPlayerInfo> players)
    {
        if (inCharSelectPhase) return;
        PopulateWaitingLobbySlots(players);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Waiting lobby
    // ─────────────────────────────────────────────────────────────────────

    private void EnterWaitingLobby(bool isHost)
    {
        inCharSelectPhase = false;
        isReady           = false;

        // Host sees "Begin Char Select" — disabled until LobbySync is ready
        beginCharSelectButton?.gameObject.SetActive(isHost);
        if (beginCharSelectButton != null)
            beginCharSelectButton.interactable = false;

        // Copy button is only useful for the host
        if (copyCodeButton != null)
            copyCodeButton.gameObject.SetActive(isHost);

        var players = NetworkGameManager.Instance?.GetPlayerList();
        if (players != null) PopulateWaitingLobbySlots(players);

        ShowPanel(waitingLobbyPanel);
    }

    private void PopulateWaitingLobbySlots(List<SessionPlayerInfo> players)
    {
        if (waitingPlayerList == null || playerSlotPrefab == null) return;

        foreach (Transform child in waitingPlayerList) Destroy(child.gameObject);

        foreach (var info in players)
        {
            string charName = (info.CharacterIndex >= 0 && info.CharacterIndex < characterNames.Count)
                ? characterNames[info.CharacterIndex] : "Not selected";

            var go = Instantiate(playerSlotPrefab, waitingPlayerList);
            go.GetComponent<PlayerSlotUI>()?.Setup(info, charName);
        }

        if (waitingPlayerCount != null)
        {
            int max = NetworkGameManager.Instance?.GetMaxPlayers() ?? 4;
            waitingPlayerCount.text = $"{players.Count} / {max} players";
        }
    }

    private void OnBeginCharSelectClicked()
    {
        bool isHost = NetworkGameManager.Instance?.IsHost ?? false;
        if (!isHost) return;

        if (LobbySync.Instance == null)
        {
            Debug.LogWarning("[MainMenuController] LobbySync not ready yet.");
            return;
        }

        LobbySync.Instance.BeginCharSelectPhase();
    }

    // ─────────────────────────────────────────────────────────────────────
    // LobbySync callbacks
    // ─────────────────────────────────────────────────────────────────────

    private void HandlePlayerDataUpdated(ulong[] clientIds)
    {
        Transform list = inCharSelectPhase ? charSelectPlayerList : waitingPlayerList;
        if (list == null || playerSlotPrefab == null || LobbySync.Instance == null) return;

        foreach (Transform child in list) Destroy(child.gameObject);

        foreach (ulong id in clientIds)
        {
            int    charIdx  = LobbySync.Instance.GetCharacterIndex(id);
            bool   ready    = LobbySync.Instance.IsReady(id);
            bool   isLocal  = id == LobbySync.Instance.LocalClientId;
            bool   isHost   = id == 0;
            string charName = (charIdx >= 0 && charIdx < characterNames.Count)
                ? characterNames[charIdx] : "Selecting…";

            var info = new SessionPlayerInfo($"{id}", $"Player {id}", charIdx, ready, isLocal, isHost);
            var go   = Instantiate(playerSlotPrefab, list);
            go.GetComponent<PlayerSlotUI>()?.Setup(info, charName);
        }

        if (waitingPlayerCount != null && !inCharSelectPhase)
            waitingPlayerCount.text = $"{clientIds.Length} / 4 players";

        RefreshStartButton();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Character select
    // ─────────────────────────────────────────────────────────────────────

    private void SwitchToCharSelectPhase()
    {
        if (inCharSelectPhase) return;

        inCharSelectPhase = true;
        isReady           = false;
        selectedCharIndex = 0;

        RefreshCharacterButtons();
        UpdateReadyVisual();

        bool isHost = !isSinglePlayer && (NetworkGameManager.Instance?.IsHost ?? false);

        singlePlayerStartButton?.gameObject.SetActive(isSinglePlayer);
        readyButton            ?.gameObject.SetActive(!isSinglePlayer);

        if (startButton != null)
        {
            startButton.gameObject.SetActive(!isSinglePlayer && isHost);
            startButton.interactable = false;
        }

        ShowPanel(characterSelectPanel);
        Debug.Log($"[MainMenuController] → Char select. SP={isSinglePlayer} Host={isHost}");
    }

    private void SelectCharacter(int index)
    {
        selectedCharIndex = index;

        if (!isSinglePlayer)
        {
            LobbySync.Instance?.SetMyCharacter(index);
            NetworkGameManager.Instance?.SetLocalCharacterSelection(index);
        }

        RefreshCharacterButtons();
    }

    private void RefreshCharacterButtons()
    {
        for (int i = 0; i < characterButtons.Count; i++)
        {
            if (characterButtons[i] == null) continue;
            bool sel = (i == selectedCharIndex);
            var  img = characterButtons[i].GetComponent<Image>();
            if (img != null) img.color = sel ? selectedTint : deselectedTint;
            characterButtons[i].transform.localScale = sel ? new Vector3(1.1f, 1.1f, 1f) : Vector3.one;
        }

        if (selectedCharacterName != null && selectedCharIndex < characterNames.Count)
            selectedCharacterName.text = characterNames[selectedCharIndex];
    }

    // ─────────────────────────────────────────────────────────────────────
    // Ready & Start
    // ─────────────────────────────────────────────────────────────────────

    private void OnReadyClicked()
    {
        isReady = !isReady;
        LobbySync.Instance?.SetMyReady(isReady);
        NetworkGameManager.Instance?.SetLocalReadyState(isReady);
        UpdateReadyVisual();
    }

    private void UpdateReadyVisual()
    {
        var txt = readyButton?.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) txt.text = isReady ? "Not Ready" : "Ready!";

        var img = readyButton?.GetComponent<Image>();
        if (img != null) img.color = isReady ? new Color(0.2f, 0.85f, 0.3f, 1f) : Color.white;
    }

    private void RefreshStartButton()
    {
        if (startButton == null || isSinglePlayer) return;

        bool isHost = NetworkGameManager.Instance?.IsHost ?? false;
        if (!isHost) return;

        bool allReady = LobbySync.Instance?.AllPlayersReady()
                     ?? NetworkGameManager.Instance?.AllPlayersReady()
                     ?? false;

        startButton.interactable = allReady;

        var txt = startButton.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null)
        {
            txt.text  = allReady ? "Start Adventure" : "Waiting for players…";
            txt.color = allReady ? Color.white : new Color(1f, 1f, 1f, 0.4f);
        }
    }

    private void OnSinglePlayerStartClicked()
    {
        if (!isSinglePlayer) return;
        LaunchGame(isMultiplayer: false);
    }

    public void OnPartymodeStartClicked(){
        SceneManager.LoadScene(PartyModeSceneName);
    }

    private void OnStartClicked()
    {
        bool isHost = NetworkGameManager.Instance?.IsHost ?? false;
        if (!isHost) return;

        bool allReady = LobbySync.Instance?.AllPlayersReady()
                     ?? NetworkGameManager.Instance?.AllPlayersReady()
                     ?? false;
        if (!allReady) return;

        LaunchGame(isMultiplayer: true);
    }

    private void LaunchGame(bool isMultiplayer)
    {
        CharacterSelection.Index  = selectedCharIndex;
        CharacterSelection.Prefab = GetSelectedPrefab();

        // Set mode BEFORE loading the scene so GameManager has it ready
        if (isMultiplayer)
            GameManager.SetMode(NetworkManager.Singleton?.IsHost ?? false 
                ? GameMode.Host : GameMode.Client);
        else
            GameManager.SetMode(GameMode.Offline);

        loadingPanel?.SetActive(true);
        ShowPanel(null);

        if (isMultiplayer)
        {
            if (NetworkManager.Singleton?.IsHost ?? false)
            {
                NetworkManager.Singleton.SceneManager.LoadScene(
                    multiplayerSceneName,
                    UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
        }
        else
        {
            SceneManager.LoadScene(singlePlayerSceneName);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Leave
    // ─────────────────────────────────────────────────────────────────────

    private void OnLeaveClicked()
    {
        isReady           = false;
        inCharSelectPhase = false;

        if (isSinglePlayer)
        {
            isSinglePlayer = false;
            ShowPanel(modePanel);
        }
        else
        {
            isConnecting = false;
            SetMultiplayerButtonsInteractable(true);
            _ = NetworkGameManager.Instance?.LeaveSessionAsync();
            // HandleSessionLeft fires → ShowPanel(mainMenuPanel)
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private void OnPlayerNameChanged(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        NetworkGameManager.Instance?.SetLocalPlayerName(name);
        PlayerPrefs.SetString("PlayerName", name);
    }

    private GameObject GetSelectedPrefab()
    {
        if (characterPrefabs == null || selectedCharIndex >= characterPrefabs.Count) return null;
        return characterPrefabs[selectedCharIndex];
    }

    private void SetMultiplayerButtonsInteractable(bool interactable)
    {
        if (hostButton != null) hostButton.interactable = interactable;
        if (joinButton != null) joinButton.interactable = interactable;
    }

    private void SetMultiplayerError(string msg)
    {
        if (multiplayerErrorText != null) multiplayerErrorText.text = msg;
    }

    private void SetSessionCode(string code)
    {
        currentJoinCode = code;
        if (sessionCodeText != null)
            sessionCodeText.text = code == "---" ? "Waiting…" : $"{code}";
    }

    private void SetUgsStatus(string msg)
    {
        if (ugsStatusText != null) ugsStatusText.text = msg;
    }
}
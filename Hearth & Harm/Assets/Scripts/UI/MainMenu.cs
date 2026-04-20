using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Unity.Netcode;

/// <summary>
/// Main menu controller.
///
/// PANEL FLOW:
///   mainMenuPanel
///     ├─ modePanel           (New Game / Multiplayer / Credits)
///     │    ├─ multiplayerPanel   (Host | Join, code display, error text)
///     │    └─ singleplayer goes straight to characterSelectPanel
///     ├─ waitingLobbyPanel   (player list, Begin Char Select — host clicks it, clients just wait)
///     ├─ characterSelectPanel
///     └─ creditsPanel
///
/// PHILOSOPHY:
///   Panels are shown or hidden as whole units. No individual buttons or
///   sub-elements are toggled at runtime. If a button should only work for
///   the host, it stays visible but only the host's click does anything.
/// </summary>
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

    // ── Main menu buttons ─────────────────────────────────────────────────
    [Header("Main Menu Buttons")]
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button multiplayerButton;
    [SerializeField] private Button creditsButton;
    [SerializeField] private Button creditsBackButton;

    // ── Mode panel buttons ────────────────────────────────────────────────
    [Header("Mode Panel Buttons")]
    [SerializeField] private Button startSinglePlayerButton;
    [SerializeField] private Button backToMainButton;

    // ── Multiplayer panel ─────────────────────────────────────────────────
    [Header("Multiplayer Panel")]
    [SerializeField] private Button          hostButton;
    [SerializeField] private Button          joinButton;
    [SerializeField] private TMP_InputField  joinCodeInput;
    [SerializeField] private TextMeshProUGUI sessionCodeText;      // always visible, shows "---" until hosting
    [SerializeField] private TextMeshProUGUI multiplayerErrorText; // always visible, empty until error
    [SerializeField] private TextMeshProUGUI ugsStatusText;        // always visible, empty when ready
    [SerializeField] private Button          backToModeButton;
    [SerializeField] private Button          enterLobbyButton;

    // ── Player name ───────────────────────────────────────────────────────
    [Header("Player Name")]
    [SerializeField] private TMP_InputField playerNameInput;

    // ── Waiting lobby panel ───────────────────────────────────────────────
    [Header("Waiting Lobby Panel")]
    [SerializeField] private TextMeshProUGUI waitingPlayerCount;
    [SerializeField] private Transform       waitingPlayerList;
    [SerializeField] private GameObject      playerSlotPrefab;
    [SerializeField] private Button          beginCharSelectButton; // visible to all, only host click works
    [SerializeField] private Button          waitingLeaveButton;

    // ── Character select panel ────────────────────────────────────────────
    [Header("Character Select Panel")]
    [SerializeField] private Transform       charSelectPlayerList;
    [SerializeField] private List<Button>    characterButtons;
    [SerializeField] private List<string>    characterNames;
    [SerializeField] private List<Sprite>    characterSprites;
    [SerializeField] private List<GameObject> characterPrefabs;
    [SerializeField] private TextMeshProUGUI selectedCharacterName;
    [SerializeField] private Color           selectedTint   = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private Color           deselectedTint = new Color(1f, 1f, 1f, 0.4f);
    [SerializeField] private Button          readyButton;       // always visible — host click also acts as ready
    [SerializeField] private Button          startButton;       // always visible — only host click does anything
    [SerializeField] private Button          singlePlayerStartButton; // shown only in singleplayer char select
    [SerializeField] private Button          charSelectLeaveButton;

    // ── Scenes ────────────────────────────────────────────────────────────
    [Header("Scenes")]
    [SerializeField] private string singlePlayerSceneName = "SinglePlayerScene";
    [SerializeField] private string multiplayerSceneName  = "MultiplayerScene";

    // ── Runtime state ─────────────────────────────────────────────────────
    private int  selectedCharIndex = 0;
    private bool isReady           = false;
    private bool inCharSelectPhase = false;
    private bool isSinglePlayer    = false;
    private bool isConnecting      = false;

    // ─────────────────────────────────────────────────────────────────────
    // Awake
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Main menu
        newGameButton   ?.onClick.AddListener(() => ShowPanel(modePanel));
        multiplayerButton?.onClick.AddListener(() => ShowPanel(multiplayerPanel));
        creditsButton   ?.onClick.AddListener(OpenCredits);
        creditsBackButton?.onClick.AddListener(CloseCredits);

        // Mode panel
        startSinglePlayerButton?.onClick.AddListener(GoToSinglePlayerCharSelect);
        backToMainButton       ?.onClick.AddListener(() => ShowPanel(mainMenuPanel));

        // Multiplayer panel
        hostButton      ?.onClick.AddListener(OnHostClicked);
        joinButton      ?.onClick.AddListener(OnJoinClicked);
        backToModeButton?.onClick.AddListener(OnBackToModeClicked);
        enterLobbyButton?.onClick.AddListener(OnEnterLobbyClicked);

        // Player name
        playerNameInput?.onEndEdit.AddListener(OnPlayerNameChanged);

        // Waiting lobby
        beginCharSelectButton?.onClick.AddListener(OnBeginCharSelectClicked);
        waitingLeaveButton   ?.onClick.AddListener(OnLeaveClicked);

        // Character buttons
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

        // Char select actions
        readyButton           ?.onClick.AddListener(OnReadyClicked);
        startButton           ?.onClick.AddListener(OnStartClicked);
        singlePlayerStartButton?.onClick.AddListener(OnSinglePlayerStartClicked);
        charSelectLeaveButton ?.onClick.AddListener(OnLeaveClicked);
    }

    private void OnDestroy()
    {
        UnsubscribeFromNetworkGameManager();

        if (LobbySync.Instance == null) return;
        LobbySync.Instance.OnCharSelectPhaseStarted -= SwitchToCharSelectPhase;
        LobbySync.Instance.OnPlayerDataUpdated      -= HandlePlayerDataUpdated;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Start
    // ─────────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Hide everything except the main menu
        creditsPanel         ?.SetActive(false);
        waitingLobbyPanel    ?.SetActive(false);
        characterSelectPanel ?.SetActive(false);
        loadingPanel         ?.SetActive(false);
        modePanel            ?.SetActive(false);
        multiplayerPanel     ?.SetActive(false);

        // Set placeholder texts so nothing looks broken before a session
        SetSessionCode("---");
        SetMultiplayerError(string.Empty);
        SetUgsStatus("Signing in…");

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

        while (LobbySync.Instance == null) yield return null;
        LobbySync.Instance.OnCharSelectPhaseStarted += SwitchToCharSelectPhase;
        LobbySync.Instance.OnPlayerDataUpdated      += HandlePlayerDataUpdated;
    }

    // ─────────────────────────────────────────────────────────────────────
    // NetworkGameManager subscription
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
    // Panel navigation — only ever show one panel at a time
    // ─────────────────────────────────────────────────────────────────────

    private void ShowPanel(GameObject target)
    {
        mainMenuPanel       ?.SetActive(false);
        modePanel           ?.SetActive(false);
        multiplayerPanel    ?.SetActive(false);
        waitingLobbyPanel   ?.SetActive(false);
        characterSelectPanel?.SetActive(false);
        creditsPanel        ?.SetActive(false);
        target              ?.SetActive(true);
    }

    private void OpenCredits()
    {
        ShowPanel(creditsPanel);
    }

    private void CloseCredits()
    {
        ShowPanel(mainMenuPanel);
    }

    private void GoToSinglePlayerCharSelect()
    {
        isSinglePlayer = true;
        GameManager.SetMultiplayer(false);
        SwitchToCharSelectPhase();
    }

    private void OnBackToModeClicked()
    {
        if (NetworkGameManager.Instance?.CurrentSession != null)
            _ = NetworkGameManager.Instance.LeaveSessionAsync();

        SetMultiplayerError(string.Empty);
        SetSessionCode("---");
        ShowPanel(modePanel);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Multiplayer — host / join
    // ─────────────────────────────────────────────────────────────────────

    private void OnHostClicked()
    {
        if (isConnecting) return;
        SetMultiplayerError(string.Empty);
        SetSessionCode("---");
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

    private void OnEnterLobbyClicked()
    {
        EnterWaitingLobby();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Session callbacks
    // ─────────────────────────────────────────────────────────────────────

    private void HandleSessionCreated()
    {
        isConnecting = false;
        GameManager.SetMultiplayer(true);

        string code = NetworkGameManager.Instance?.GetJoinCode() ?? "---";
        SetSessionCode(code);

        EnterWaitingLobby();
    }

    private void HandleSessionJoined()
    {
        isConnecting = false;
        GameManager.SetMultiplayer(true);
        EnterWaitingLobby();
    }

    private void HandleSessionLeft()
    {
        isConnecting      = false;
        inCharSelectPhase = false;
        isReady           = false;

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

    private void EnterWaitingLobby()
    {
        inCharSelectPhase = false;
        isReady           = false;
        isSinglePlayer    = false;

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
        // Only the host actually does anything — clients see the button but nothing happens
        bool isHost = NetworkGameManager.Instance?.IsHost ?? false;
        if (!isHost) return;

        LobbySync.Instance?.BeginCharSelectPhase();
        SwitchToCharSelectPhase();
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
            int    charIdx = LobbySync.Instance.GetCharacterIndex(id);
            bool   ready   = LobbySync.Instance.IsReady(id);
            bool   isLocal = id == LobbySync.Instance.LocalClientId;
            bool   isHost  = id == 0;

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
        inCharSelectPhase = true;
        isReady           = false;
        selectedCharIndex = 0;

        RefreshCharacterButtons();
        UpdateReadyVisual();
        RefreshStartButton();

        // Show the singleplayer start button only in SP, hide multiplayer buttons
        if (singlePlayerStartButton != null)
            singlePlayerStartButton.gameObject.SetActive(isSinglePlayer);
        if (readyButton != null)
            readyButton.gameObject.SetActive(!isSinglePlayer);
        if (startButton != null)
            startButton.gameObject.SetActive(!isSinglePlayer);

        ShowPanel(characterSelectPanel);
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
            var img  = characterButtons[i].GetComponent<Image>();
            if (img != null) img.color = sel ? selectedTint : deselectedTint;
            characterButtons[i].transform.localScale = sel
                ? new Vector3(1.1f, 1.1f, 1f)
                : Vector3.one;
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
        RefreshStartButton();
    }

    private void UpdateReadyVisual()
    {
        var txt = readyButton?.GetComponentInChildren<TextMeshProUGUI>();
        if (txt != null) txt.text = isReady ? "Not Ready" : "Ready!";

        var img = readyButton?.GetComponent<Image>();
        if (img != null) img.color = isReady
            ? new Color(0.2f, 0.85f, 0.3f, 1f)
            : Color.white;
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

    // Called by the singleplayer-only start button
    private void OnSinglePlayerStartClicked()
    {
        if (!isSinglePlayer) return;
        LaunchGame(isMultiplayer: false);
    }

    // Called by the multiplayer start button (host only does anything)
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
        if (CharacterSelectionSync.Instance != null)
            CharacterSelectionSync.Instance.SubmitCharacterIndex(selectedCharIndex);

        CharacterSelection.Index  = selectedCharIndex;
        CharacterSelection.Prefab = GetSelectedPrefab();

        WaveManager.Instance?.ResetToLevel1();
        EnemyManager.Instance?.ClearAllEnemies();

        loadingPanel?.SetActive(true);
        ShowPanel(null); // hide all nav panels, loading panel sits on top

        if (isMultiplayer)
        {
            if (NetworkManager.Singleton?.IsHost ?? false)
            {
                NetworkManager.Singleton.SceneManager.LoadScene(
                    multiplayerSceneName,
                    UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
            // Non-host clients wait for the host's scene load RPC
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
            GameManager.SetMultiplayer(false);
            ShowPanel(modePanel);
        }
        else
        {
            isConnecting = false;
            SetMultiplayerButtonsInteractable(true);
            _ = NetworkGameManager.Instance?.LeaveSessionAsync();
            // HandleSessionLeft fires and navigates back to main menu
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Player name
    // ─────────────────────────────────────────────────────────────────────

    private void OnPlayerNameChanged(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        NetworkGameManager.Instance?.SetLocalPlayerName(name);
        PlayerPrefs.SetString("PlayerName", name);
    }

    private void Update()
    {
        if (enterLobbyButton == null) return;
        bool connected = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        enterLobbyButton.interactable = connected;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

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

    private void SetMultiplayerError(string message)
    {
        if (multiplayerErrorText == null) return;
        multiplayerErrorText.text = message;
    }

    private void SetSessionCode(string code)
    {
        if (sessionCodeText == null) return;
        sessionCodeText.text = $"Code: {code}";
    }

    private void SetUgsStatus(string message)
    {
        if (ugsStatusText == null) return;
        ugsStatusText.text = message;
    }
}
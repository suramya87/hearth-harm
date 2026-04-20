using System;
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

    // ── Main menu buttons ─────────────────────────────────────────────────
    [Header("Main Menu Buttons")]
    [SerializeField] private Button newGameButton;
    [SerializeField] private Button multiplayerButton;
    [SerializeField] private Button creditsButton;
    [SerializeField] private Button creditsBackButton;

    // ── Mode panel ────────────────────────────────────────────────────────
    [Header("Mode Panel")]
    [SerializeField] private Button startSinglePlayerButton;
    [SerializeField] private Button backToMainButton;

    // ── Multiplayer panel ─────────────────────────────────────────────────
    [Header("Multiplayer Panel")]
    [SerializeField] private TextMeshProUGUI ugsStatusText;       // "Signing in…" etc.
    [SerializeField] private TextMeshProUGUI multiplayerErrorText; // surface errors if needed
    [SerializeField] private Button          backToModeButton;

    // ── Player name ───────────────────────────────────────────────────────
    [Header("Player Name")]
    [SerializeField] private TMP_InputField playerNameInput;

    // ── Waiting lobby panel ───────────────────────────────────────────────
    [Header("Waiting Lobby Panel")]
    [SerializeField] private TextMeshProUGUI sessionCodeText;       // shows join code
    [SerializeField] private TextMeshProUGUI waitingPlayerCount;
    [SerializeField] private Transform       waitingPlayerList;
    [SerializeField] private GameObject      playerSlotPrefab;
    [SerializeField] private Button          beginCharSelectButton; // host-only logic, visible to all
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
    [SerializeField] private Button           startButton;           // host-only
    [SerializeField] private Button           singlePlayerStartButton;
    [SerializeField] private Button           charSelectLeaveButton;

    // ── Scenes ────────────────────────────────────────────────────────────
    [Header("Scenes")]
    [SerializeField] private string singlePlayerSceneName = "SinglePlayerScene";
    [SerializeField] private string multiplayerSceneName  = "MultiplayerScene";

    // ── Runtime state ─────────────────────────────────────────────────────
    private int  selectedCharIndex        = 0;
    private bool isReady                  = false;
    private bool inCharSelectPhase        = false;
    private bool isSinglePlayer           = false;
    private bool lobbySyncCoroutineActive = false;

    // ─────────────────────────────────────────────────────────────────────
    // Awake
    // ─────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        newGameButton    ?.onClick.AddListener(() => ShowPanel(modePanel));
        multiplayerButton?.onClick.AddListener(() => ShowPanel(multiplayerPanel));
        creditsButton    ?.onClick.AddListener(OpenCredits);
        creditsBackButton?.onClick.AddListener(CloseCredits);

        startSinglePlayerButton?.onClick.AddListener(GoToSinglePlayerCharSelect);
        backToMainButton       ?.onClick.AddListener(() => ShowPanel(mainMenuPanel));
        backToModeButton       ?.onClick.AddListener(OnBackToModeClicked);

        playerNameInput?.onEndEdit.AddListener(OnPlayerNameChanged);

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

    // ─────────────────────────────────────────────────────────────────────
    // Start
    // ─────────────────────────────────────────────────────────────────────

    private void Start()
    {
        creditsPanel         ?.SetActive(false);
        waitingLobbyPanel    ?.SetActive(false);
        characterSelectPanel ?.SetActive(false);
        loadingPanel         ?.SetActive(false);
        modePanel            ?.SetActive(false);
        multiplayerPanel     ?.SetActive(false);

        SetSessionCode("---");
        SetUgsStatus("Signing in…");
        SetMultiplayerError(string.Empty);

        if (beginCharSelectButton != null)
            beginCharSelectButton.interactable = false;

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
    }

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
                lobbySyncCoroutineActive = false;
                yield break;
            }
            yield return null;
        }

        Debug.Log("[MainMenuController] LobbySync ready — subscribing.");
        UnsubscribeFromLobbySync();
        SubscribeToLobbySync();

        if (beginCharSelectButton != null)
            beginCharSelectButton.interactable = true;

        yield return null;

        if (!inCharSelectPhase && LobbySync.Instance.IsCharSelectPhaseActive)
        {
            Debug.Log("[MainMenuController] Char select already active — catching up.");
            SwitchToCharSelectPhase();
        }

        lobbySyncCoroutineActive = false;
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

    private void OnUgsSignedIn()    => SetUgsStatus(string.Empty);
    private void OnUgsSignInFailed(string e) => SetUgsStatus("Sign-in failed. Check connection.");

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
        if (target != null) target.SetActive(true);
    }

    private void OpenCredits()  => ShowPanel(creditsPanel);
    private void CloseCredits() => ShowPanel(mainMenuPanel);

    private void GoToSinglePlayerCharSelect()
    {
        isSinglePlayer = true;
        GameManager.SetMode(GameMode.Offline);
        SwitchToCharSelectPhase();
    }

    private void OnBackToModeClicked()
    {
        // Just navigate back so the player can use the Widget UI again.
        SetMultiplayerError(string.Empty);
        ShowPanel(modePanel);
    }


    private void HandleSessionCreated()
    {
        GameManager.SetMode(GameMode.Host);

        string code = NetworkGameManager.Instance?.GetJoinCode() ?? "---";
        SetSessionCode(code);

        EnterWaitingLobby();

        if (!lobbySyncCoroutineActive)
            StartCoroutine(SubscribeToLobbySyncWhenReady());
    }

    private void HandleSessionJoined()
    {
        GameManager.SetMode(GameMode.Client);

        SetSessionCode("---");

        EnterWaitingLobby();

        if (!lobbySyncCoroutineActive)
            StartCoroutine(SubscribeToLobbySyncWhenReady());
    }

    private void HandleSessionLeft()
    {
        inCharSelectPhase        = false;
        isReady                  = false;
        lobbySyncCoroutineActive = false;

        UnsubscribeFromLobbySync();

        if (beginCharSelectButton != null)
            beginCharSelectButton.interactable = false;

        SetSessionCode("---");
        ShowPanel(mainMenuPanel);
    }

    private void HandleSessionError(string error)
    {
        SetMultiplayerError(error);
        ShowPanel(multiplayerPanel);
    }

    private void HandlePlayersUpdated(List<SessionPlayerInfo> players)
    {
        if (inCharSelectPhase) return;
        PopulateWaitingLobbySlots(players);
    }


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

        if (singlePlayerStartButton != null)
            singlePlayerStartButton.gameObject.SetActive(isSinglePlayer);

        if (readyButton != null)
            readyButton.gameObject.SetActive(!isSinglePlayer);

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
            var img  = characterButtons[i].GetComponent<Image>();
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
        LaunchGame(false);
    }

    private void OnStartClicked()
    {
        bool isHost = NetworkGameManager.Instance?.IsHost ?? false;
        if (!isHost) return;

        bool allReady = LobbySync.Instance?.AllPlayersReady()
                     ?? NetworkGameManager.Instance?.AllPlayersReady()
                     ?? false;
        if (!allReady) return;

        LaunchGame(true);
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
        ShowPanel(null);

        if (isMultiplayer)
        {
            if (NetworkManager.Singleton?.IsHost ?? false)
            {
                NetworkManager.Singleton.SceneManager.LoadScene(
                    multiplayerSceneName,
                    UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
            // Clients wait — NGO replicates scene load automatically
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
            GameManager.SetMode(GameMode.Offline);
            ShowPanel(modePanel);
        }
        else
        {
            _ = NetworkGameManager.Instance?.LeaveSessionAsync();
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

    private void SetSessionCode(string code)
    {
        if (sessionCodeText != null) sessionCodeText.text = $"Code: {code}";
    }

    private void SetUgsStatus(string msg)
    {
        if (ugsStatusText != null) ugsStatusText.text = msg;
    }

    private void SetMultiplayerError(string msg)
    {
        if (multiplayerErrorText != null) multiplayerErrorText.text = msg;
    }
}
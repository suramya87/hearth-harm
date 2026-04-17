// MultiplayerMenuController.cs
// ─────────────────────────────────────────────────────────────────────────────
// Handles ONLY networking concerns (host/join, ready-up, scene load).
// All panel transitions are delegated to MenuFlowController.
//
// Attach to any persistent GameObject in the Main Menu scene.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MultiplayerMenuController : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────
    public static MultiplayerMenuController Instance { get; private set; }

    // ── Lobby UI ───────────────────────────────────────────────────────────
    [Header("Connection UI")]
    [SerializeField] private TMP_InputField      joinCodeInput;
    [SerializeField] private TextMeshProUGUI     displayJoinCodeText;
    [SerializeField] private TextMeshProUGUI     statusText;

    [Header("Lobby UI")]
    [SerializeField] private Button              startButton;        // host only
    [SerializeField] private Button              beginCharSelectButton; // host only, in lobby

    [Header("Scene")]
    [SerializeField] private string             gameSceneName = "GameScene";

    // ── Ready tracking ─────────────────────────────────────────────────────
    private readonly HashSet<ulong> _readyClients = new();

    // ══════════════════════════════════════════════════════════════════════
    // Unity lifecycle
    // ══════════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (startButton != null)
        {
            startButton.onClick.AddListener(OnStartClicked);
            startButton.gameObject.SetActive(false);
        }

        if (beginCharSelectButton != null)
        {
            beginCharSelectButton.onClick.AddListener(OnBeginCharSelectClicked);
            beginCharSelectButton.gameObject.SetActive(false);
        }

        SubscribeToBootstrapper();
    }

    private void OnDestroy()
    {
        UnsubscribeFromBootstrapper();
        UnsubscribeFromNetworkManager();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Connection – called by UI buttons in the MultiplayerPanel
    // ══════════════════════════════════════════════════════════════════════

    public async void OnHostClicked()
    {
        GameManager.SetMode(GameMode.Host);
        UpdateStatus("Creating session...");
        await NetworkBootstrapper.Instance.HostGame();
    }

    public async void OnJoinClicked()
    {
        string code = joinCodeInput != null ? joinCodeInput.text.Trim() : "";
        if (string.IsNullOrEmpty(code) || code.Length < 6)
        {
            UpdateStatus("Enter a valid 6-character code.");
            return;
        }
        GameManager.SetMode(GameMode.Client);
        UpdateStatus($"Joining {code.ToUpper()}...");
        await NetworkBootstrapper.Instance.JoinGame(code);
    }

    public void OnDisconnectClicked()
    {
        NetworkBootstrapper.Instance?.Disconnect();
        GameManager.SetMode(GameMode.None);
        _readyClients.Clear();
        UpdateStatus("");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Bootstrapper callbacks
    // ══════════════════════════════════════════════════════════════════════

    private void SubscribeToBootstrapper()
    {
        if (NetworkBootstrapper.Instance == null) return;
        NetworkBootstrapper.Instance.OnJoinCodeReady    += OnJoinCodeReady;
        NetworkBootstrapper.Instance.OnConnected        += OnNetworkConnected;
        NetworkBootstrapper.Instance.OnConnectionFailed += OnConnectionFailed;
    }

    private void UnsubscribeFromBootstrapper()
    {
        if (NetworkBootstrapper.Instance == null) return;
        NetworkBootstrapper.Instance.OnJoinCodeReady    -= OnJoinCodeReady;
        NetworkBootstrapper.Instance.OnConnected        -= OnNetworkConnected;
        NetworkBootstrapper.Instance.OnConnectionFailed -= OnConnectionFailed;
    }

    private void UnsubscribeFromNetworkManager()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback  -= OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void OnJoinCodeReady(string code)
    {
        if (displayJoinCodeText != null) displayJoinCodeText.text = code;
        OnNetworkConnected(); // host path: code ready → go to lobby
    }

    private void OnNetworkConnected()
    {
        // Tell the flow controller to show the lobby
        MenuFlowController.Instance?.TransitionToLobby();
        UpdateStatus("Connected! Waiting in lobby...");

        if (NetworkManager.Singleton.IsHost)
        {
            // Show "Begin Char Select" in lobby (host only)
            if (beginCharSelectButton != null)
                beginCharSelectButton.gameObject.SetActive(true);

            SubscribeToNetworkManager();
        }
    }

    private void OnConnectionFailed(string error)
    {
        UpdateStatus($"Error: {error}");
        GameManager.SetMode(GameMode.None);
        MenuFlowController.Instance?.TransitionToConnectionFailed();
    }

    private void SubscribeToNetworkManager()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback  += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Lobby: move to character select (host button in lobby)
    // ══════════════════════════════════════════════════════════════════════

    private void OnBeginCharSelectClicked()
    {
        if (!NetworkManager.Singleton.IsHost) return;

        _readyClients.Clear();

        if (startButton != null)
        {
            startButton.gameObject.SetActive(true);
            startButton.interactable = false;
        }
        if (beginCharSelectButton != null)
            beginCharSelectButton.gameObject.SetActive(false);

        // ← Replace the old direct call with this:
        LobbyNetwork.Instance?.BroadcastBeginCharSelect();

        UpdateStatus("Pick your character.");
        EvaluateStartButton();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Character selection – called by MenuFlowController when a char button
    // is pressed during the MP flow
    // ══════════════════════════════════════════════════════════════════════

    public void NotifyCharacterSelected(int index)
    {
        // Sync over network
        if (CharacterSelectionSync.Instance != null)
            CharacterSelectionSync.Instance.SubmitCharacterIndex(index);

        UpdateStatus($"Character {index} selected. Waiting for others...");

        // Mark this client ready
        if (LobbyNetwork.Instance != null)
            LobbyNetwork.Instance.SetReadyServerRpc(
                NetworkManager.Singleton.LocalClientId, true);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Ready tracking (host only)
    // ══════════════════════════════════════════════════════════════════════

    private void OnClientConnected(ulong clientId)
    {
        _readyClients.Remove(clientId);
        EvaluateStartButton();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        _readyClients.Remove(clientId);
        EvaluateStartButton();
    }

    /// Called by LobbyNetwork (or equivalent) when a client's ready state changes
    public void HandleReadyChanged(ulong clientId, bool isReady)
    {
        if (isReady) _readyClients.Add(clientId);
        else         _readyClients.Remove(clientId);
        EvaluateStartButton();
    }

    private void EvaluateStartButton()
    {
        if (startButton == null || !NetworkManager.Singleton.IsHost) return;

        int  total    = NetworkManager.Singleton.ConnectedClientsIds.Count;
        bool allReady = _readyClients.Count == total && total > 0;

        startButton.interactable = allReady;
        UpdateStatus(allReady
            ? "All players ready! Press Start."
            : $"Waiting... ({_readyClients.Count}/{total} ready)");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Start game (host only)
    // ══════════════════════════════════════════════════════════════════════

    public void OnStartClicked()
    {
        if (!NetworkManager.Singleton.IsHost) return;

        MenuFlowController.Instance?.TransitionToLoading();

        var status = NetworkManager.Singleton.SceneManager.LoadScene(
            gameSceneName, LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
        {
            UpdateStatus($"Scene load failed: {status}");
            MenuFlowController.Instance?.TransitionToMPCharSelect(); // revert
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Helper
    // ══════════════════════════════════════════════════════════════════════

    private void UpdateStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        if (!string.IsNullOrEmpty(msg)) Debug.Log($"[MultiplayerMenu] {msg}");
    }
}
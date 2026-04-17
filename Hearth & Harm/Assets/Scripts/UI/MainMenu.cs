// MenuFlowController.cs
// ─────────────────────────────────────────────────────────────────────────────
// Single source-of-truth for every panel transition in the Main Menu scene.
// Attach this to a persistent root GameObject (e.g. "MenuManager").
//
// Panel wiring (assign in Inspector):
//   mainMenuPanel        – the "MainMenuPanel" with New Game / Continue / etc.
//   modeSelectorPanel    – the "ModePanel" (SinglePlayer / Multiplayer / Exit)
//   multiplayerPanel     – the "MultiplayerPanel" (Create / Join / code entry)
//   lobbyPanel           – the "LobbyContent" panel
//   charSelectPanel      – the "CharacterSelectPanel" (shared SP + MP)
//   loadingPanel         – the "LOADING" overlay
//
// Dependencies:
//   MultiplayerMenuController  – handles all networking; calls back into this
//                                controller via the public Transition* methods.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum MenuState
{
    Main,
    ModeSelect,
    CharSelectSinglePlayer,
    MultiplayerConnect,
    Lobby,
    CharSelectMultiplayer,
    Loading
}

public class MenuFlowController : MonoBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────
    public static MenuFlowController Instance { get; private set; }

    // ── Panels ─────────────────────────────────────────────────────────────
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject modeSelectorPanel;
    [SerializeField] private GameObject multiplayerPanel;
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private GameObject charSelectPanel;
    [SerializeField] private GameObject loadingPanel;

    // ── Character Select UI (shared) ───────────────────────────────────────
    [Header("Character Selection")]
    [SerializeField] private List<Button>        characterButtons;
    [SerializeField] private TextMeshProUGUI     selectedNameText;
    [SerializeField] private string[]            characterNames = { "SmokeStack", "Sconstance" };

    // ── SP-only: Start Game button (inside charSelectPanel) ────────────────
    [Header("Single Player")]
    [SerializeField] private Button spStartButton;          // only visible in SP flow
    [SerializeField] private string singlePlayerScene = "SinglePlayerScene";

    // ── Scene names ────────────────────────────────────────────────────────
    [Header("Scenes")]
    [SerializeField] private string mainMenuScene = "MainMenu";

    // ── State ──────────────────────────────────────────────────────────────
    private MenuState _currentState = MenuState.Main;
    private readonly Stack<MenuState> _history = new();

    // ── All panels list (for easy hide-all) ───────────────────────────────
    private List<GameObject> _allPanels;

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
        _allPanels = new List<GameObject>
        {
            mainMenuPanel, modeSelectorPanel, multiplayerPanel,
            lobbyPanel, charSelectPanel, loadingPanel
        };

        // Wire shared character buttons once
        for (int i = 0; i < characterButtons.Count; i++)
        {
            int captured = i;
            characterButtons[i].onClick.AddListener(() => OnCharacterSelected(captured));
        }

        if (spStartButton != null)
            spStartButton.onClick.AddListener(OnSPStartClicked);

        GoTo(MenuState.Main, clearHistory: true);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Public navigation – called by UI buttons (no args needed)
    // ══════════════════════════════════════════════════════════════════════

    /// Main menu "New Game / Continue" → mode selector
    public void OnNewGameClicked()    => GoTo(MenuState.ModeSelect);

    /// Mode selector → single-player character select
    public void OnSinglePlayerClicked() => GoTo(MenuState.CharSelectSinglePlayer);

    /// Mode selector → multiplayer connection panel
    public void OnMultiplayerClicked()  => GoTo(MenuState.MultiplayerConnect);

    /// Any "Back" button – pops history
    public void OnBackClicked()         => GoBack();

    /// Exit to OS
    public void OnExitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ══════════════════════════════════════════════════════════════════════
    // Called BY MultiplayerMenuController when networking events fire
    // ══════════════════════════════════════════════════════════════════════

    /// Host created / client joined → show lobby
    public void TransitionToLobby()           => GoTo(MenuState.Lobby);

    /// From lobby "Begin Char Select" button → MP character select
    public void TransitionToMPCharSelect()    => GoTo(MenuState.CharSelectMultiplayer);

    /// Host hit Start → show loading overlay (scene load is in progress)
    public void TransitionToLoading()         => GoTo(MenuState.Loading);

    /// Network error → pop back to multiplayer connection panel
    public void TransitionToConnectionFailed()
    {
        // Don't push to history – this is an error recovery
        HideAll();
        multiplayerPanel.SetActive(true);
        _currentState = MenuState.MultiplayerConnect;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Character selection (shared SP + MP logic)
    // ══════════════════════════════════════════════════════════════════════

    private void OnCharacterSelected(int index)
    {
        CharacterSelection.Index = index;
        UpdateSelectionVisuals(index);

        if (_currentState == MenuState.CharSelectSinglePlayer)
        {
            // SP: just update visuals; player presses the Start button to proceed
        }
        else if (_currentState == MenuState.CharSelectMultiplayer)
        {
            // MP: propagate over network via MultiplayerMenuController
            MultiplayerMenuController.Instance?.NotifyCharacterSelected(index);
        }
    }

    private void UpdateSelectionVisuals(int index)
    {
        if (selectedNameText != null && index < characterNames.Length)
            selectedNameText.text = characterNames[index];
        // Add highlight / preview model logic here if needed
    }

    // ══════════════════════════════════════════════════════════════════════
    // Single-player start
    // ══════════════════════════════════════════════════════════════════════

    private void OnSPStartClicked()
    {
        if (CharacterSelection.Index < 0)
        {
            Debug.LogWarning("[MenuFlow] No character selected yet.");
            return;
        }
        GameManager.SetMode(GameMode.Offline);
        GoTo(MenuState.Loading, clearHistory: true);
        SceneManager.LoadScene(singlePlayerScene);
    }

    // ══════════════════════════════════════════════════════════════════════
    // State machine core
    // ══════════════════════════════════════════════════════════════════════

    private void GoTo(MenuState next, bool clearHistory = false)
    {
        if (clearHistory) _history.Clear();
        else              _history.Push(_currentState);

        _currentState = next;
        ApplyState(next);
    }

    private void GoBack()
    {
        if (_history.Count == 0) return;
        _currentState = _history.Pop();
        ApplyState(_currentState);
    }

    private void ApplyState(MenuState state)
    {
        HideAll();

        switch (state)
        {
            case MenuState.Main:
                mainMenuPanel.SetActive(true);
                break;

            case MenuState.ModeSelect:
                modeSelectorPanel.SetActive(true);
                break;

            case MenuState.CharSelectSinglePlayer:
                charSelectPanel.SetActive(true);
                SetSPCharSelectMode(true);
                UpdateSelectionVisuals(0);
                break;

            case MenuState.MultiplayerConnect:
                multiplayerPanel.SetActive(true);
                break;

            case MenuState.Lobby:
                lobbyPanel.SetActive(true);
                break;

            case MenuState.CharSelectMultiplayer:
                charSelectPanel.SetActive(true);
                SetSPCharSelectMode(false);
                UpdateSelectionVisuals(0);
                break;

            case MenuState.Loading:
                loadingPanel.SetActive(true);
                break;
        }

        Debug.Log($"[MenuFlow] → {state}");
    }

    private void HideAll()
    {
        foreach (var p in _allPanels)
            if (p != null) p.SetActive(false);
    }

    /// Toggle the SP-only "Start Game" button on/off depending on flow
    private void SetSPCharSelectMode(bool isSP)
    {
        if (spStartButton != null)
            spStartButton.gameObject.SetActive(isSP);
    }
}
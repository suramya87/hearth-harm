using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Detects when the player enters the End room and shows the completion panel.
/// </summary>
public class EndRoomUI : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Header("Panel")]
    [SerializeField] private GameObject      panelRoot;
    [SerializeField] private Button          nextLevelButton;
    [SerializeField] private Button          mainMenuButton;
    [SerializeField] private TextMeshProUGUI levelLabel;
    [SerializeField] private TextMeshProUGUI stagesClearedLabel;
    [SerializeField] private TextMeshProUGUI nextLevelLabel;

    private bool shown;

    private void Start()
    {
        nextLevelButton?.onClick.AddListener(OnNextLevel);
        mainMenuButton?.onClick.AddListener(OnMainMenu);
        HidePanel();
    }

    private void OnEnable()
    {
        RoomManager.OnAnyRoomChanged += OnRoomChanged;
        LevelGenerator.OnLevelReady  += OnLevelReady;
    }

    private void OnDisable()
    {
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;
        LevelGenerator.OnLevelReady  -= OnLevelReady;
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom room)
    {
        if (shown || room?.prefabData == null) return;
        if (room.prefabData.roomType == LevelGenerator.RoomType.End)
        { shown = true; ShowPanel(); }
    }

    private void OnLevelReady() { shown = false; HidePanel(); }

    // ── Panel ──────────────────────────────────────────────────────────────

    private void ShowPanel()
    {
        if (panelRoot == null) return;
        panelRoot.SetActive(true);
        Time.timeScale = 0f;

        int level   = WaveManager.Instance?.CurrentLevel  ?? 1;
        int cleared = WaveManager.Instance?.StagesCleared ?? 0;

        if (levelLabel)         levelLabel.text         = $"Level {level} Complete!";
        if (stagesClearedLabel) stagesClearedLabel.text = cleared == 0 ? "First stage cleared!" : $"Stages Cleared: {cleared}";
        if (nextLevelLabel)     nextLevelLabel.text     = $"Next: Level {level + 1}";
    }

    private void HidePanel() { if (panelRoot) panelRoot.SetActive(false); }

    // ── Buttons ────────────────────────────────────────────────────────────

    public void OnNextLevel()
    {
        Time.timeScale = 1f;
        HidePanel();
        shown = false;
        WaveManager.Instance?.AdvanceLevel();
        GameStateManager.Instance?.NotifyLevelAdvanced();
        FindAnyObjectByType<LevelGenerator>()?.GenerateLevel();
    }

    public void OnMainMenu()
    {
        Time.timeScale = 1f;
        WaveManager.Instance?.ResetToLevel1();
        EnemyManager.Instance?.ClearAllEnemies();
        SceneManager.LoadScene(mainMenuSceneName);
    }
}
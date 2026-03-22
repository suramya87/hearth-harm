using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Shows when the player dies.</summary>
public class LoseScreen : MonoBehaviour
{
    public static LoseScreen Instance { get; private set; }

    [SerializeField] private GameObject      panelRoot;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private TextMeshProUGUI stagesClearedLabel;
    [SerializeField] private TextMeshProUGUI levelReachedLabel;
    [SerializeField] private Button          retryButton;
    [SerializeField] private Button          mainMenuButton;
    [SerializeField] private string          mainMenuScene     = "MainMenu";
    [SerializeField] private bool            retryResetsProgress = true;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        retryButton?.onClick.AddListener(OnRetry);
        mainMenuButton?.onClick.AddListener(OnMainMenu);
        HidePanel();
    }

    public static void Show() => Instance?.ShowPanel();

    private void ShowPanel()
    {
        if (panelRoot) panelRoot.SetActive(true);
        int s = WaveManager.Instance?.StagesCleared ?? 0;
        int l = WaveManager.Instance?.CurrentLevel  ?? 1;
        if (titleLabel)         titleLabel.text         = "You Died";
        if (stagesClearedLabel) stagesClearedLabel.text = s == 0 ? "No stages cleared" : $"{s} Stage{(s>1?"s":"")} Cleared";
        if (levelReachedLabel)  levelReachedLabel.text  = $"Reached Level {l}";
        Time.timeScale = 0f;
    }

    private void HidePanel() { if (panelRoot) panelRoot.SetActive(false); }

    public void OnRetry()    { Time.timeScale = 1f; HidePanel(); GameStateManager.Instance?.RestartGame(retryResetsProgress); }
    public void OnMainMenu() { Time.timeScale = 1f; WaveManager.Instance?.ResetToLevel1(); SceneManager.LoadScene(mainMenuScene); }
}
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Handles the lose panel. Win UI handled by EndRoomUI.</summary>
public class GameStateUI : MonoBehaviour
{
    [Header("Gameplay panels (hidden on lose)")]
    [SerializeField] private List<GameObject> gameplayPanels;

    [Header("Lose panel")]
    [SerializeField] private GameObject      losePanel;
    [SerializeField] private TextMeshProUGUI loseMessage;
    [SerializeField] private Button          restartButton;

    [SerializeField] private string loseText = "You Died.\nThe dungeon claims another soul.";

    private void Start()
    {
        restartButton?.onClick.AddListener(() => GameStateManager.Instance?.RestartGame(true));
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnGameLost      += ShowLose;
            GameStateManager.Instance.OnGameRestarted += ShowPlaying;
        }
        ShowPlaying();
    }

    private void OnDestroy()
    {
        if (GameStateManager.Instance == null) return;
        GameStateManager.Instance.OnGameLost      -= ShowLose;
        GameStateManager.Instance.OnGameRestarted -= ShowPlaying;
    }

    private void ShowPlaying()
    {
        foreach (var p in gameplayPanels) if (p) p.SetActive(true);
        if (losePanel) losePanel.SetActive(false);
    }

    private void ShowLose()
    {
        foreach (var p in gameplayPanels) if (p) p.SetActive(false);
        if (losePanel)   losePanel.SetActive(true);
        if (loseMessage) loseMessage.text = loseText;
    }
}
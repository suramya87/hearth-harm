using UnityEngine;

public class SettingsPanelController : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mainMenuPanel;

    public GameObject displayPanel;
    public GameObject graphicsPanel;
    public GameObject languagePanel;
    public GameObject audioPanel;
    public GameObject gameplayPanel;
    public GameObject controlsPanel;

    GameObject currentPanel;

    void OnEnable()
    {
        ShowMain();
    }

    void HideAllSubPanels()
    {
        if (displayPanel) displayPanel.SetActive(false);
        if (graphicsPanel) graphicsPanel.SetActive(false);
        if (languagePanel) languagePanel.SetActive(false);
        if (audioPanel) audioPanel.SetActive(false);
        if (gameplayPanel) gameplayPanel.SetActive(false);
        if (controlsPanel) controlsPanel.SetActive(false);
    }

    public void ShowMain()
    {
        HideAllSubPanels();

        if (mainMenuPanel) mainMenuPanel.SetActive(true);
        currentPanel = null;
    }

    void ShowSub(GameObject panel)
    {
        if (!panel) return;

        if (mainMenuPanel) mainMenuPanel.SetActive(false);
        HideAllSubPanels();

        panel.SetActive(true);
        currentPanel = panel;
    }

    // ---- Button hooks ----
    public void OpenDisplay() => ShowSub(displayPanel);
    public void OpenGraphics() => ShowSub(graphicsPanel);
    public void OpenLanguage() => ShowSub(languagePanel);
    public void OpenAudio() => ShowSub(audioPanel);
    public void OpenGameplay() => ShowSub(gameplayPanel);
    public void OpenControls() => ShowSub(controlsPanel);

    public void Back()
    {
        // If we're in a submenu, go back to main.
        if (currentPanel != null)
            ShowMain();
        else
            gameObject.SetActive(false); // optional: close settings entirely if already at main
    }
}
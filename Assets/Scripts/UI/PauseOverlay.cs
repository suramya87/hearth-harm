using UnityEngine;

public class PauseOverlay : MonoBehaviour
{
    [Header("Pause State Canvases")]
    [SerializeField] private Canvas[] pauseCanvases;

    [Header("Gameplay UI Canvases")]
    [SerializeField] private Canvas[] gameplayCanvases;

    [Header("Settings Canvases")]
    [SerializeField] private Canvas[] settingsCanvases;

    bool isPaused = false;

    void Awake()
    {
        SetCanvases(pauseCanvases, false);
        SetCanvases(settingsCanvases, false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (AreAnyEnabled(settingsCanvases))
            {
                CloseSettings();
            }
            else
            {
                TogglePause();
            }
        }
    }

    void TogglePause()
    {
        isPaused = !isPaused;

        SetCanvases(pauseCanvases, isPaused);
        SetCanvases(gameplayCanvases, !isPaused);
        SetCanvases(settingsCanvases, false);

        Time.timeScale = isPaused ? 0f : 1f;
    }

    // ───────── UI BUTTON HOOKS ─────────

    public void Resume()
    {
        if (!isPaused) return;
        TogglePause();
    }

    public void OpenSettings()
    {
        if (!isPaused) return;

        SetCanvases(pauseCanvases, false);
        SetCanvases(settingsCanvases, true);
    }

    public void CloseSettings()
    {
        SetCanvases(settingsCanvases, false);
        SetCanvases(pauseCanvases, true);
    }

    // ───────── Helpers ─────────

    void SetCanvases(Canvas[] canvases, bool enabled)
    {
        foreach (var canvas in canvases)
        {
            if (canvas != null)
                canvas.enabled = enabled;
        }
    }

    bool AreAnyEnabled(Canvas[] canvases)
    {
        foreach (var canvas in canvases)
        {
            if (canvas != null && canvas.enabled)
                return true;
        }
        return false;
    }
}
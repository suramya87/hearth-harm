using UnityEngine;
using UnityEngine.UI;

public class GameplaySettingsUI : MonoBehaviour
{
    private const string TIMER_ENABLED_KEY = "SpeedrunTimerEnabled";

    [SerializeField] private Toggle speedrunTimerToggle;

    private void OnEnable()
    {
        bool enabled = PlayerPrefs.GetInt(TIMER_ENABLED_KEY, 1) == 1;

        if (speedrunTimerToggle != null)
        {
            speedrunTimerToggle.SetIsOnWithoutNotify(enabled);
            speedrunTimerToggle.onValueChanged.AddListener(SetSpeedrunTimerEnabled);
        }
    }

    private void OnDisable()
    {
        if (speedrunTimerToggle != null)
            speedrunTimerToggle.onValueChanged.RemoveListener(SetSpeedrunTimerEnabled);
    }

    private void SetSpeedrunTimerEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(TIMER_ENABLED_KEY, enabled ? 1 : 0);
        PlayerPrefs.Save();

        SpeedrunTimer timer = FindFirstObjectByType<SpeedrunTimer>();
        if (timer != null)
            timer.SetVisible(enabled);
    }
}
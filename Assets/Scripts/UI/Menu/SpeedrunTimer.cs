using TMPro;
using UnityEngine;

public class SpeedrunTimer : MonoBehaviour
{
    private const string TIMER_ENABLED_KEY = "SpeedrunTimerEnabled";

    [SerializeField] private GameObject timerRoot;
    [SerializeField] private TMP_Text timerText;

    private float elapsed;
    private bool running;
    private bool visible;

    private void Awake()
    {
        visible = PlayerPrefs.GetInt(TIMER_ENABLED_KEY, 1) == 1;
        SetVisible(visible);
    }

    private void Start()
    {
        StartTimer();
    }

    private void Update()
    {
        if (!running)
            return;

        elapsed += Time.deltaTime;

        if (visible)
            RefreshText();
    }

    public void StartTimer()
    {
        elapsed = 0f;
        running = true;
        RefreshText();
    }

    public void StopTimer()
    {
        running = false;
    }

    public void SetVisible(bool value)
    {
        visible = value;

        if (timerRoot != null)
            timerRoot.SetActive(visible);

        if (visible)
            RefreshText();

        PlayerPrefs.SetInt(TIMER_ENABLED_KEY, visible ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void RefreshText()
    {
        if (timerText == null)
            return;

        int totalSeconds = Mathf.FloorToInt(elapsed);
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        int milliseconds = Mathf.FloorToInt((elapsed - totalSeconds) * 1000f);

        timerText.text = $"{minutes:00}:{seconds:00}.{milliseconds:000}";
    }
}
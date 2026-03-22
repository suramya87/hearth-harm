using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles LOSE state (player death). Win/progression goes through EndRoomUI.
/// </summary>
public class GameStateManager : MonoBehaviour
{
    public static GameStateManager Instance { get; private set; }

    public event Action OnGameLost;
    public event Action OnGameRestarted;

    public enum State { Playing, Lost }
    public State CurrentState { get; private set; } = State.Playing;

    private HealthComponent playerHealth;
    private bool            subscribed;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()  => LevelGenerator.OnLevelReady += OnLevelReady;
    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
        Unsubscribe();
    }

    private void Update()
    {
        if (!subscribed) TrySubscribe();
        if (CurrentState == State.Playing && playerHealth != null && playerHealth.IsDead)
            HandleDeath();
    }

    private void OnLevelReady()
    {
        CurrentState = State.Playing;
        subscribed   = false;
        Time.timeScale = 1f;
        TrySubscribe();
    }

    private void TrySubscribe()
    {
        var unit = FindAnyObjectByType<Unit>();
        if (unit == null) return;
        var hc = unit.GetComponent<HealthComponent>();
        if (hc == null || hc == playerHealth) return;
        Unsubscribe();
        playerHealth       = hc;
        playerHealth.OnDeath += HandleDeath;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (playerHealth != null) playerHealth.OnDeath -= HandleDeath;
        playerHealth = null; subscribed = false;
    }

    private void HandleDeath()
    {
        if (CurrentState != State.Playing) return;
        CurrentState = State.Lost;
        OnGameLost?.Invoke();
        LoseScreen.Show();
    }

    public void NotifyLevelAdvanced()
    {
        CurrentState = State.Playing; subscribed = false;
        Time.timeScale = 1f;
        OnGameRestarted?.Invoke();
    }

    public void RestartGame(bool resetProgress = true)
    {
        Time.timeScale = 1f;
        if (resetProgress) WaveManager.Instance?.ResetToLevel1();
        OnGameRestarted?.Invoke();
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen != null) gen.GenerateLevel();
        else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
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
        // In multiplayer, only track the LOCAL player's health
        // FindAnyObjectByType would find remote players too
        Unit unit = null;
    
        if (GameManager.IsMultiplayer)
        {
            // Find the NetworkObject owned by this client
            foreach (var u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
            {
                var bridge = u.GetComponent<Unity.Netcode.NetworkBehaviour>();
                if (bridge != null && bridge.IsOwner)
                {
                    unit = u;
                    break;
                }
            }
        }
        else
        {
            unit = FindAnyObjectByType<Unit>();
        }
    
        if (unit == null) return;
        if (!unit.gameObject.activeInHierarchy) return;
    
        var hc = unit.GetComponent<HealthComponent>();
        if (hc == null || hc == playerHealth) return;
    
        Unsubscribe();
        playerHealth = hc;
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
        CurrentState = State.Playing;
        subscribed = false;
        Unsubscribe();
        OnGameRestarted?.Invoke();
    
        if (GameManager.IsMultiplayer)
        {
            // In multiplayer, host triggers level resync via LevelSyncBridge
            var bridge = UnityEngine.Object.FindAnyObjectByType<LevelSyncBridge>();
            if (bridge != null && bridge.IsServer)
            {
                if (resetProgress) WaveManager.Instance?.ResetToLevel1();
                // Reload scene so NGO re-syncs cleanly
                Unity.Netcode.NetworkManager.Singleton.SceneManager.LoadScene(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                    UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
        }
        else
        {
            if (resetProgress) WaveManager.Instance?.ResetToLevel1();
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }
    }

}
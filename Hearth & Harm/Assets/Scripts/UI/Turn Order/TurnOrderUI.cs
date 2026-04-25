using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draft 1 turn order UI.
/// Responsibilities:
/// - Toggle player/enemy phase overlays
/// - Rebuild enemy turn-order tokens from EnemyTurnQueue
/// - Provide structure for later player token syncing and animation
/// </summary>
public class TurnOrderUI : MonoBehaviour
{

    [System.Serializable]
    private class EnemyTokenPrefabEntry
    {
        public string enemyNameContains;
        public GameObject tokenPrefab;
    }

    [System.Serializable]
    private class PlayerTokenPrefabEntry
    {
        public PlayerClass playerClass;
        public GameObject tokenPrefab;
    }

    [Header("Roots")]
    [SerializeField] private Transform playerTurnsRoot;
    [SerializeField] private Transform enemyTurnsRoot;

    [Header("Phase Overlays")]
    [SerializeField] private GameObject notYoTurnPlayer;
    [SerializeField] private GameObject notYoTurnEnemy;

    [Header("Prefabs")]
    [SerializeField] private List<PlayerTokenPrefabEntry> playerTokenPrefabs = new();
    [SerializeField] private List<EnemyTokenPrefabEntry> enemyTokenPrefabs = new();

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs;

    private readonly List<GameObject> playerTokenObjects = new();
    private readonly List<GameObject> enemyTokenObjects = new();


    private void HandleLevelReady()
    {
        RefreshAll();
    }

    private void HandleRoomChanged(LevelGenerator.PlacedRoom _)
    {
        RefreshAll();
    }

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady += HandleLevelReady;
        RoomManager.OnAnyRoomChanged += HandleRoomChanged;
    }

    private void Start()
    {
        Debug.Log("[TurnOrderUI] Start called.");

        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnPlayerTurnBegin += HandlePlayerTurnBegin;
            TurnSystem.Instance.OnEnemyPhaseBegin += HandleEnemyPhaseBegin;
            TurnSystem.Instance.OnEnemyPhaseEnd += HandleEnemyPhaseEnd;
        }
        else
        {
            Debug.LogWarning("[TurnOrderUI] TurnSystem.Instance was null in Start.");
        }

        if (EnemyTurnQueue.Instance != null)
        {
            EnemyTurnQueue.Instance.OnQueueChanged += RefreshEnemyTokens;
        }
        else
        {
            Debug.LogWarning("[TurnOrderUI] EnemyTurnQueue.Instance was null in Start.");
        }

        if (EnemyManager.Instance != null)
        {
            EnemyManager.Instance.OnEnemyListChanged += RefreshEnemyTokens;
        }
        else
        {
            Debug.LogWarning("[TurnOrderUI] EnemyManager.Instance was null in Start.");
        }

        RefreshAll();
    }

    private void OnDisable()
    {
        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnPlayerTurnBegin -= HandlePlayerTurnBegin;
            TurnSystem.Instance.OnEnemyPhaseBegin -= HandleEnemyPhaseBegin;
            TurnSystem.Instance.OnEnemyPhaseEnd -= HandleEnemyPhaseEnd;
        }

        if (EnemyTurnQueue.Instance != null)
        {
            EnemyTurnQueue.Instance.OnQueueChanged -= RefreshEnemyTokens;
        }

        if (EnemyManager.Instance != null)
        {
            EnemyManager.Instance.OnEnemyListChanged -= RefreshEnemyTokens;
        }

        LevelGenerator.OnLevelReady -= HandleLevelReady;
        RoomManager.OnAnyRoomChanged -= HandleRoomChanged;
    }

    // ── Refresh ───────────────────────────────────────────────────────────

    public void RefreshAll()
    {
        RefreshPhaseOverlays();
        RefreshEnemyTokens();
        RefreshPlayerTokens();
    }

    private void RefreshPhaseOverlays()
    {
        bool isPlayerTurn = TurnSystem.Instance == null || TurnSystem.Instance.IsPlayerTurn;

        if (notYoTurnPlayer != null)
            notYoTurnPlayer.SetActive(!isPlayerTurn);

        if (notYoTurnEnemy != null)
            notYoTurnEnemy.SetActive(isPlayerTurn);
    }

    private void RefreshEnemyTokens()
    {
        Debug.Log("[TurnOrderUI] RefreshEnemyTokens called.");

        ClearEnemyTokens();

        if (enemyTurnsRoot == null || enemyTokenPrefabs == null || enemyTokenPrefabs.Count == 0)
        {
            Debug.LogWarning("[TurnOrderUI] Enemy roots or prefabs missing.");
            return;
        }

        if (EnemyTurnQueue.Instance == null)
        {
            Debug.LogWarning("[TurnOrderUI] EnemyTurnQueue.Instance is null.");
            return;
        }

        List<EnemyUnit> queuedEnemies = EnemyTurnQueue.Instance.GetQueuedEnemies();
        Debug.Log($"[TurnOrderUI] queuedEnemies count: {queuedEnemies.Count}");

        foreach (EnemyUnit enemy in queuedEnemies)
        {
            if (enemy == null || enemy.IsDead) continue;

            GameObject prefab = GetEnemyTokenPrefab(enemy);
            if (prefab == null) continue;

            GameObject tokenObj = Instantiate(prefab, enemyTurnsRoot);
            enemyTokenObjects.Add(tokenObj);

            TurnOrderTokenUI tokenUI = tokenObj.GetComponent<TurnOrderTokenUI>();
            if (tokenUI != null)
            {
                tokenUI.BindEnemy(enemy);
            }

            if (showDebugLogs)
                Debug.Log($"[TurnOrderUI] Added enemy token for {enemy.name}");
        }
    }

    private void RefreshPlayerTokens()
    {
        ClearPlayerTokens();

        if (playerTurnsRoot == null || playerTokenPrefabs == null || playerTokenPrefabs.Count == 0)
            return;

        Unit[] allUnits = FindObjectsByType<Unit>(FindObjectsSortMode.None);

        foreach (Unit unit in allUnits)
        {
            if (unit == null) continue;

            PlayerStats stats = unit.GetComponent<PlayerStats>();
            if (stats == null) continue;

            GameObject prefab = GetPlayerTokenPrefab(unit);
            if (prefab == null) continue;

            GameObject tokenObj = Instantiate(prefab, playerTurnsRoot);
            playerTokenObjects.Add(tokenObj);

            TurnOrderTokenUI tokenUI = tokenObj.GetComponent<TurnOrderTokenUI>();
            if (tokenUI != null)
            {
                tokenUI.BindPlayer(unit);
            }

            if (showDebugLogs)
                Debug.Log($"[TurnOrderUI] Added player token for {unit.name}");
        }
    }

    private void ClearEnemyTokens()
    {
        for (int i = 0; i < enemyTokenObjects.Count; i++)
        {
            if (enemyTokenObjects[i] != null)
                Destroy(enemyTokenObjects[i]);
        }

        enemyTokenObjects.Clear();
    }

    private void ClearPlayerTokens()
    {
        for (int i = 0; i < playerTokenObjects.Count; i++)
        {
            if (playerTokenObjects[i] != null)
                Destroy(playerTokenObjects[i]);
        }

        playerTokenObjects.Clear();
    }

    // ── Phase Events ──────────────────────────────────────────────────────

    private void HandlePlayerTurnBegin()
    {
        if (showDebugLogs)
            Debug.Log("[TurnOrderUI] Player turn began.");

        RefreshPhaseOverlays();
    }

    private void HandleEnemyPhaseBegin()
    {
        if (showDebugLogs)
            Debug.Log("[TurnOrderUI] Enemy phase began.");

        RefreshPhaseOverlays();
        RefreshEnemyTokens();
    }

    private void HandleEnemyPhaseEnd()
    {
        if (showDebugLogs)
            Debug.Log("[TurnOrderUI] Enemy phase ended.");

        RefreshPhaseOverlays();
        RefreshEnemyTokens();
    }

    private GameObject GetEnemyTokenPrefab(EnemyUnit enemy)
    {
        if (enemy == null) return null;
        if (enemyTokenPrefabs == null || enemyTokenPrefabs.Count == 0)
            return null;

        string enemyName = enemy.Stats != null && !string.IsNullOrWhiteSpace(enemy.Stats.enemyName)
            ? enemy.Stats.enemyName.ToLowerInvariant()
            : enemy.name.ToLowerInvariant();

        foreach (var entry in enemyTokenPrefabs)
        {
            if (entry == null || entry.tokenPrefab == null) continue;
            if (string.IsNullOrWhiteSpace(entry.enemyNameContains)) continue;

            string matchText = entry.enemyNameContains.ToLowerInvariant();

            if (enemyName.Contains(matchText))
                return entry.tokenPrefab;
        }

        return enemyTokenPrefabs[0].tokenPrefab;
    }

    private GameObject GetPlayerTokenPrefab(Unit player)
    {
        if (player == null) return null;

        PlayerStats stats = player.GetComponent<PlayerStats>();
        if (stats == null) return null;

        foreach (var entry in playerTokenPrefabs)
        {
            if (entry == null || entry.tokenPrefab == null) continue;

            if (entry.playerClass == stats.playerClass)
                return entry.tokenPrefab;
        }

        return null;
    }
}
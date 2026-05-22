using System.Collections.Generic;
using UnityEngine;


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

    [Header("Enemy Token Animation")]
    [SerializeField] private float enemyTokenHeight = 34f;
    [SerializeField] private float enemyTokenSpacing = 6f;
    [SerializeField] private float enemyTokenMoveSpeed = 12f;

    [SerializeField] private float enemyTokenXOffset = 0f;
    [SerializeField] private float enemyTokenTopY = 150f;

    private readonly Dictionary<EnemyUnit, GameObject> enemyTokenMap = new();
    private readonly Dictionary<GameObject, Vector2> enemyTokenTargets = new();
    private readonly List<GameObject> playerTokenObjects = new();
    private readonly List<GameObject> enemyTokenObjects = new();

    private void Update()
    {
        AnimateEnemyTokens();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying) return;

        RecalculateEnemyTokenTargets();
    }

    private void AnimateEnemyTokens()
    {
        foreach (var pair in enemyTokenTargets)
        {
            GameObject tokenObj = pair.Key;
            if (tokenObj == null) continue;

            RectTransform rect = tokenObj.GetComponent<RectTransform>();
            if (rect == null) continue;

            rect.anchoredPosition = Vector2.Lerp(
                rect.anchoredPosition,
                pair.Value,
                enemyTokenMoveSpeed * Time.deltaTime
            );
        }
    }

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
        if (EnemyManager.Instance != null)
        {
            EnemyManager.Instance.OnEnemyTurnStarted += HandleEnemyTurnStarted;
            EnemyManager.Instance.OnEnemyTurnFinished += HandleEnemyTurnFinished;
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
        if (EnemyManager.Instance != null)
        {
            EnemyManager.Instance.OnEnemyTurnStarted -= HandleEnemyTurnStarted;
            EnemyManager.Instance.OnEnemyTurnFinished -= HandleEnemyTurnFinished;
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
        if (enemyTurnsRoot == null || enemyTokenPrefabs == null || enemyTokenPrefabs.Count == 0)
            return;

        if (EnemyTurnQueue.Instance == null)
            return;

        List<EnemyUnit> queuedEnemies = EnemyTurnQueue.Instance.GetQueuedEnemies();

        // Remove tokens for enemies no longer in queue
        List<EnemyUnit> toRemove = new();

        foreach (var pair in enemyTokenMap)
        {
            if (!queuedEnemies.Contains(pair.Key) || pair.Key == null || pair.Key.IsDead)
            {
                if (pair.Value != null)
                    Destroy(pair.Value);

                toRemove.Add(pair.Key);
            }
        }

        foreach (EnemyUnit enemy in toRemove)
        {
            if (!enemyTokenMap.TryGetValue(enemy, out GameObject tokenObj)) continue;

            enemyTokenObjects.Remove(tokenObj);
            enemyTokenTargets.Remove(tokenObj);

            if (tokenObj != null)
                Destroy(tokenObj);

            enemyTokenMap.Remove(enemy);
        }

        // Create missing tokens
        foreach (EnemyUnit enemy in queuedEnemies)
        {
            if (enemy == null || enemy.IsDead) continue;
            if (enemyTokenMap.ContainsKey(enemy)) continue;

            GameObject prefab = GetEnemyTokenPrefab(enemy);
            if (prefab == null) continue;

            GameObject tokenObj = Instantiate(prefab, enemyTurnsRoot);

            RectTransform spawnedRect = tokenObj.GetComponent<RectTransform>();
            if (spawnedRect != null)
            {
                spawnedRect.anchoredPosition = new Vector2(0, enemyTokenTopY);
            }

            enemyTokenMap[enemy] = tokenObj;
            enemyTokenObjects.Add(tokenObj);

            TurnOrderTokenUI tokenUI = tokenObj.GetComponent<TurnOrderTokenUI>();
            if (tokenUI != null)
            {
                tokenUI.BindEnemy(enemy);
            }
        }

        // Assign target positions based on queue order
        for (int i = 0; i < queuedEnemies.Count; i++)
        {
            EnemyUnit enemy = queuedEnemies[i];
            if (enemy == null || enemy.IsDead) continue;
            if (!enemyTokenMap.TryGetValue(enemy, out GameObject tokenObj)) continue;

            RectTransform rect = tokenObj.GetComponent<RectTransform>();
            if (rect == null) continue;

            Vector2 target = new Vector2(
                enemyTokenXOffset,
                enemyTokenTopY - i * (enemyTokenHeight + enemyTokenSpacing)
            );
            enemyTokenTargets[tokenObj] = target;
        }
        RecalculateEnemyTokenTargets();
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
    private void HandleEnemyTurnStarted(EnemyUnit enemy)
    {
        if (showDebugLogs)
            Debug.Log($"[TurnOrderUI] Highlighting {enemy.name}");

        foreach (var obj in enemyTokenObjects)
        {
            if (obj == null) continue;

            var token = obj.GetComponent<TurnOrderTokenUI>();
            if (token == null) continue;

            if (token.GetBoundEnemy() == enemy)
                token.SetHighlighted(true);
        }
    }

    private void HandleEnemyTurnFinished(EnemyUnit enemy)
    {
        if (showDebugLogs)
            Debug.Log($"[TurnOrderUI] Un-highlighting {enemy.name}");

        foreach (var obj in enemyTokenObjects)
        {
            if (obj == null) continue;

            var token = obj.GetComponent<TurnOrderTokenUI>();
            if (token == null) continue;

            if (token.GetBoundEnemy() == enemy)
                token.SetHighlighted(false);
        }
    }
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
    }

    private void HandleEnemyPhaseEnd()
    {
        if (showDebugLogs)
            Debug.Log("[TurnOrderUI] Enemy phase ended.");

        RefreshPhaseOverlays();
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

    private void RecalculateEnemyTokenTargets()
    {
        if (EnemyTurnQueue.Instance == null) return;

        List<EnemyUnit> queuedEnemies = EnemyTurnQueue.Instance.GetQueuedEnemies();

        for (int i = 0; i < queuedEnemies.Count; i++)
        {
            EnemyUnit enemy = queuedEnemies[i];
            if (enemy == null || enemy.IsDead) continue;
            if (!enemyTokenMap.TryGetValue(enemy, out GameObject tokenObj)) continue;

            Vector2 target = new Vector2(
                enemyTokenXOffset,
                enemyTokenTopY - i * (enemyTokenHeight + enemyTokenSpacing)
            );

            enemyTokenTargets[tokenObj] = target;
        }
    }
}
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TurnSystemUI : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private Button          endTurnButton;
    [SerializeField] private TextMeshProUGUI turnNumberText;

    [Header("Visuals")]
    [SerializeField] private GameObject endTurnFlashOverlay;
    [SerializeField] private GameObject disabledClickFeedback;

    [Header("Timings")]
    [SerializeField] private float flashInterval            = 0.3f;
    [SerializeField] private float disabledFeedbackDuration = 0.15f;

    [SerializeField] private GameObject playerTurnOnlyUI;

    private PlayerStats localStats;
    private Coroutine   flashRoutine;

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;
        SubscribeTurnSystems();
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
        UnsubscribeTurnSystems();
    }

    private void Start()
    {
        endTurnButton?.onClick.AddListener(OnEndTurnClicked);
        SetOverlay(endTurnFlashOverlay,   false);
        SetOverlay(disabledClickFeedback, false);
        UpdateTurnText();

        // Set initial state based on whichever turn system is active.
        SetPlayerTurnUI(IsLocalPlayerTurn());
    }

    private void OnLevelReady()
    {
        SubscribeTurnSystems();
        StartCoroutine(FindPlayerStats());
    }

    private void SubscribeTurnSystems()
    {
        // Singleplayer
        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnChanged     += OnTurnChanged;
            TurnSystem.Instance.OnPlayerTurnBegin += OnPlayerTurnBegin;
            TurnSystem.Instance.OnEnemyPhaseBegin += OnEnemyPhaseBegin;
        }

        // Multiplayer — subscribe to per-player events
        if (NetworkedTurnSystem.Instance != null)
        {
            NetworkedTurnSystem.Instance.OnTurnChanged     += OnTurnChangedMP;
            NetworkedTurnSystem.Instance.OnPlayerTurnBegin += OnPlayerTurnBegin;
            NetworkedTurnSystem.Instance.OnEnemyPhaseBegin += OnEnemyPhaseBegin;
        }
    }

    private void UnsubscribeTurnSystems()
    {
        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnChanged     -= OnTurnChanged;
            TurnSystem.Instance.OnPlayerTurnBegin -= OnPlayerTurnBegin;
            TurnSystem.Instance.OnEnemyPhaseBegin -= OnEnemyPhaseBegin;
        }

        if (NetworkedTurnSystem.Instance != null)
        {
            NetworkedTurnSystem.Instance.OnTurnChanged     -= OnTurnChangedMP;
            NetworkedTurnSystem.Instance.OnPlayerTurnBegin -= OnPlayerTurnBegin;
            NetworkedTurnSystem.Instance.OnEnemyPhaseBegin -= OnEnemyPhaseBegin;
        }
    }

    private IEnumerator FindPlayerStats()
    {
        float elapsed = 0f;
        while (elapsed < 10f)
        {
            // In multiplayer find the LOCAL owned unit's stats.
            Unit unit = GameManager.IsMultiplayer
                ? UnitActionSystem.FindLocalOwnedUnit()
                : FindAnyObjectByType<Unit>();

            if (unit != null)
            {
                localStats = unit.GetComponent<PlayerStats>();
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // ── Update ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (localStats == null) return;

        bool isPlayer     = IsLocalPlayerTurn();
        bool outOfStamina = localStats.currentStamina == 0;

        if (!isPlayer)
        {
            StopFlash();
            SetOverlay(endTurnFlashOverlay, true);
            return;
        }

        if (!outOfStamina)
        {
            StopFlash();
            SetOverlay(endTurnFlashOverlay, false);
            return;
        }

        if (flashRoutine == null)
            flashRoutine = StartCoroutine(FlashRoutine());
    }

    // ── Button ─────────────────────────────────────────────────────────────

    private void OnEndTurnClicked()
    {
        if (!IsLocalPlayerTurn())
        {
            StartCoroutine(DisabledFeedback());
            return;
        }

        if (GameManager.IsMultiplayer)
        {
            NetworkedTurnSystem.Instance?.RequestEndTurn();
        }
        else
        {
            TurnSystem.Instance?.NextTurn();
        }
    }

    // ── Events ─────────────────────────────────────────────────────────────

    private void OnTurnChanged(object s, EventArgs e)    => UpdateTurnText();
    private void OnTurnChangedMP(object s, EventArgs e)  => UpdateTurnText();

    private void OnPlayerTurnBegin()
    {
        SetPlayerTurnUI(true);
        if (endTurnButton) endTurnButton.interactable = true;
        UpdateTurnText();
    }

    private void OnEnemyPhaseBegin()
    {
        SetPlayerTurnUI(false);
        StopFlash();
        if (endTurnButton) endTurnButton.interactable = false;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static bool IsLocalPlayerTurn()
    {
        if (!GameManager.IsMultiplayer)
            return TurnSystem.Instance == null || TurnSystem.Instance.IsPlayerTurn;
        return NetworkedTurnSystem.Instance == null ||
               NetworkedTurnSystem.Instance.IsPlayerPhase;
    }

    // ── Flash ──────────────────────────────────────────────────────────────

    private IEnumerator FlashRoutine()
    {
        while (true)
        {
            SetOverlay(endTurnFlashOverlay, true);
            yield return new WaitForSeconds(flashInterval);
            SetOverlay(endTurnFlashOverlay, false);
            yield return new WaitForSeconds(flashInterval);
        }
    }

    private void StopFlash()
    {
        if (flashRoutine != null) { StopCoroutine(flashRoutine); flashRoutine = null; }
        SetOverlay(endTurnFlashOverlay, false);
    }

    private IEnumerator DisabledFeedback()
    {
        SetOverlay(disabledClickFeedback, true);
        yield return new WaitForSeconds(disabledFeedbackDuration);
        SetOverlay(disabledClickFeedback, false);
    }

    private void UpdateTurnText()
    {
        if (turnNumberText == null) return;
        if (GameManager.IsMultiplayer && NetworkedTurnSystem.Instance != null)
            turnNumberText.text = "TURN " + NetworkedTurnSystem.Instance.TurnNumber;
        else if (TurnSystem.Instance != null)
            turnNumberText.text = "TURN " + TurnSystem.Instance.GetTrunNumber();
    }

    private static void SetOverlay(GameObject obj, bool active)
    {
        if (obj != null) obj.SetActive(active);
    }

    private void SetPlayerTurnUI(bool active)
    {
        if (playerTurnOnlyUI != null)
            playerTurnOnlyUI.SetActive(active);
    }
}
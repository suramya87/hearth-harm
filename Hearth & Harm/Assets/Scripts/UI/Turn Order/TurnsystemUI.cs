using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// End Turn button UI. Flashes when out of stamina, locks during enemy phase.
/// Subscribes to TurnSystem (SP). For MP, extend or swap with MultiplayerTurnSystemUI.
/// </summary>
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
        SubscribeTurnSystem();
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
        UnsubscribeTurnSystem();
    }

    private void Start()
    {
        endTurnButton?.onClick.AddListener(OnEndTurnClicked);
        SetOverlay(endTurnFlashOverlay,   false);
        SetOverlay(disabledClickFeedback, false);
        UpdateTurnText();
        SetPlayerTurnUI(TurnSystem.Instance == null || TurnSystem.Instance.IsPlayerTurn);
    }

    private void OnLevelReady()
    {
        SubscribeTurnSystem();
        StartCoroutine(FindPlayerStats());
    }

    private void SubscribeTurnSystem()
    {
        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnChanged     += OnTurnChanged;
            TurnSystem.Instance.OnPlayerTurnBegin += OnPlayerTurnBegin;
            TurnSystem.Instance.OnEnemyPhaseBegin += OnEnemyPhaseBegin;
        }
    }

    private void UnsubscribeTurnSystem()
    {
        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnTurnChanged     -= OnTurnChanged;
            TurnSystem.Instance.OnPlayerTurnBegin -= OnPlayerTurnBegin;
            TurnSystem.Instance.OnEnemyPhaseBegin -= OnEnemyPhaseBegin;
        }
    }

    private IEnumerator FindPlayerStats()
    {
        float e = 0f;
        while (e < 10f)
        {
            var unit = FindAnyObjectByType<Unit>();
            if (unit != null) { localStats = unit.GetComponent<PlayerStats>(); yield break; }
            e += Time.deltaTime;
            yield return null;
        }
    }

    // ── Update ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (localStats == null) return;
        bool isPlayer     = TurnSystem.Instance == null || TurnSystem.Instance.IsPlayerTurn;
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
        if (TurnSystem.Instance != null && !TurnSystem.Instance.IsPlayerTurn)
        { StartCoroutine(DisabledFeedback()); return; }

        TurnSystem.Instance?.NextTurn();
    }

    // ── Events ─────────────────────────────────────────────────────────────

    private void OnTurnChanged(object s, EventArgs e) => UpdateTurnText();

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
        if (turnNumberText != null && TurnSystem.Instance != null)
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
using System;
using System.Collections.Generic;
using UnityEngine;

public class UnitActionSystemUI : MonoBehaviour
{
    [SerializeField] private Transform actionButtonPrefab;
    [Tooltip("The container transform that holds the action buttons.")]
    [SerializeField] private Transform actionButtonContainerTransform;

    private readonly List<ActionButtonUI> actionButtonUIList = new();

    // Cached so we can unsubscribe from stamina events when the unit changes.
    private PlayerStats subscribedStats;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        if (UnitActionSystem.Instance != null)
        {
            UnitActionSystem.Instance.OnSelectedUnitChanged   += OnSelectedUnitChanged;
            UnitActionSystem.Instance.OnSelectedActionChanged += OnSelectedActionChanged;
        }
        else
        {
            LevelGenerator.OnLevelReady += OnLevelReady;
        }

        CreateUnitActionButtons();
        UpdateSelectedVisual();
    }

    private void OnDestroy()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
        UnsubscribeStamina();

        if (UnitActionSystem.Instance != null)
        {
            UnitActionSystem.Instance.OnSelectedUnitChanged   -= OnSelectedUnitChanged;
            UnitActionSystem.Instance.OnSelectedActionChanged -= OnSelectedActionChanged;
        }
    }

    private void OnLevelReady()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;

        if (UnitActionSystem.Instance != null)
        {
            UnitActionSystem.Instance.OnSelectedUnitChanged   += OnSelectedUnitChanged;
            UnitActionSystem.Instance.OnSelectedActionChanged += OnSelectedActionChanged;
        }

        CreateUnitActionButtons();
        UpdateSelectedVisual();
    }

    // ── Button creation ────────────────────────────────────────────────────

    private void CreateUnitActionButtons()
    {
        if (actionButtonContainerTransform == null)
        {
            Debug.LogError("[UnitActionSystemUI] actionButtonContainerTransform is not assigned!");
            return;
        }

        foreach (Transform child in actionButtonContainerTransform)
            Destroy(child.gameObject);
        actionButtonUIList.Clear();

        UnsubscribeStamina();

        Unit unit = UnitActionSystem.Instance?.GetSelectedUnit();
        if (unit == null) return;

        // Subscribe to stamina changes so affordability updates in real time.
        subscribedStats = unit.GetComponent<PlayerStats>();
        if (subscribedStats != null)
            subscribedStats.OnStaminaChanged += OnStaminaChanged;

        foreach (BaseAction action in unit.GetBaseActionArray())
        {
            Transform      t  = Instantiate(actionButtonPrefab, actionButtonContainerTransform);
            ActionButtonUI ui = t.GetComponent<ActionButtonUI>();
            if (ui != null)
            {
                ui.SetBaseAction(action);
                actionButtonUIList.Add(ui);
            }
        }
    }

    private void UpdateSelectedVisual()
    {
        foreach (ActionButtonUI btn in actionButtonUIList)
            btn?.UpdateSelectedVisual();
    }

    // ── Stamina subscription ───────────────────────────────────────────────

    private void UnsubscribeStamina()
    {
        if (subscribedStats != null)
            subscribedStats.OnStaminaChanged -= OnStaminaChanged;
        subscribedStats = null;
    }

    private void OnStaminaChanged(int current, int max)
    {
        UpdateSelectedVisual();
    }

    // ── Events ─────────────────────────────────────────────────────────────

    private void OnSelectedUnitChanged(object sender, EventArgs e)
    {
        CreateUnitActionButtons();
        UpdateSelectedVisual();
    }

    private void OnSelectedActionChanged(object sender, EventArgs e)
    {
        UpdateSelectedVisual();
    }
}
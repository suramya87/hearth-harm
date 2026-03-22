using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Populates the action bar with one button per action on the selected unit.
/// </summary>
public class UnitActionSystemUI : MonoBehaviour
{
    [SerializeField] private Transform actionButtonPrefab;
    [SerializeField] private Transform actionButtonContainer;

    private readonly List<ActionButtonUI> buttons = new();

    private void Start()
    {
        UnitActionSystem.Instance.OnSelectedUnitChange   += (_,__) => Rebuild();
        UnitActionSystem.Instance.OnSelectedActionChange += (_,__) => RefreshSelected();
        Rebuild();
    }

    private void Update() => RefreshSelected();

    private void Rebuild()
    {
        foreach (Transform c in actionButtonContainer) Destroy(c.gameObject);
        buttons.Clear();

        var unit = UnitActionSystem.Instance?.GetSelectedUnit();
        if (unit == null) return;

        foreach (var action in unit.GetBaseActionArray())
        {
            var t  = Instantiate(actionButtonPrefab, actionButtonContainer);
            var ui = t.GetComponent<ActionButtonUI>();
            ui?.SetBaseAction(action);
            buttons.Add(ui);
        }
    }

    private void RefreshSelected()
    {
        foreach (var b in buttons) b?.UpdateSelectedVisual();
    }
}
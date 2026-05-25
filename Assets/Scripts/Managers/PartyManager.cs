using System;
using System.Collections.Generic;
using UnityEngine;

public class PartyManager : MonoBehaviour
{
    public static PartyManager Instance { get; private set; }

    public event Action<Unit> OnSelectedUnitChanged;
    public event Action OnPartyChanged;

    private readonly List<Unit> partyUnits = new();

    private Unit selectedUnit;

    public Unit SelectedUnit => selectedUnit;
    public IReadOnlyList<Unit> PartyUnits => partyUnits;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            CycleSelectedUnit();
        }
    }

    private void CycleSelectedUnit()
    {
        if (partyUnits.Count <= 1)
            return;

        int currentIndex = partyUnits.IndexOf(selectedUnit);

        if (currentIndex < 0)
        {
            SelectUnit(partyUnits[0]);
            return;
        }

        int nextIndex = (currentIndex + 1) % partyUnits.Count;

        SelectUnit(partyUnits[nextIndex]);
    }

    public void RegisterUnit(Unit unit)
    {
        if (unit == null)
            return;

        if (!partyUnits.Contains(unit))
        {
            partyUnits.Add(unit);
            OnPartyChanged?.Invoke();
        }

        if (selectedUnit == null)
            SelectUnit(unit);
    }

    public void UnregisterUnit(Unit unit)
    {
        if (unit == null)
            return;

        if (partyUnits.Remove(unit))
            OnPartyChanged?.Invoke();

        if (selectedUnit == unit)
        {
            selectedUnit = partyUnits.Count > 0 ? partyUnits[0] : null;
            OnSelectedUnitChanged?.Invoke(selectedUnit);
        }
    }

    public void SelectUnit(Unit unit)
    {
        if (unit == null)
            return;

        if (!partyUnits.Contains(unit))
            return;

        selectedUnit = unit;

        // IMPORTANT: this is probably the missing piece
        UnitActionSystem.Instance?.SetSelectedUnit(unit);

        OnSelectedUnitChanged?.Invoke(selectedUnit);

        CameraController2D.Instance?.SoftFocusOn(unit.transform);

        Debug.Log($"[PartyManager] Selected {unit.name}");
    }


}
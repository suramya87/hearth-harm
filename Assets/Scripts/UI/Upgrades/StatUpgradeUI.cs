using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class StatUpgradeUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Buttons")]
    [SerializeField] private Button strengthButton;
    [SerializeField] private Button constitutionButton;
    [SerializeField] private Button dexterityButton;
    [SerializeField] private Button intelligenceButton;
    [SerializeField] private Button perceptionButton;
    [SerializeField] private Button charismaButton;
    [SerializeField] private Button luckButton;

    private readonly HashSet<RoomGrid> upgradedRooms = new();
    private readonly Queue<Unit> pendingUpgradeUnits = new();

    private Unit currentUpgradeUnit;
    private bool subscribed;

    private void Awake()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);

        HookupButtons();
    }

    private void Start()
    {
        StartCoroutine(SubscribeWhenReady());
    }

    private void OnDestroy()
    {
        if (subscribed && EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= HandleRoomCleared;

        Time.timeScale = 1f;
    }

    private IEnumerator SubscribeWhenReady()
    {
        while (EnemyManager.Instance == null)
            yield return null;

        EnemyManager.Instance.OnRoomCleared += HandleRoomCleared;
        subscribed = true;

        Debug.Log("[StatUpgradeUI] Subscribed to room clear event.");
    }

    private void HandleRoomCleared(RoomGrid clearedRoom)
    {
        RoomGrid currentRoom = RoomManager.Instance != null
            ? RoomManager.Instance.GetCurrentRoomGrid()
            : null;

        if (clearedRoom == null || clearedRoom != currentRoom)
            return;

        if (upgradedRooms.Contains(clearedRoom))
        {
            Debug.Log("[StatUpgradeUI] This room already gave an upgrade.");
            return;
        }

        upgradedRooms.Add(clearedRoom);

        StartPartyUpgradeSequence();
    }

    private void StartPartyUpgradeSequence()
    {
        BuildUpgradeQueue();
        ShowNextUpgradeTarget();
    }

    private void BuildUpgradeQueue()
    {
        pendingUpgradeUnits.Clear();

        if (PartyManager.Instance == null || PartyManager.Instance.PartyUnits.Count == 0)
        {
            Unit fallback = UnitActionSystem.Instance != null
                ? UnitActionSystem.Instance.GetSelectedUnit()
                : null;

            if (fallback != null)
                pendingUpgradeUnits.Enqueue(fallback);

            return;
        }

        Unit selected = PartyManager.Instance.SelectedUnit;

        if (selected != null)
            pendingUpgradeUnits.Enqueue(selected);

        foreach (Unit unit in PartyManager.Instance.PartyUnits)
        {
            if (unit == null || unit == selected)
                continue;

            pendingUpgradeUnits.Enqueue(unit);
        }
    }

    private void ShowNextUpgradeTarget()
    {
        if (pendingUpgradeUnits.Count == 0)
        {
            currentUpgradeUnit = null;

            if (panelRoot != null)
                panelRoot.SetActive(false);

            Time.timeScale = 1f;

            Debug.Log("[StatUpgradeUI] Finished party stat upgrades.");
            return;
        }

        currentUpgradeUnit = pendingUpgradeUnits.Dequeue();

        PartyManager.Instance?.SelectUnit(currentUpgradeUnit);
        CameraController2D.Instance?.ForceCenterOn(currentUpgradeUnit.transform);

        if (panelRoot != null)
            panelRoot.SetActive(true);

        Time.timeScale = 0f;

        Debug.Log($"[StatUpgradeUI] Choose stat for {currentUpgradeUnit.DisplayName}");
    }

    private void HookupButtons()
    {
        if (strengthButton != null)
            strengthButton.onClick.AddListener(() => SelectStat(PlayerStatType.Strength));

        if (constitutionButton != null)
            constitutionButton.onClick.AddListener(() => SelectStat(PlayerStatType.Constitution));

        if (dexterityButton != null)
            dexterityButton.onClick.AddListener(() => SelectStat(PlayerStatType.Dexterity));

        if (intelligenceButton != null)
            intelligenceButton.onClick.AddListener(() => SelectStat(PlayerStatType.Intelligence));

        if (perceptionButton != null)
            perceptionButton.onClick.AddListener(() => SelectStat(PlayerStatType.Perception));

        if (charismaButton != null)
            charismaButton.onClick.AddListener(() => SelectStat(PlayerStatType.Charisma));

        if (luckButton != null)
            luckButton.onClick.AddListener(() => SelectStat(PlayerStatType.Luck));
    }

    private void SelectStat(PlayerStatType statType)
    {
        if (currentUpgradeUnit == null)
        {
            Debug.LogError("[StatUpgradeUI] No current upgrade unit.");
            return;
        }

        PlayerStats playerStats = currentUpgradeUnit.GetComponent<PlayerStats>();

        if (playerStats == null)
        {
            Debug.LogError($"[StatUpgradeUI] Could not find PlayerStats on {currentUpgradeUnit.name}.");
            ShowNextUpgradeTarget();
            return;
        }

        playerStats.IncreaseStat(statType, 1);

        Debug.Log($"[StatUpgradeUI] Increased {statType} for {currentUpgradeUnit.DisplayName}");

        ShowNextUpgradeTarget();
    }
}
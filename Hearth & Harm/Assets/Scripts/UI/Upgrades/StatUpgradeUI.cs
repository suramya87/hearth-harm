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

        ShowPanel();
    }

    private void ShowPanel()
    {
        Debug.Log("[StatUpgradeUI] Showing stat upgrade panel.");

        if (panelRoot != null)
            panelRoot.SetActive(true);

        Time.timeScale = 0f;
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
        PlayerStats playerStats = GetCurrentPlayerStats();

        if (playerStats == null)
        {
            Debug.LogError("[StatUpgradeUI] Could not find PlayerStats.");
            return;
        }

        playerStats.PreviewStatUpgrade(statType);
        playerStats.ConfirmPendingUpgrade();

        Debug.Log($"[StatUpgradeUI] Increased {statType}");

        if (panelRoot != null)
            panelRoot.SetActive(false);

        Time.timeScale = 1f;
    }

    private PlayerStats GetCurrentPlayerStats()
    {
        Unit selectedUnit = UnitActionSystem.Instance != null
            ? UnitActionSystem.Instance.GetSelectedUnit()
            : null;

        if (selectedUnit != null)
        {
            PlayerStats stats = selectedUnit.GetComponent<PlayerStats>();
            if (stats != null)
                return stats;
        }

        return FindFirstObjectByType<PlayerStats>();
    }
}
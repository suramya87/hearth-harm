using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class StatsPanelUI : MonoBehaviour
{
    [System.Serializable]
    private class StatRow
    {
        public PlayerStatType statType;
        public TMP_Text valueText;
        public Button plusButton;
        public GameObject pendingHighlight;
    }

    [Header("Player")]
    [SerializeField] private PlayerStats playerStats;

    [Header("Points")]
    [SerializeField] private TMP_Text perkPointsText;

    [Header("Rows")]
    [SerializeField] private StatRow[] statRows;

    [Header("Confirm")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    private PlayerStatType? pendingStat;

    private void Start()
    {
        if (playerStats == null)
            playerStats = FindFirstObjectByType<PlayerStats>();

        HookupButtons();
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    private void HookupButtons()
    {
        foreach (StatRow row in statRows)
        {
            if (row == null || row.plusButton == null)
                continue;

            PlayerStatType capturedType = row.statType;
            row.plusButton.onClick.AddListener(() => PreviewUpgrade(capturedType));
        }

        if (confirmButton != null)
            confirmButton.onClick.AddListener(ConfirmUpgrade);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(CancelUpgrade);
    }

    private void PreviewUpgrade(PlayerStatType statType)
    {
        if (playerStats == null || playerStats.availablePerkPoints <= 0)
            return;

        pendingStat = statType;
        Refresh();
    }

    private void ConfirmUpgrade()
    {
        if (playerStats == null || !pendingStat.HasValue)
            return;

        if (playerStats.availablePerkPoints <= 0)
            return;

        playerStats.IncreaseStat(pendingStat.Value, 1);
        playerStats.availablePerkPoints--;

        pendingStat = null;
        Refresh();
    }

    private void CancelUpgrade()
    {
        pendingStat = null;
        Refresh();
    }

    private void Refresh()
    {
        if (playerStats == null)
            return;

        if (perkPointsText != null)
            perkPointsText.text = $"Points: {playerStats.availablePerkPoints}";

        bool canSpend = playerStats.availablePerkPoints > 0;

        foreach (StatRow row in statRows)
        {
            if (row == null)
                continue;

            int value = playerStats.GetStatValue(row.statType);

            if (pendingStat.HasValue && pendingStat.Value == row.statType)
                value += 1;

            if (row.valueText != null)
                row.valueText.text = value.ToString();

            if (row.plusButton != null)
                row.plusButton.gameObject.SetActive(canSpend);

            if (row.pendingHighlight != null)
                row.pendingHighlight.SetActive(pendingStat.HasValue && pendingStat.Value == row.statType);
        }

        if (confirmButton != null)
            confirmButton.gameObject.SetActive(pendingStat.HasValue);

        if (cancelButton != null)
            cancelButton.gameObject.SetActive(pendingStat.HasValue);
    }
}
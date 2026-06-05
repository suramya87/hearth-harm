using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ActionButtonUI : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private Button          button;
    [SerializeField] private TextMeshProUGUI actionNameText;
    [SerializeField] private GameObject      selectedHighlight;

    [Header("Details")]
    [SerializeField] private TextMeshProUGUI actionDescText;
    [SerializeField] private Image           actionIcon;
    [SerializeField] private GameObject      staminaCostRoot;
    [SerializeField] private TextMeshProUGUI staminaCostText;

    [Header("Affordability")]
    [Range(0f, 1f)]
    [SerializeField] private float       unaffordableAlpha = 0.4f;
    [SerializeField] private CanvasGroup canvasGroup;

    private BaseAction  action;
    private PlayerStats cachedStats;   // FIX: cache from GetComponentInParent, not GetComponent

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
    }

    public void SetBaseAction(BaseAction a)
    {
        action = a;

        // FIX: PlayerStats lives on the unit root, not on the action component.
        // GetComponentInParent walks up the hierarchy and finds it correctly.
        cachedStats = a.GetComponentInParent<PlayerStats>();

        if (actionNameText) actionNameText.text = a.GetActionName().ToUpper();

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => UnitActionSystem.Instance?.SetSelectedAction(action));

        RefreshIcon();
        RefreshDesc();
        RefreshStamina();
        RefreshAffordability();
    }

    public void UpdateSelectedVisual()
    {
        var sel = UnitActionSystem.Instance?.GetSelectedAction();
        if (selectedHighlight) selectedHighlight.SetActive(sel == action);
        RefreshAffordability();
    }

    // ── Refresh helpers ────────────────────────────────────────────────────

    private void RefreshIcon()
    {
        if (actionIcon == null) return;
        if (action is CombatAction ca && ca.ActionData?.icon != null)
        { actionIcon.sprite = ca.ActionData.icon; actionIcon.enabled = true; }
        else actionIcon.enabled = false;
    }

    private void RefreshDesc()
    {
        if (actionDescText == null) return;
        if (action is CombatAction ca && ca.ActionData != null)
        {
            var d = ca.ActionData;
            actionDescText.text = d.useDiceDamage
                ? $"{d.diceCount}d{(int)d.dieType}" + (d.flatBonus != 0 ? $"+{d.flatBonus}" : "")
                : d.baseDamage.ToString();
        }
        else actionDescText.text = "";
    }

    private void RefreshStamina()
    {
        if (staminaCostRoot == null) return;
        if (action is CombatAction ca && ca.ActionData != null)
        {
            int cost = ca.ActionData.staminaCost;
            staminaCostRoot.SetActive(cost > 0);
            if (staminaCostText) staminaCostText.text = cost.ToString();
        }
        else if (action is MoveAction)
        {
            staminaCostRoot.SetActive(true);
            if (staminaCostText) staminaCostText.text = "1";
        }
        else staminaCostRoot.SetActive(false);
    }

    private void RefreshAffordability()
    {
        if (canvasGroup == null || button == null) return;
        bool ok = CanAfford();
        canvasGroup.alpha   = ok ? 1f : unaffordableAlpha;
        button.interactable = ok;
    }

    private bool CanAfford()
    {
        if (action == null) return false;

        // If there's no stamina system at all, always allow.
        if (cachedStats == null) return true;

        if (action is MoveAction)
            return cachedStats.currentStamina >= 1;

        if (action is CombatAction ca && ca.ActionData != null)
            return cachedStats.currentStamina >= ca.ActionData.staminaCost;

        return true;
    }
}
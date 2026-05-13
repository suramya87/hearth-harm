using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class EnemyHealthContainerUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Fire States")]
    [SerializeField] private GameObject fireMax;
    [SerializeField] private GameObject fireHigh;
    [SerializeField] private GameObject fireMedium;
    [SerializeField] private GameObject fireLow;

    [Header("UI")]
    [SerializeField] private GameObject hoverOverlay;
    [SerializeField] private TextMeshProUGUI healthText;

    [Header("Root")]
    [SerializeField] private GameObject root;

    [Header("Hide")]
    [SerializeField] private float hideDelay = 2f;

    private HealthComponent bound;
    private int currentTier = -1;
    private Coroutine hideCoroutine;

    private void Awake()
    {
        if (root != null)
            root.SetActive(false);
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void OnDestroy()
    {
        Unbind();
    }

    public void SetTarget(HealthComponent hc)
    {
        Unbind();

        if (hc == null)
        {
            Hide();
            return;
        }

        bound = hc;
        bound.OnHealthChanged += OnHealthChanged;

        if (root != null)
            root.SetActive(true);

        OnHealthChanged(bound.CurrentHealth, bound.MaxHealth);

        if (hideCoroutine != null)
            StopCoroutine(hideCoroutine);

        hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    public void ClearTarget()
    {
        Unbind();
        Hide();
    }

    private void Unbind()
    {
        if (bound != null)
            bound.OnHealthChanged -= OnHealthChanged;

        bound = null;
        currentTier = -1;

        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
            hideCoroutine = null;
        }
    }

    private void OnHealthChanged(int cur, int max)
    {
        if (root != null)
            root.SetActive(true);

        if (healthText != null)
            healthText.text = cur.ToString();

        float pct = max > 0 ? (float)cur / max : 0f;
        UpdateFire(pct);
    }

    private void UpdateFire(float pct)
    {
        int tier = pct > 0.75f ? 3 : pct > 0.5f ? 2 : pct > 0.25f ? 1 : 0;

        if (tier == currentTier)
            return;

        currentTier = tier;

        if (fireMax != null) fireMax.SetActive(tier == 3);
        if (fireHigh != null) fireHigh.SetActive(tier == 2);
        if (fireMedium != null) fireMedium.SetActive(tier == 1);
        if (fireLow != null) fireLow.SetActive(tier == 0);
    }

    private void Hide()
    {
        if (root != null)
            root.SetActive(false);
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(hideDelay);
        Hide();
        hideCoroutine = null;
    }

    public void OnPointerEnter(PointerEventData _)
    {
        if (hoverOverlay != null)
            hoverOverlay.SetActive(true);
    }

    public void OnPointerExit(PointerEventData _)
    {
        if (hoverOverlay != null)
            hoverOverlay.SetActive(false);
    }
}
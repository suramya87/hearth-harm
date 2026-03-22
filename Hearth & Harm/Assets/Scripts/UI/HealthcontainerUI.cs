using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Shows the local player's health. Auto-binds to the Unit after level ready.
/// Works in SP and MP (just needs a HealthComponent on the player).
/// </summary>
public class HealthContainerUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Fire States")]
    [SerializeField] private GameObject fireMax, fireHigh, fireMedium, fireLow;

    [Header("UI")]
    [SerializeField] private GameObject      hoverOverlay;
    [SerializeField] private TextMeshProUGUI healthText;

    [Header("Damage Flash")]
    [SerializeField] private GameObject damageFlashUI;
    [SerializeField] private float      flashDuration = 0.25f;

    private HealthComponent bound;
    private int             lastHealth = -1;
    private int             currentTier = -1;

    private void OnEnable()  => LevelGenerator.OnLevelReady += OnLevelReady;
    private void OnDisable() { LevelGenerator.OnLevelReady -= OnLevelReady; Unbind(); }

    private void OnLevelReady() { Unbind(); StartCoroutine(WaitAndBind()); }

    private IEnumerator WaitAndBind()
    {
        float t = 0f;
        while (t < 10f)
        {
            var unit = FindFirstObjectByType<Unit>();
            if (unit != null)
            {
                var hc = unit.GetComponent<HealthComponent>();
                if (hc != null) { Bind(hc); yield break; }
            }
            t += Time.deltaTime;
            yield return null;
        }
    }

    private void Bind(HealthComponent hc)
    {
        bound = hc;
        bound.OnHealthChanged += OnHealthChanged;
        OnHealthChanged(hc.CurrentHealth, hc.MaxHealth);
    }

    private void Unbind()
    {
        if (bound != null) bound.OnHealthChanged -= OnHealthChanged;
        bound = null;
    }

    private void OnHealthChanged(int cur, int max)
    {
        if (cur < lastHealth) TriggerFlash();
        lastHealth = cur;
        if (healthText) healthText.text = cur.ToString();
        UpdateFire((float)cur / max);
    }

    private void UpdateFire(float pct)
    {
        int tier = pct > 0.75f ? 3 : pct > 0.5f ? 2 : pct > 0.25f ? 1 : 0;
        if (tier == currentTier) return;
        currentTier = tier;
        if (fireMax)    fireMax.SetActive(tier == 3);
        if (fireHigh)   fireHigh.SetActive(tier == 2);
        if (fireMedium) fireMedium.SetActive(tier == 1);
        if (fireLow)    fireLow.SetActive(tier == 0);
    }

    private void TriggerFlash()
    {
        if (!damageFlashUI) return;
        StopAllCoroutines();
        StartCoroutine(Flash());
    }

    private IEnumerator Flash()
    {
        damageFlashUI.SetActive(true);
        yield return new WaitForSeconds(flashDuration);
        damageFlashUI.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData _) { if (hoverOverlay) hoverOverlay.SetActive(true); }
    public void OnPointerExit(PointerEventData _)  { if (hoverOverlay) hoverOverlay.SetActive(false); }
}
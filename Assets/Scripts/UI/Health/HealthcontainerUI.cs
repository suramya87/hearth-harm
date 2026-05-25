using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

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

    // FIX: track the bind coroutine separately so TriggerFlash can't kill it
    private Coroutine bindCoroutine;
    private Coroutine flashCoroutine;

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;

        // FIX: also try to bind immediately in case the level is already ready
        // (e.g. UI enabled after level generation, or domain reload in editor)
        TryBindImmediate();
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;
        Unbind();

        if (PartyManager.Instance != null)
            PartyManager.Instance.OnSelectedUnitChanged -= HandleSelectedUnitChanged;
    }

    private void OnLevelReady()
    {
        Unbind();
        if (bindCoroutine != null) StopCoroutine(bindCoroutine);
        bindCoroutine = StartCoroutine(WaitAndBind());
    }

    // FIX: synchronous bind attempt — covers the case where the player already
    // exists when this component enables (common after a scene reload or hot-reload).
    private void TryBindImmediate()
    {
        if (PartyManager.Instance != null)
        {
            PartyManager.Instance.OnSelectedUnitChanged -= HandleSelectedUnitChanged;
            PartyManager.Instance.OnSelectedUnitChanged += HandleSelectedUnitChanged;

            if (PartyManager.Instance.SelectedUnit != null)
            {
                BindToUnit(PartyManager.Instance.SelectedUnit);
                return;
            }
        }

        if (bound == null)
        {
            if (bindCoroutine != null) StopCoroutine(bindCoroutine);
            bindCoroutine = StartCoroutine(WaitAndBind());
        }
    }

    private IEnumerator WaitAndBind()
    {
        float t = 0f;

        while (t < 10f)
        {
            if (PartyManager.Instance != null && PartyManager.Instance.SelectedUnit != null)
            {
                BindToUnit(PartyManager.Instance.SelectedUnit);
                bindCoroutine = null;
                yield break;
            }

            t += Time.deltaTime;
            yield return null;
        }

        Debug.LogWarning("[HealthContainerUI] Could not find selected unit HealthComponent after 10s.");
        bindCoroutine = null;
    }

    private void Bind(HealthComponent hc)
    {
        Unbind(); // safe to call even if bound == null
        bound = hc;
        bound.OnHealthChanged += OnHealthChanged;

        // FIX: force a full refresh so the UI shows correct values immediately,
        // even if InitializeHealth already fired before we subscribed
        OnHealthChanged(hc.CurrentHealth, hc.MaxHealth);
    }

    private void Unbind()
    {
        if (bound != null) bound.OnHealthChanged -= OnHealthChanged;
        bound = null;
        lastHealth = -1;
        currentTier = -1;
    }

    private void OnHealthChanged(int cur, int max)
    {
        bool tookDamage = lastHealth >= 0 && cur < lastHealth;
        if (tookDamage) TriggerFlash();
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
        // FIX: stop only the flash coroutine, not ALL coroutines (which would kill WaitAndBind)
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(Flash());
    }

    private IEnumerator Flash()
    {
        damageFlashUI.SetActive(true);
        yield return new WaitForSeconds(flashDuration);
        damageFlashUI.SetActive(false);
        flashCoroutine = null;
    }

    public void OnPointerEnter(PointerEventData _) { if (hoverOverlay) hoverOverlay.SetActive(true); }
    public void OnPointerExit(PointerEventData _)  { if (hoverOverlay) hoverOverlay.SetActive(false); }




    private void HandleSelectedUnitChanged(Unit unit)
    {
        BindToUnit(unit);
    }

    private void BindToUnit(Unit unit)
    {
        if (unit == null)
            return;

        HealthComponent hc = unit.GetComponent<HealthComponent>();

        if (hc == null)
            return;

        Bind(hc);

        Debug.Log($"[HealthContainerUI] Bound to {unit.name}");
    }
}
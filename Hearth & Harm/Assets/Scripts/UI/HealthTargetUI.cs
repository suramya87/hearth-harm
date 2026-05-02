using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class HealthTargetUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Fire States")]
    [SerializeField] private GameObject fireMax, fireHigh, fireMedium, fireLow;
    [Header("UI")]
    [SerializeField] private GameObject      hoverOverlay;
    [SerializeField] private TextMeshProUGUI healthText;
    [Header("Damage Flash")]
    [SerializeField] private GameObject damageFlashUI;
    [SerializeField] private float      flashDuration = 0.25f;

    private HealthComponent target;
    private int lastHealth = -1, tier = -1;

    private void OnEnable()  { if (target) Bind(target); }
    private void OnDisable() { Unbind(); }

    public void SetTarget(HealthComponent hc)
    {
        if (hc == target) return;
        Unbind(); target = hc;
        if (target != null) { gameObject.SetActive(true); Bind(target); }
    }

    public void ClearTarget() { Unbind(); target = null; gameObject.SetActive(false); }

    private void Bind(HealthComponent hc)
    {
        hc.OnHealthChanged += OnChanged;
        OnChanged(hc.CurrentHealth, hc.MaxHealth);
    }

    private void Unbind()
    {
        if (target != null) target.OnHealthChanged -= OnChanged;
    }

    private void OnChanged(int cur, int max)
    {
        if (cur < lastHealth && damageFlashUI) StartCoroutine(Flash());
        lastHealth = cur;
        if (healthText) healthText.text = cur.ToString();
        int t = cur > max * 0.75f ? 3 : cur > max * 0.5f ? 2 : cur > max * 0.25f ? 1 : 0;
        if (t == tier) return; tier = t;
        if (fireMax)    fireMax.SetActive(t==3);
        if (fireHigh)   fireHigh.SetActive(t==2);
        if (fireMedium) fireMedium.SetActive(t==1);
        if (fireLow)    fireLow.SetActive(t==0);
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
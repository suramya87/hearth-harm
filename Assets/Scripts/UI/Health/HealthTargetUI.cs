using System.Collections;
using UnityEngine;


public class HealthTargetUI : MonoBehaviour
{
    [SerializeField] private GameObject healthBarRoot;
    [SerializeField] private UnityEngine.UI.Slider slider;
    [SerializeField] private float hideDelay = 2f;
    [SerializeField] private float animSpeed = 5f;

    private Coroutine hideCoroutine;
    private float targetFill;
    private HealthComponent currentTarget; // Track the current unit

    private void Awake()
    {
        if (healthBarRoot != null) healthBarRoot.SetActive(false);
    }

    public void SetTarget(HealthComponent hc)
        {
            // Unsubscribe from previous target if exists
            ClearTarget();

            if (hc != null)
            {
                currentTarget = hc;
                
                // Subscribe to the event defined in your HealthComponent
                currentTarget.OnHealthChanged += OnChanged;

                // FIX: Access the public Properties CurrentHealth and MaxHealth
                OnChanged(currentTarget.CurrentHealth, currentTarget.MaxHealth);
            }
        }

    public void ClearTarget()
    {
        if (currentTarget != null)
        {
            currentTarget.OnHealthChanged -= OnChanged;
            currentTarget = null;
        }

        if (healthBarRoot != null) healthBarRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        ClearTarget(); // Cleanup on destroy
    }

    // ── Public callback — called by HealthComponent.OnHealthChanged ────────

    public void OnChanged(int curr, int max)
    {
        // Don't start coroutines on inactive or destroyed objects
        if (!gameObject.activeInHierarchy) return;

        targetFill = max > 0 ? (float)curr / max : 0f;

        if (healthBarRoot != null) healthBarRoot.SetActive(true);

        // Cancel any pending hide so the bar stays visible during combat
        if (hideCoroutine != null) StopCoroutine(hideCoroutine);

        StartCoroutine(AnimateBar());
        hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    // ── Coroutines ─────────────────────────────────────────────────────────

    private IEnumerator AnimateBar()
    {
        if (slider == null) yield break;

        while (!Mathf.Approximately(slider.value, targetFill))
        {
            // Stop if deactivated mid-animation (e.g. enemy dies)
            if (!gameObject.activeInHierarchy) yield break;

            slider.value = Mathf.MoveTowards(slider.value, targetFill, animSpeed * Time.deltaTime);
            yield return null;
        }
        slider.value = targetFill;
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(hideDelay);

        if (gameObject.activeInHierarchy && healthBarRoot != null)
            healthBarRoot.SetActive(false);
    }
}
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class StaminaContainerUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Particles")]
    [SerializeField] private StaminaParticleUI particlePrefab;
    [SerializeField] private RectTransform     particleLayer;

    [Header("UI")]
    [SerializeField] private GameObject        hoverOverlay;
    [SerializeField] private TextMeshProUGUI   staminaText;

    private RectTransform           rect;
    private PlayerStats             stats;
    private int                     lastStamina = -1;
    private readonly List<StaminaParticleUI> particles = new();

    // Physics params
    private const float MouseForceRadius   = 60f;
    private const float MouseForceStrength = 300f;
    private const float RepelRadius        = 18f;
    private const float RepelStrength      = 80f;

    private void Awake() => rect = GetComponent<RectTransform>();

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady += OnLevelReady;

        if (PartyManager.Instance != null)
            PartyManager.Instance.OnSelectedUnitChanged += HandleSelectedUnitChanged;

        TryBindToSelectedUnit();
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady -= OnLevelReady;

        if (PartyManager.Instance != null)
            PartyManager.Instance.OnSelectedUnitChanged -= HandleSelectedUnitChanged;
    }

    private void OnLevelReady() => StartCoroutine(WaitAndBind());

    private IEnumerator WaitAndBind()
    {
        float t = 0f;

        while (t < 10f)
        {
            if (PartyManager.Instance != null && PartyManager.Instance.SelectedUnit != null)
            {
                BindToUnit(PartyManager.Instance.SelectedUnit);
                yield break;
            }

            t += Time.deltaTime;
            yield return null;
        }

        Debug.LogWarning("[StaminaContainerUI] Could not bind to selected unit after 10s.");
    }

    private void Update()
    {
        if (stats == null) return;
        if (stats.currentStamina != lastStamina) { lastStamina = stats.currentStamina; SyncParticles(); SyncText(); }
        ApplyMouseDisturbance();
        ApplyRepulsion();
    }

    // ── Particles ──────────────────────────────────────────────────────────

    private void SyncParticles()
    {
        int target = lastStamina;
        while (particles.Count > target) { Destroy(particles[^1].gameObject); particles.RemoveAt(particles.Count-1); }
        while (particles.Count < target) SpawnParticle();
    }

    private void SpawnParticle()
    {
        var p = Instantiate(particlePrefab, particleLayer, false);
        var b = InnerBounds();
        p.GetComponent<RectTransform>().anchoredPosition = new Vector2(
            Random.Range(b.xMin, b.xMax), Random.Range(b.yMin, b.yMax));
        p.Initialize(b);
        particles.Add(p);
    }

    private void SyncText() { if (staminaText) staminaText.text = lastStamina.ToString(); }

    // ── Physics ────────────────────────────────────────────────────────────

    private void ApplyMouseDisturbance()
    {
        if (particleLayer == null) return;

        Camera uiCamera = null;
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            uiCamera = canvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            particleLayer, Input.mousePosition, uiCamera, out var local);

        if (!particleLayer.rect.Contains(local)) return;

        foreach (var p in particles)
        {
            var particleRect = p.GetComponent<RectTransform>();
            var dir = particleRect.anchoredPosition - local;
            float d = dir.magnitude;

            if (d < MouseForceRadius && d > 0.001f)
            {
                float strength = (1f - d / MouseForceRadius) * MouseForceStrength;
                p.ApplyForce(dir.normalized * strength * Time.deltaTime);
            }
        }
    }

    private void ApplyRepulsion()
    {
        for (int i = 0; i < particles.Count; i++)
        for (int j = i+1; j < particles.Count; j++)
        {
            var dir = particles[i].GetComponent<RectTransform>().anchoredPosition -
                      particles[j].GetComponent<RectTransform>().anchoredPosition;
            float d = dir.magnitude;
            if (d < RepelRadius && d > 0.01f)
            {
                var f = dir.normalized * (1f - d/RepelRadius) * RepelStrength;
                particles[i].ApplyForce( f * Time.deltaTime);
                particles[j].ApplyForce(-f * Time.deltaTime);
            }
        }
    }

    private Rect InnerBounds() { var s = rect.rect.size * 0.5f; return new Rect(-s, s*2f); }

    public void OnPointerEnter(PointerEventData _) { if (hoverOverlay) hoverOverlay.SetActive(true); }
    public void OnPointerExit(PointerEventData _)  { if (hoverOverlay) hoverOverlay.SetActive(false); }




    private void HandleSelectedUnitChanged(Unit unit)
    {
        BindToUnit(unit);
    }

    private void TryBindToSelectedUnit()
    {
        if (PartyManager.Instance != null && PartyManager.Instance.SelectedUnit != null)
        {
            BindToUnit(PartyManager.Instance.SelectedUnit);
            return;
        }

        StartCoroutine(WaitAndBind());
    }

    private void BindToUnit(Unit unit)
    {
        if (unit == null)
            return;

        PlayerStats newStats = unit.GetComponent<PlayerStats>();

        if (newStats == null)
            return;

        stats = newStats;
        lastStamina = stats.currentStamina;

        SyncParticles();
        SyncText();

        Debug.Log($"[StaminaContainerUI] Bound to {unit.name}");
    }
}
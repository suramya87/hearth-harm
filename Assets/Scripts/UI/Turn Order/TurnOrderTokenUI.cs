using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// Basic token binder for turn order UI.
public class TurnOrderTokenUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Optional UI")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image iconImage;

    [Header("Visual State")]
    [SerializeField] private Image background;
    [SerializeField] private Color normalColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color hoverColor = new Color(0.35f, 0.35f, 0.35f, 1f);
    [SerializeField] private Color selectedColor = Color.white;
    [SerializeField] private Color clickFlashColor = Color.yellow;
    [SerializeField] private float clickFlashTime = 0.08f;

    private EnemyUnit boundEnemy;
    private Unit boundPlayer;

    private bool isHovering;
    private Coroutine flashRoutine;

    private void Awake()
    {
        if (background != null)
            normalColor = background.color;
    }

    private void OnEnable()
    {
        if (PartyManager.Instance != null)
            PartyManager.Instance.OnSelectedUnitChanged += HandleSelectedUnitChanged;

        RefreshVisualState();
    }

    private void OnDisable()
    {
        if (PartyManager.Instance != null)
            PartyManager.Instance.OnSelectedUnitChanged -= HandleSelectedUnitChanged;

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }
    }

    public void BindEnemy(EnemyUnit enemy)
    {
        boundEnemy = enemy;
        boundPlayer = null;

        if (nameText != null)
        {
            string displayName = enemy != null && enemy.Stats != null
                ? enemy.Stats.enemyName
                : enemy != null
                    ? enemy.name.Replace("(Clone)", "").Trim()
                    : "Enemy";

            nameText.text = displayName;
        }

        RefreshVisualState();
    }

    public void BindPlayer(Unit player)
    {
        boundPlayer = player;
        boundEnemy = null;

        if (nameText != null)
        {
            if (player != null)
                nameText.text = player.DisplayName;
            else
                nameText.text = "Player";
        }

        RefreshVisualState();
    }

    public EnemyUnit GetBoundEnemy() => boundEnemy;
    public Unit GetBoundPlayer() => boundPlayer;

    public void SetHighlighted(bool value)
    {
        if (background == null)
            return;

        background.color = value ? selectedColor : normalColor;
    }

    private void HandleSelectedUnitChanged(Unit unit)
    {
        RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        if (background == null)
            return;

        if (boundPlayer != null &&
            PartyManager.Instance != null &&
            PartyManager.Instance.SelectedUnit == boundPlayer)
        {
            background.color = selectedColor;
            return;
        }

        background.color = isHovering ? hoverColor : normalColor;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (boundPlayer != null)
        {
            if (TurnSystem.Instance != null && !TurnSystem.Instance.IsPlayerTurn)
                return;

            PartyManager.Instance?.SelectUnit(boundPlayer);
            CameraController2D.Instance?.SoftFocusOn(boundPlayer.transform);

            FlashClick();

            Debug.Log($"[TurnOrderTokenUI] Selected player token: {boundPlayer.DisplayName}");
            return;
        }

        if (boundEnemy == null)
            return;

        if (TurnSystem.Instance != null && !TurnSystem.Instance.IsPlayerTurn)
            return;

        HealthComponent health = boundEnemy.GetComponent<HealthComponent>();
        if (health != null)
            EnemyHealthUI.Instance?.SetTarget(health);

        CameraController2D.Instance?.SoftFocusOn(boundEnemy.transform);
        TilemapHighlighter.Instance?.ShowEnemyMoveRange(boundEnemy);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;

        if (boundPlayer != null)
        {
            RefreshVisualState();
            return;
        }

        if (boundEnemy == null)
            return;

        TilemapHighlighter.Instance?.ShowEnemyMoveRange(boundEnemy);

        HealthComponent health = boundEnemy.GetComponent<HealthComponent>();
        if (health != null)
            EnemyHealthUI.Instance?.SetTarget(health);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;

        if (boundPlayer != null)
        {
            RefreshVisualState();
            return;
        }

        TilemapHighlighter.Instance?.ClearEnemyPreview();

        if (boundEnemy == null)
            return;

        EnemyHealthUI.Instance?.ClearTarget();
    }

    private void FlashClick()
    {
        if (background == null)
            return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        background.color = clickFlashColor;

        yield return new WaitForSecondsRealtime(clickFlashTime);

        flashRoutine = null;
        RefreshVisualState();
    }
}
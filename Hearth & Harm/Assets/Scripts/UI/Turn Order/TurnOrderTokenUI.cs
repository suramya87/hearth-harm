using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// Basic token binder for turn order UI.
public class TurnOrderTokenUI : MonoBehaviour, IPointerClickHandler
{
    [Header("Optional UI")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image iconImage;

    private EnemyUnit boundEnemy;
    private Unit boundPlayer;

    [SerializeField] private Image background;
    private Color normalColor;
    public void SetHighlighted(bool value)
    {
        if (background == null) return;

        background.color = value ? Color.white : normalColor;
    }
    private void Awake()
    {
        if (background != null)
            normalColor = background.color;
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
    }

    public void BindPlayer(Unit player)
    {
        boundPlayer = player;
        boundEnemy = null;

        if (nameText != null)
            nameText.text = player != null ? player.name : "Player";
    }

    public EnemyUnit GetBoundEnemy() => boundEnemy;
    public Unit GetBoundPlayer() => boundPlayer;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (boundEnemy == null) return;

        if (TurnSystem.Instance != null && !TurnSystem.Instance.IsPlayerTurn)
            return;

        HealthComponent health = boundEnemy.GetComponent<HealthComponent>();
        if (health != null)
            EnemyHealthUI.Instance?.SetTarget(health);

        CameraController2D.Instance?.SoftFocusOn(boundEnemy.transform);
    }
}
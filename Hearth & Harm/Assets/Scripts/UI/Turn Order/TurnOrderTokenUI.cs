using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Basic token binder for turn order UI.
/// Draft 1 keeps this simple: bind enemy/player references and optional visuals.
/// </summary>
public class TurnOrderTokenUI : MonoBehaviour
{
    [Header("Optional UI")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Image iconImage;

    private EnemyUnit boundEnemy;
    private Unit boundPlayer;

    public void BindEnemy(EnemyUnit enemy)
    {
        boundEnemy = enemy;
        boundPlayer = null;

        if (nameText != null)
            nameText.text = enemy != null ? enemy.name : "Enemy";
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
}
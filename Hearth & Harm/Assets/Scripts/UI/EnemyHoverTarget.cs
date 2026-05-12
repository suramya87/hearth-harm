using UnityEngine;

[RequireComponent(typeof(Collider))]
public class EnemyHoverTarget : MonoBehaviour
{
    private EnemyUnit enemyUnit;
    private HealthComponent health;

    private void Awake()
    {
        enemyUnit = GetComponent<EnemyUnit>();
        health = GetComponent<HealthComponent>();
    }

    private void OnMouseEnter()
    {
        if (enemyUnit == null || enemyUnit.IsDead)
            return;

        if (health != null)
            EnemyHealthUI.Instance?.SetTarget(health);
    }

    private void OnMouseExit()
    {
        if (enemyUnit == null)
            return;

        EnemyHealthUI.Instance?.ClearTarget();
    }
}
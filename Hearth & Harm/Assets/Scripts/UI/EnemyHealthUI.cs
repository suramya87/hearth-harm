using UnityEngine;

/// <summary>Shows the health of the last-clicked enemy.</summary>
public class EnemyHealthUI : MonoBehaviour
{
    public static EnemyHealthUI Instance { get; private set; }
    [SerializeField] private HealthTargetUI healthUI;
    private void Awake() { Instance = this; }
    public void SetTarget(HealthComponent hc) => healthUI?.SetTarget(hc);
    public void ClearTarget()                 => healthUI?.ClearTarget();
}
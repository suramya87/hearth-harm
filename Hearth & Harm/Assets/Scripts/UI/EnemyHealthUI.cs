using UnityEngine;


public class EnemyHealthUI : MonoBehaviour
{
    public static EnemyHealthUI Instance { get; private set; }

    [SerializeField] private HealthTargetUI healthUI;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (EnemyManager.Instance != null)
        {
            EnemyManager.Instance.OnEnemyTurnStarted += HandleEnemyTurnStarted;
        }

        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnPlayerTurnBegin += HandlePlayerTurnBegin;
        }
    }

    private void OnDestroy()
    {
        if (EnemyManager.Instance != null)
        {
            EnemyManager.Instance.OnEnemyTurnStarted -= HandleEnemyTurnStarted;
        }

        if (TurnSystem.Instance != null)
        {
            TurnSystem.Instance.OnPlayerTurnBegin -= HandlePlayerTurnBegin;
        }
    }

    public void SetTarget(HealthComponent hc)
    {
        healthUI?.SetTarget(hc);
    }

    public void ClearTarget()
    {
        healthUI?.ClearTarget();
    }

    private void HandleEnemyTurnStarted(EnemyUnit enemy)
    {
        if (enemy == null)
        {
            ClearTarget();
            return;
        }

        HealthComponent health = enemy.GetComponent<HealthComponent>();

        if (health != null)
        {
            SetTarget(health);
        }
        else
        {
            ClearTarget();
        }
    }

    private void HandlePlayerTurnBegin()
    {
        ClearTarget();
    }
}
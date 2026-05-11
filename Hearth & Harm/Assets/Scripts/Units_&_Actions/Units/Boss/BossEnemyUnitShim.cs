using System;
using UnityEngine;

[RequireComponent(typeof(BossUnit))]
[RequireComponent(typeof(BossAI))]
public class BossEnemyUnitShim : EnemyAI
{
    private BossAI    bossAI;
    private BossUnit  bossUnit;

    private new void Awake()
    {
        bossAI   = GetComponent<BossAI>();
        bossUnit = GetComponent<BossUnit>();
    }

    private void Start()
    {
        EnemyManager.Instance?.RegisterEnemy(GetComponent<EnemyUnit>());
    }

    public new void TakeTurn(Action onComplete)
    {
        if (bossUnit == null || bossUnit.IsDead) { onComplete?.Invoke(); return; }
        bossAI.TakeTurn(onComplete);
    }
}
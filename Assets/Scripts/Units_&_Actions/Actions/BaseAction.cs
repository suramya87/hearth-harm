using System;
using UnityEngine;

public abstract class BaseAction : MonoBehaviour
{
    protected Unit           unit;
    protected bool           isActive;
    protected Action         onActionComplete;
    protected PlayerStats    playerStats;
    protected UnitAnimator   unitAnimator;
    protected PlayerAnimator playerAnimator;

    protected virtual void Awake()
    {
        unit           = GetComponent<Unit>();
        playerStats    = GetComponent<PlayerStats>();
        unitAnimator   = GetComponent<UnitAnimator>();
        playerAnimator = GetComponent<PlayerAnimator>();

        Debug.Log($"[BaseAction] {gameObject.name} — unitAnimator={unitAnimator}, playerAnimator={playerAnimator}");
    }

    public abstract string GetActionName();
}
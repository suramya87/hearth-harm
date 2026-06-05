using System;
using UnityEngine;

public abstract class BaseAction : MonoBehaviour
{
    protected Unit           unit;
    protected PlayerStats    playerStats;
    protected UnitAnimator   unitAnimator;
    protected PlayerAnimator playerAnimator;
    protected bool           isActive;
    protected Action         onActionComplete;

    protected virtual void Awake()
    {
        unit           = GetComponent<Unit>();
        playerStats    = GetComponentInParent<PlayerStats>();
        unitAnimator   = GetComponent<UnitAnimator>();
        playerAnimator = GetComponent<PlayerAnimator>();

        if (playerStats == null)
            Debug.LogWarning($"[BaseAction] No PlayerStats found in parent hierarchy of '{gameObject.name}'. " +
                             "Stamina checks will fail. Ensure PlayerStats is on this GameObject or a parent.");
    }

    public abstract string GetActionName();

    public virtual void TakeAction(Action onComplete)
    {
        onActionComplete = onComplete;
        isActive = true;
    }

    public Unit GetUnit() => unit;

    public PlayerStats GetPlayerStats() => playerStats;

    protected bool CanExecuteLocally()
    {
        if (!GameManager.IsMultiplayer) return true;
        var netObj = unit != null ? unit.GetComponent<Unity.Netcode.NetworkObject>() : null;
        return netObj != null && netObj.IsOwner;
    }
}
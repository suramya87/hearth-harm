using System;
using UnityEngine;

/// <summary>
/// Base class for all unit actions.
/// Subclasses must call CanExecuteLocally() before doing anything input-driven.
/// </summary>
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
        // FIX: use GetComponentInParent so PlayerStats is found even if it lives
        // on a parent GameObject (e.g. a root unit prefab with action children).
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

    /// <summary>Expose the owning Unit for ownership checks upstream.</summary>
    public Unit GetUnit() => unit;

    /// <summary>Expose PlayerStats so UI can check affordability without a separate lookup.</summary>
    public PlayerStats GetPlayerStats() => playerStats;

    /// <summary>
    /// Returns true when this client is allowed to execute this action.
    /// Always true in singleplayer. In multiplayer, only the owner of this
    /// unit may execute actions.
    /// </summary>
    protected bool CanExecuteLocally()
    {
        if (!GameManager.IsMultiplayer) return true;
        var netObj = unit != null ? unit.GetComponent<Unity.Netcode.NetworkObject>() : null;
        return netObj != null && netObj.IsOwner;
    }
}
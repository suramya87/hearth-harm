using UnityEngine;


public class PlayerAnimator : UnitAnimator
{
    [Header("Player Parameters")]
    [SerializeField] private string paramStaminaEmpty    = "staminaEmpty";
    [SerializeField] private string paramRoomTransition  = "roomTransition";

    private int hashStaminaEmpty, hashRoomTransition, hashClassAbility;

    private PlayerStats playerStats;

    protected override void Awake()
    {
        base.Awake();
        playerStats = GetComponent<PlayerStats>();

        hashStaminaEmpty   = Animator.StringToHash(paramStaminaEmpty);
        hashRoomTransition = Animator.StringToHash(paramRoomTransition);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        RoomManager.OnAnyRoomChanged += OnRoomChanged;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;
    }

    // Call this every time stamina changes (e.g. after MoveAction or CombatAction)
    public void RefreshStaminaState()
    {
        bool empty = playerStats != null && playerStats.currentStamina <= 0;
        anim.SetBool(hashStaminaEmpty, empty);
    }


    private void OnRoomChanged(LevelGenerator.PlacedRoom _) =>
        anim.SetTrigger(hashRoomTransition);
}
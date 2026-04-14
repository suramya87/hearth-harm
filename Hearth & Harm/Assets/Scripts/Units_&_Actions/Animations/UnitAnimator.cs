using UnityEngine;

[RequireComponent(typeof(Animator))]
public class UnitAnimator : MonoBehaviour
{
    [Header("Animator Parameters")]
    [SerializeField] private string paramIsMoving    = "isMoving";
    [SerializeField] private string paramIsIdle      = "isIdle";
    [SerializeField] private string paramFacingNorth = "facingNorth";
    [SerializeField] private string paramFacingSouth = "facingSouth";
    [SerializeField] private string paramFacingEast  = "facingEast";
    [SerializeField] private string paramFacingWest  = "facingWest";
    [SerializeField] private string paramAttack      = "attack";
    [SerializeField] private string paramHurt        = "hurt";
    [SerializeField] private string paramIsDead      = "isDead";

    protected Animator      anim;
    private   HealthComponent health;

    protected int hashIsMoving, hashIsIdle,
                  hashFacingNorth, hashFacingSouth, hashFacingEast, hashFacingWest,
                  hashAttack, hashHurt, hashIsDead;

    protected virtual void Awake()
    {
        anim   = GetComponent<Animator>();
        health = GetComponent<HealthComponent>();

        hashIsMoving    = Animator.StringToHash(paramIsMoving);
        hashIsIdle      = Animator.StringToHash(paramIsIdle);
        hashFacingNorth = Animator.StringToHash(paramFacingNorth);
        hashFacingSouth = Animator.StringToHash(paramFacingSouth);
        hashFacingEast  = Animator.StringToHash(paramFacingEast);
        hashFacingWest  = Animator.StringToHash(paramFacingWest);
        hashAttack      = Animator.StringToHash(paramAttack);
        hashHurt        = Animator.StringToHash(paramHurt);
        hashIsDead      = Animator.StringToHash(paramIsDead);
    }

    protected virtual void OnEnable()
    {
        if (health == null) return;
        health.OnDeath         += OnDeath;
        health.OnHealthChanged += OnHealthChanged;
    }

    protected virtual void OnDisable()
    {
        if (health == null) return;
        health.OnDeath         -= OnDeath;
        health.OnHealthChanged -= OnHealthChanged;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void SetMoving(bool moving)
    {
        anim.SetBool(hashIsMoving, moving);
        anim.SetBool(hashIsIdle, !moving);
    }

    public void SetFacing(Vector2Int dir)
    {
        bool northSouthDominant = Mathf.Abs(dir.y) >= Mathf.Abs(dir.x);

        bool north = northSouthDominant && dir.y > 0;
        bool south = northSouthDominant && dir.y < 0;
        bool east  = !northSouthDominant && dir.x > 0;
        bool west  = !northSouthDominant && dir.x < 0;

        anim.SetBool(hashFacingNorth, north);
        anim.SetBool(hashFacingSouth, south);
        anim.SetBool(hashFacingEast,  east);
        anim.SetBool(hashFacingWest,  west);
    }

    public void TriggerAttack() =>
        anim.SetTrigger(hashAttack);


    private void OnDeath() =>
        anim.SetBool(hashIsDead, true);

    private void OnHealthChanged(int current, int max)
    {
        if (current < max && current > 0)
            anim.SetTrigger(hashHurt);
    }
}
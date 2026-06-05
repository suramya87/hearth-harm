using System.Collections;
using UnityEngine;

public class DiceVisual : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 10f;

    [Header("Fake Roll Physics")]
    [SerializeField] private float chaosSpeedMin = 250f;
    [SerializeField] private float chaosSpeedMax = 600f;
    [SerializeField] private float chaosDrag = 0.9f;
    [SerializeField] private float bounceDamping = 0.75f;
    [SerializeField] private float settleVelocityThreshold = 20f;

    private Vector3 target;
    private bool settled;
    private TMPro.TextMeshPro label;
    private Vector3 targetScale = Vector3.one;

    private Vector3 velocity;
    private Rect chaosBounds;
    private bool chaosMode;

    public bool IsChaosSettled =>
        chaosMode && velocity.magnitude <= settleVelocityThreshold;

    private void Awake()
    {
        label = GetComponentInChildren<TMPro.TextMeshPro>(true);
    }

    public void Initialize(Vector3 spawn, Vector3 settle, int value)
    {
        transform.localPosition = spawn;
        transform.localScale = Vector3.one * 1.5f;
        targetScale = Vector3.one * 1.5f;

        target = settle;
        settled = false;

        if (label)
        {
            label.text = value.ToString();
            label.gameObject.SetActive(false);
        }
    }

    public void BeginChaos(Rect bounds)
    {
        chaosBounds = bounds;
        chaosMode = true;
        settled = false;

        Vector2 dir = UnityEngine.Random.insideUnitCircle.normalized;
        if (dir == Vector2.zero) dir = Vector2.right;

        float speed = UnityEngine.Random.Range(chaosSpeedMin, chaosSpeedMax);
        velocity = new Vector3(dir.x, dir.y, 0f) * speed;

        GetComponent<DiceSpinVisual>()?.Spin();
    }

    public void EndChaos()
    {
        chaosMode = false;
        velocity = Vector3.zero;
        GetComponent<DiceSpinVisual>()?.StopSpin();
    }

    public void UpdateTarget(Vector3 t)
    {
        target = t;
        settled = false;

        if (label)
            label.gameObject.SetActive(false);
    }

    public void Reroll(int value)
    {
        settled = false;

        if (label)
        {
            label.text = value.ToString();
            label.gameObject.SetActive(false);
        }

        GetComponent<DiceSpinVisual>()?.Spin();
    }

    public void SetTargetScale(float scale)
    {
        targetScale = Vector3.one * scale;
    }

    private void Update()
    {
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            targetScale,
            moveSpeed * Time.deltaTime
        );

        if (chaosMode)
        {
            UpdateChaosMovement();
            return;
        }

        if (settled) return;

        transform.localPosition = Vector3.Lerp(
            transform.localPosition,
            target,
            moveSpeed * Time.deltaTime
        );

        if (Vector3.Distance(transform.localPosition, target) < 1f)
        {
            transform.localPosition = target;
            settled = true;
            StartCoroutine(RevealLabelDelayed());
        }
    }

    private void UpdateChaosMovement()
    {
        float dt = Time.deltaTime;

        transform.localPosition += velocity * dt;
        velocity = Vector3.Lerp(velocity, Vector3.zero, chaosDrag * dt);

        Vector3 pos = transform.localPosition;

        if (pos.x < chaosBounds.xMin)
        {
            pos.x = chaosBounds.xMin;
            velocity.x *= -bounceDamping;
        }
        else if (pos.x > chaosBounds.xMax)
        {
            pos.x = chaosBounds.xMax;
            velocity.x *= -bounceDamping;
        }

        if (pos.y < chaosBounds.yMin)
        {
            pos.y = chaosBounds.yMin;
            velocity.y *= -bounceDamping;
        }
        else if (pos.y > chaosBounds.yMax)
        {
            pos.y = chaosBounds.yMax;
            velocity.y *= -bounceDamping;
        }

        transform.localPosition = pos;
    }

    private IEnumerator RevealLabelDelayed()
    {
        yield return new WaitForSeconds(0.1f);

        if (label)
            label.gameObject.SetActive(true);
    }
}
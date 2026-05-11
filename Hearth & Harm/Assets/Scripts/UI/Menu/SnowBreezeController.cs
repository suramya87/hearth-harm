using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class SnowBreezeController : MonoBehaviour
{
    [Header("Breeze")]
    [SerializeField] private float breezeStrength = 0.35f;
    [SerializeField] private float breezeSpeed = 0.25f;
    [SerializeField] private float breezeShiftInterval = 5f;

    [Header("Vertical Fall")]
    [SerializeField] private float fallSpeed = -0.25f;

    private ParticleSystem particles;
    private ParticleSystem.VelocityOverLifetimeModule velocityModule;

    private float currentBreeze;
    private float targetBreeze;
    private float timer;

    private void Awake()
    {
        particles = GetComponent<ParticleSystem>();
        velocityModule = particles.velocityOverLifetime;
        velocityModule.enabled = true;

        PickNewBreeze();
    }

    private void Update()
    {
        timer += Time.deltaTime;

        if (timer >= breezeShiftInterval)
        {
            timer = 0f;
            PickNewBreeze();
        }

        currentBreeze = Mathf.Lerp(
            currentBreeze,
            targetBreeze,
            Time.deltaTime * breezeSpeed
        );

        velocityModule.x = new ParticleSystem.MinMaxCurve(currentBreeze);
        velocityModule.y = new ParticleSystem.MinMaxCurve(fallSpeed);
    }

    private void PickNewBreeze()
    {
        targetBreeze = Random.Range(-breezeStrength, breezeStrength);
    }
}
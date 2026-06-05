using UnityEngine;

public class StaminaParticleUI : MonoBehaviour
{
    RectTransform rect;
    Vector2 velocity;
    Rect bounds;

    float drag = 1.5f;
    float wanderStrength = 10f;

    public void Initialize(Rect containerRect)
    {
        rect = GetComponent<RectTransform>();
        bounds = containerRect;

        velocity = Random.insideUnitCircle.normalized * Random.Range(20f, 40f);
    }

    public void ApplyForce(Vector2 force)
    {
        velocity += force;
    }


    void Update()
    {
        float dt = Time.deltaTime;

        // Gentle wander motion
        velocity += Random.insideUnitCircle * wanderStrength * dt;

        Vector2 pos = rect.anchoredPosition;

        // --- Soft wall pressure ---
        float wallBuffer = 20f;
        float wallForce = 40f;

        if (pos.x - bounds.xMin < wallBuffer)
            velocity.x += wallForce * dt;

        if (bounds.xMax - pos.x < wallBuffer)
            velocity.x -= wallForce * dt;

        if (pos.y - bounds.yMin < wallBuffer)
            velocity.y += wallForce * dt;

        if (bounds.yMax - pos.y < wallBuffer)
            velocity.y -= wallForce * dt;


        // Drag (liquid resistance)
        velocity = Vector2.Lerp(velocity, Vector2.zero, drag * dt);

        pos += velocity * dt;

        if (pos.x < bounds.xMin || pos.x > bounds.xMax)
        {
            velocity.x *= -0.9f;
            pos.x = Mathf.Clamp(pos.x, bounds.xMin, bounds.xMax);
        }

        if (pos.y < bounds.yMin || pos.y > bounds.yMax)
        {
            velocity.y *= -0.9f;
            pos.y = Mathf.Clamp(pos.y, bounds.yMin, bounds.yMax);
        }

        rect.anchoredPosition = pos;
    }
}

using UnityEngine;
public class StaminaParticleUI : MonoBehaviour
{
    private RectTransform rect;
    private Vector2 velocity;
    private Rect bounds;
    private const float Drag = 1.5f, Wander = 10f;

    public void Initialize(Rect b) { rect = GetComponent<RectTransform>(); bounds = b; velocity = Random.insideUnitCircle.normalized * Random.Range(20f,40f); }
    public void ApplyForce(Vector2 f) => velocity += f;

    private void Update()
    {
        float dt = Time.deltaTime;
        velocity += Random.insideUnitCircle * Wander * dt;
        var pos = rect.anchoredPosition;
        float wall = 20f, wf = 40f;
        if (pos.x - bounds.xMin < wall) velocity.x += wf * dt;
        if (bounds.xMax - pos.x < wall) velocity.x -= wf * dt;
        if (pos.y - bounds.yMin < wall) velocity.y += wf * dt;
        if (bounds.yMax - pos.y < wall) velocity.y -= wf * dt;
        velocity = Vector2.Lerp(velocity, Vector2.zero, Drag * dt);
        pos += velocity * dt;
        pos.x = Mathf.Clamp(pos.x, bounds.xMin, bounds.xMax);
        pos.y = Mathf.Clamp(pos.y, bounds.yMin, bounds.yMax);
        if (pos.x <= bounds.xMin || pos.x >= bounds.xMax) velocity.x *= -0.9f;
        if (pos.y <= bounds.yMin || pos.y >= bounds.yMax) velocity.y *= -0.9f;
        rect.anchoredPosition = pos;
    }
}
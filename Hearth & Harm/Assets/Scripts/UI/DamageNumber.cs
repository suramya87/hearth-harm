using TMPro;
using UnityEngine;

/// <summary>
/// Floating damage number that drifts upward and then destroys itself.
/// Works in 2D — uses world-space TMP.
/// </summary>
public class DamageNumber : MonoBehaviour
{
    [Header("References")]
    public TextMeshPro text;

    [Header("Timing")]
    public float lifetime = 0.6f;

    [Header("Movement")]
    public Vector2 floatVelocity = new(0f, 1.2f);

    [Header("Spawn offset from the owner's position")]
    public Vector2 positionOffset = new(0f, 0.3f);

    private void Start()
    {
        Destroy(gameObject, lifetime);
    }

    private void Update()
    {
        transform.position += (Vector3)(floatVelocity * Time.deltaTime);
    }

    public void Initialize(int amount)
    {
        if (text != null) text.text = amount.ToString();
    }

    /// <summary>Convenience spawn helper.</summary>
    public static DamageNumber Spawn(GameObject prefab, Vector3 worldPos, int amount)
    {
        if (prefab == null) return null;
        var dn     = prefab.GetComponent<DamageNumber>();
        var offset = dn != null ? (Vector3)(Vector2)dn.positionOffset : Vector3.zero;
        var go     = Instantiate(prefab, worldPos + offset, Quaternion.identity);
        go.GetComponent<DamageNumber>()?.Initialize(amount);
        return go.GetComponent<DamageNumber>();
    }
}
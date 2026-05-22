using TMPro;
using UnityEngine;

public class StaminaNumber : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TextMeshPro text;

    [Header("Timing")]
    [SerializeField] private float lifetime = 0.75f;

    [Header("Movement")]
    [SerializeField] private Vector2 floatVelocity = new(0f, 1.1f);

    [Header("Spawn Offset")]
    [SerializeField] private Vector2 positionOffset = new(0f, 0.6f);

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
        if (text != null)
            text.text = $"+{amount}";
    }

    public static StaminaNumber Spawn(GameObject prefab, Vector3 worldPos, int amount)
    {
        if (prefab == null)
            return null;

        StaminaNumber template = prefab.GetComponent<StaminaNumber>();

        Vector3 offset = template != null
            ? (Vector3)(Vector2)template.positionOffset
            : Vector3.zero;

        GameObject go = Instantiate(prefab, worldPos + offset, Quaternion.identity);

        StaminaNumber number = go.GetComponent<StaminaNumber>();
        number?.Initialize(amount);

        return number;
    }
}
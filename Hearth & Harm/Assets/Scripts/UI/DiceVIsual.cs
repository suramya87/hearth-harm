using UnityEngine;
public class DiceVisual : MonoBehaviour
{
    public float moveSpeed = 10f;
    private Vector3 target;
    private bool settled;
    private TMPro.TextMeshPro label;

    private void Awake() => label = GetComponentInChildren<TMPro.TextMeshPro>(true);

    public void Initialize(Vector3 spawn, Vector3 settle, int value)
    {
        transform.localPosition = spawn; target = settle; settled = false;
        if (label) { label.text = value.ToString(); label.gameObject.SetActive(false); }
        GetComponent<DiceSpinVisual>()?.Spin();
    }

    public void UpdateTarget(Vector3 t) { target = t; settled = false; if (label) label.gameObject.SetActive(false); }

    public void Reroll(int value)
    {
        settled = false;
        if (label) { label.text = value.ToString(); label.gameObject.SetActive(false); }
        GetComponent<DiceSpinVisual>()?.Spin();
    }

    private void Update()
    {
        if (settled) return;
        transform.localPosition = Vector3.Lerp(transform.localPosition, target, moveSpeed * Time.deltaTime);
        if (Vector3.Distance(transform.localPosition, target) < 1f)
        { transform.localPosition = target; settled = true; if (label) label.gameObject.SetActive(true); }
    }
}
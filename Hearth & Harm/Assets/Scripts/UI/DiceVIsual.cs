using UnityEngine;
using System.Collections;

public class DiceVisual : MonoBehaviour
{
    public float moveSpeed = 10f;
    private Vector3 target;
    private bool settled;
    private TMPro.TextMeshPro label;
    private Vector3 targetScale = Vector3.one;


    private IEnumerator RevealLabelDelayed()
    {
        yield return new WaitForSeconds(0.1f);
        if (label) label.gameObject.SetActive(true);
    }
    private void Awake() => label = GetComponentInChildren<TMPro.TextMeshPro>(true);

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

        GetComponent<DiceSpinVisual>()?.Spin();
    }

    public void UpdateTarget(Vector3 t) { target = t; settled = false; if (label) label.gameObject.SetActive(false); }

    public void Reroll(int value)
    {
        settled = false;
        if (label) { label.text = value.ToString(); label.gameObject.SetActive(false); }
        GetComponent<DiceSpinVisual>()?.Spin();
    }

    public void SetTargetScale(float scale)
    {
        targetScale = Vector3.one * scale;
    }

    private void Update()
    {
        // ALWAYS update scale
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            targetScale,
            moveSpeed * Time.deltaTime
        );

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
}
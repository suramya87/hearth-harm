using System.Collections;
using UnityEngine;
public class DiceSpinVisual : MonoBehaviour
{
    [SerializeField] private float duration = 0.5f;
    [SerializeField] private float speed    = 720f;
    private Coroutine routine;
    public void Spin()
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(SpinRoutine());
    }
    private IEnumerator SpinRoutine()
    {
        float t = 0f; var axis = Random.onUnitSphere;
        while (t < duration) { t += Time.deltaTime; transform.Rotate(axis, speed * Time.deltaTime, Space.Self); yield return null; }
        routine = null;
    }
}
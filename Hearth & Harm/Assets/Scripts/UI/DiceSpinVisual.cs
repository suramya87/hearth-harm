using UnityEngine;

public class DiceSpinVisual : MonoBehaviour
{
    [Header("Spin")]
    [SerializeField] private float spinSpeedMin = 360f;
    [SerializeField] private float spinSpeedMax = 1080f;

    private Vector3 spinAxis;
    private float spinSpeed;
    private bool spinning;

    public void Spin()
    {
        spinAxis = UnityEngine.Random.onUnitSphere;
        spinSpeed = UnityEngine.Random.Range(spinSpeedMin, spinSpeedMax);
        spinning = true;
    }

    public void StopSpin()
    {
        spinning = false;
    }

    private void Update()
    {
        if (!spinning) return;

        transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.Self);
    }
}
using UnityEngine;

public class DicePhysicsDie : MonoBehaviour
{
    [System.Serializable]
    public class Face
    {
        public int value;
        public Transform faceTransform;
    }

    [Header("References")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Face[] faces;

    [Header("Settling")]
    [SerializeField] private float velocityThreshold = 0.05f;
    [SerializeField] private float angularVelocityThreshold = 0.05f;

    public bool IsSettled
    {
        get
        {
            if (rb == null) return true;

            return rb.linearVelocity.magnitude <= velocityThreshold
                && rb.angularVelocity.magnitude <= angularVelocityThreshold;
        }
    }

    private void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();
    }

    public void Roll(Vector3 force, Vector3 torque)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.AddForce(force, ForceMode.Impulse);
        rb.AddTorque(torque, ForceMode.Impulse);
    }

    public int GetTopValue()
    {
        if (faces == null || faces.Length == 0)
        {
            Debug.LogWarning($"{name} has no dice faces assigned.");
            return 1;
        }

        Face bestFace = faces[0];
        float bestDot = -999f;

        foreach (Face face in faces)
        {
            if (face == null || face.faceTransform == null)
                continue;

            float dot = Vector3.Dot(face.faceTransform.up, Vector3.up);

            if (dot > bestDot)
            {
                bestDot = dot;
                bestFace = face;
            }
        }

        return bestFace.value;
    }
}
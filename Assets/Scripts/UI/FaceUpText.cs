using UnityEngine;

public class FaceUpText : MonoBehaviour
{
    void LateUpdate()
    {
        // Upright + 90 degree rotation
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }
}

using UnityEngine;

public class MouseWorld2D : MonoBehaviour
{
    public static MouseWorld2D Instance { get; private set; }

    [Header("Camera (auto-assigned if blank)")]
    [SerializeField] private Camera cam;

    [Header("Z depth for world position (usually 0 for 2D)")]
    [SerializeField] private float worldZ = 0f;

    private void Awake()
    {
        Instance = this;
        if (cam == null) cam = Camera.main;
    }

    public static Vector3 GetPosition()
    {
        if (Instance == null || Instance.cam == null) return Vector3.zero;
        Vector3 screenPos = Input.mousePosition;
        screenPos.z = Instance.cam.nearClipPlane + Mathf.Abs(Instance.worldZ - Instance.cam.transform.position.z);
        return Instance.cam.ScreenToWorldPoint(screenPos);
    }

    public static GridPosition GetGridPosition(RoomGrid room)
    {
        if (room == null) return default;
        return room.GetGridPosition(GetPosition());
    }
}
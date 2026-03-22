using UnityEngine;
/// <summary>Child of room prefab. Defines camera pan bounds. Uses Collider2D.</summary>
public class CameraRoomBounds : MonoBehaviour
{
    public Bounds  GetBounds() => GetComponent<Collider2D>()?.bounds ?? new Bounds(transform.position, Vector3.one*20f);
    public Vector3 GetCenter() => GetBounds().center;
    private void OnDrawGizmos() { Gizmos.color = Color.green; var b=GetBounds(); Gizmos.DrawWireCube(b.center,b.size); }
}
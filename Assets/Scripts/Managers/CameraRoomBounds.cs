using UnityEngine;

/// <summary>
/// Defines the camera pan limits for a room.
///
/// TWO MODES — pick one per room prefab:
///
///   Auto-size  (default)
///     Leave boundsOverride at zero. The bounds are calculated from the
///     Floor tilemap's cell bounds + padding. No collider needed.
///
///   Manual override
///     Set boundsOverride to a non-zero size to use that instead.
///     Still no collider needed.
///
///   Collider (legacy)
///     Attach a BoxCollider2D and set useCollider = true if you prefer
///     the old collider-driven approach.
/// </summary>
public class CameraRoomBounds : MonoBehaviour
{
    [Header("Mode")]
    [Tooltip("Use a BoxCollider2D on this object instead of auto-calculating bounds.")]
    [SerializeField] private bool useCollider = false;

    [Header("Auto-size (useCollider = false)")]
    [Tooltip("Extra world-unit padding added on all sides of the tilemap bounds.")]
    [SerializeField] private float padding = 1f;

    [Tooltip("Leave at zero to auto-calculate from the Floor tilemap. " +
             "Set a non-zero value to override manually.")]
    [SerializeField] private Vector2 boundsOverride = Vector2.zero;

    // ── Public API ─────────────────────────────────────────────────────────

    public Bounds GetBounds()
    {
        if (useCollider)
        {
            var col = GetComponent<Collider2D>();
            if (col != null) return col.bounds;
        }

        // Manual override
        if (boundsOverride.sqrMagnitude > 0.001f)
            return new Bounds(transform.position, boundsOverride);

        // Auto from floor tilemap
        var floor = GetFloorTilemap();
        if (floor != null)
        {
            var tb     = floor.localBounds;
            var center = floor.transform.TransformPoint(tb.center);
            var size   = new Vector3(tb.size.x + padding * 2f,
                                     tb.size.y + padding * 2f, 1f);
            return new Bounds(center, size);
        }

        // Ultimate fallback — shouldn't happen
        Debug.LogWarning($"[CameraRoomBounds] No bounds source found on {gameObject.name}. " +
                          "Add a Floor tilemap or set boundsOverride.");
        return new Bounds(transform.position, new Vector3(20f, 20f, 1f));
    }

    public Vector3 GetCenter() => GetBounds().center;

    // ── Helpers ────────────────────────────────────────────────────────────

    private UnityEngine.Tilemaps.Tilemap GetFloorTilemap()
    {
        // Walk up to the room root, then search children for "Floor"
        Transform root = transform;
        while (root.parent != null) root = root.parent;

        foreach (var tm in root.GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>())
            if (tm.gameObject.name == "Floor") return tm;

        return null;
    }

    // ── Gizmo ──────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        var b = GetBounds();
        Gizmos.DrawWireCube(b.center, b.size);
    }
}
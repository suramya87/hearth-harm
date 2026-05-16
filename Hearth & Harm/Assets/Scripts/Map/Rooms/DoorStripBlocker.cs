using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Attach to every door-strip GameObject in your room prefabs.
///
/// When the strip is ACTIVE   (SetActive(true)  / closed door)  → cells are
/// registered as walls in UnifiedWorldGrid so pathfinding cannot pass through.
///
/// When the strip is INACTIVE (SetActive(false) / open door)    → cells are
/// unregistered so BFS treats the gap as walkable floor.
///
/// Call SetOwnerGrid(roomGrid) from RoomConnector.InitBlockers() after the
/// room's RoomGrid and UnifiedWorldGrid are both fully initialised.
/// </summary>
public class DoorStripBlocker : MonoBehaviour
{
    [Tooltip("Optional override. If set, only these grid cells (in world-space " +
             "integer coordinates) are used instead of auto-detection.")]
    [SerializeField] private Vector2Int[] manualCells;

    private RoomGrid      ownerGrid;
    private bool          registered;

    private List<Vector3> cachedPositions;

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Must be called by RoomConnector.InitBlockers() after the owning RoomGrid
    /// and UnifiedWorldGrid are ready. Safe to call multiple times.
    /// </summary>
    public void SetOwnerGrid(RoomGrid grid)
    {
        if (grid == null) return;
        ownerGrid = grid;

        if (gameObject.activeInHierarchy)
        {
            if (!registered) Register();
        }
        else
        {
            if (registered) Unregister();
        }
    }

    // ── Unity messages ─────────────────────────────────────────────────────

    private void OnEnable()  => Register();   // strip active   = wall/closed
    private void OnDisable() => Unregister(); // strip inactive = open
    private void OnDestroy() => Unregister(); // cleanup on prefab teardown

    // ── Registration ───────────────────────────────────────────────────────

    private void Register()
    {
        if (registered)        return;
        if (ownerGrid == null) return;

        var uwg = UnifiedWorldGrid.Instance;
        if (uwg == null)       return;

        cachedPositions = BuildWorldPositions();
        if (cachedPositions.Count == 0)
        {
            Debug.LogWarning($"[DoorStripBlocker] {gameObject.name} found no cells to block.", this);
            return;
        }

        foreach (var pos in cachedPositions)
            uwg.RegisterWallCell(pos, ownerGrid);

        registered = true;
        Debug.Log($"[DoorStripBlocker] {gameObject.name} registered {cachedPositions.Count} wall cell(s).");
    }

    private void Unregister()
    {
        if (!registered) return;
        registered = false;

        var uwg = UnifiedWorldGrid.Instance;
        if (uwg == null)
        {
            Debug.Log($"[DoorStripBlocker] {gameObject.name} Unregister — UWG already gone, skipping.");
            return;
        }

        if (cachedPositions != null)
        {
            foreach (var pos in cachedPositions)
                uwg.UnregisterWallCell(pos);

            Debug.Log($"[DoorStripBlocker] {gameObject.name} unregistered {cachedPositions.Count} wall cell(s).");
        }
    }

    // ── Cell detection ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns world-space cell centres for every tile/pixel this strip covers.
    /// Priority: manualCells → Tilemap → SpriteRenderer bounds → BoxCollider2D bounds → transform fallback.
    /// </summary>
    private List<Vector3> BuildWorldPositions()
    {
        var positions = new List<Vector3>();

        // 1. Manual override
        if (manualCells != null && manualCells.Length > 0)
        {
            foreach (var c in manualCells)
                positions.Add(new Vector3(c.x + 0.5f, c.y + 0.5f, 0f));
            return positions;
        }

        // 2. Tilemap (most common for door strips)
        var tm = GetComponent<Tilemap>();
        if (tm != null)
        {
            tm.CompressBounds(); // tighten cellBounds before iterating
            foreach (var cell in tm.cellBounds.allPositionsWithin)
            {
                if (!tm.HasTile(cell)) continue;
                positions.Add(tm.GetCellCenterWorld(cell));
            }
            if (positions.Count > 0) return positions;
        }

        // 3. SpriteRenderer bounds
        var sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            var b = sr.bounds;
            for (float x = Mathf.Floor(b.min.x) + 0.5f; x < b.max.x; x += 1f)
            for (float y = Mathf.Floor(b.min.y) + 0.5f; y < b.max.y; y += 1f)
                positions.Add(new Vector3(x, y, 0f));
            if (positions.Count > 0) return positions;
        }

        // 4. BoxCollider2D bounds
        var col = GetComponent<BoxCollider2D>();
        if (col != null)
        {
            var b = col.bounds;
            for (float x = Mathf.Floor(b.min.x) + 0.5f; x < b.max.x; x += 1f)
            for (float y = Mathf.Floor(b.min.y) + 0.5f; y < b.max.y; y += 1f)
                positions.Add(new Vector3(x, y, 0f));
            if (positions.Count > 0) return positions;
        }

        // 5. Transform position fallback
        Debug.LogWarning($"[DoorStripBlocker] {gameObject.name}: no Tilemap, SpriteRenderer, " +
                         "or BoxCollider2D found — falling back to single transform cell.", this);
        positions.Add(new Vector3(
            Mathf.Floor(transform.position.x) + 0.5f,
            Mathf.Floor(transform.position.y) + 0.5f,
            0f));
        return positions;
    }

    // ── Editor gizmos ──────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = gameObject.activeInHierarchy
            ? new Color(1f, 0.2f, 0.2f, 0.6f)   // red  = wall/blocking
            : new Color(0.2f, 1f, 0.2f, 0.6f);  // green = open/passable

        foreach (var pos in BuildWorldPositions())
            Gizmos.DrawCube(pos, new Vector3(0.9f, 0.9f, 0.1f));
    }
#endif
}
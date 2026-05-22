using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Attach this component to every door strip GameObject in your room prefabs.
/// </summary>
public class DoorStripBlocker : MonoBehaviour
{
    [Tooltip("If left empty the component auto-detects cells from Tilemap, " +
             "SpriteRenderer, or BoxCollider2D on this GameObject.")]
    [SerializeField] private Vector2Int[] manualCells; 

    // private RoomGrid ownerGrid;

    private bool registered;

    // ── Public API ─────────────────────────────────────────────────────────

    private RoomGrid ownerGrid;
    private Tilemap  ownerFloorTilemap;   

    public void SetOwnerGrid(RoomGrid grid)
    {
        ownerGrid        = grid;
        ownerFloorTilemap = grid?.GetFloorTilemap();   
        if (gameObject.activeInHierarchy) Register();
        else Unregister();
    }

    // ── Unity messages ─────────────────────────────────────────────────────

    private void OnEnable()
    {
        Register();
    }

    private void OnDisable()
    {
        Unregister();
    }

    private void OnDestroy()
    {
        Unregister();
    }

    // ── Registration ───────────────────────────────────────────────────────

    private void Register()
    {
        var uwg = UnifiedWorldGrid.Instance;
        if (uwg == null || ownerGrid == null) return;
        if (registered) return;

        foreach (var worldPos in GetBlockedWorldPositions())
            uwg.RegisterWallCell(worldPos, ownerGrid);

        registered = true;
    }

    private void Unregister()
    {
        var uwg = UnifiedWorldGrid.Instance;
        if (uwg == null || !registered) return;

        foreach (var worldPos in GetBlockedWorldPositions())
            uwg.UnregisterWallCell(worldPos);

        registered = false;

        foreach (var ma in Object.FindObjectsByType<MoveAction>(FindObjectsSortMode.None))
            ma.InvalidateCache();
    }

    // ── Cell detection ─────────────────────────────────────────────────────

    private List<Vector3> GetBlockedWorldPositions()
    {
        var positions = new List<Vector3>();

        if (manualCells != null && manualCells.Length > 0)
        {
            foreach (var c in manualCells)
                positions.Add(new Vector3(c.x + 0.5f, c.y + 0.5f, 0f));
            return positions;
        }

        var tm = GetComponent<Tilemap>();
        if (tm != null)
        {
            foreach (var cell in tm.cellBounds.allPositionsWithin)
            {
                if (!tm.HasTile(cell)) continue;
                positions.Add(tm.GetCellCenterWorld(cell));  
            }
            return positions;
        }

        var sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            var b = sr.bounds;
            if (ownerFloorTilemap != null)
            {
                for (float x = b.min.x; x < b.max.x; x += 1f)
                for (float y = b.min.y; y < b.max.y; y += 1f)
                {
                    var cell   = ownerFloorTilemap.WorldToCell(new Vector3(x, y, 0f));
                    positions.Add(ownerFloorTilemap.GetCellCenterWorld(cell));
                }
            }
            else
            {
                for (float x = b.min.x + 0.5f; x < b.max.x; x += 1f)
                for (float y = b.min.y + 0.5f; y < b.max.y; y += 1f)
                    positions.Add(new Vector3(x, y, 0f));
            }
            return positions;
        }

        var col = GetComponent<BoxCollider2D>();
        if (col != null)
        {
            var b = col.bounds;
            if (ownerFloorTilemap != null)
            {
                for (float x = b.min.x; x < b.max.x; x += 1f)
                for (float y = b.min.y; y < b.max.y; y += 1f)
                {
                    var cell   = ownerFloorTilemap.WorldToCell(new Vector3(x, y, 0f));
                    positions.Add(ownerFloorTilemap.GetCellCenterWorld(cell));
                }
            }
            else
            {
                for (float x = b.min.x + 0.5f; x < b.max.x; x += 1f)
                for (float y = b.min.y + 0.5f; y < b.max.y; y += 1f)
                    positions.Add(new Vector3(x, y, 0f));
            }
            return positions;
        }

        if (ownerFloorTilemap != null)
        {
            var cell = ownerFloorTilemap.WorldToCell(transform.position);
            positions.Add(ownerFloorTilemap.GetCellCenterWorld(cell));
        }
        else
        {
            positions.Add(new Vector3(
                Mathf.Floor(transform.position.x) + 0.5f,
                Mathf.Floor(transform.position.y) + 0.5f, 0f));
        }
        return positions;
    }
}
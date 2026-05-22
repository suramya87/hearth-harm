using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Scans the SpawnPoints tilemap on a room and returns ALL spawn tiles
/// per direction — not just one.  Used by the hallway PCG system to
/// measure mouth width and find the centre of each door opening.
///
/// </summary>
public class HallwaySpawnPointScanner : MonoBehaviour
{
    // All cell positions (in tilemap-local coords) keyed by direction.
    private readonly Dictionary<LevelGenerator.Direction, List<Vector3Int>> tilesByDirection = new();
    private bool scanned;

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Scan the SpawnPoints tilemap (once) and cache results.
    /// Call this before any Get* method.
    /// </summary>
    public void Scan()
    {
        if (scanned) return;
        scanned = true;
        tilesByDirection.Clear();

        Tilemap spawnTilemap = null;
        foreach (Tilemap tm in GetComponentsInChildren<Tilemap>(includeInactive: true))
        {
            if (tm.gameObject.name == "SpawnPoints") { spawnTilemap = tm; break; }
        }

        if (spawnTilemap == null)
        {
            Debug.LogWarning($"[HallwaySpawnPointScanner] No SpawnPoints tilemap on {gameObject.name}");
            return;
        }

        foreach (Vector3Int pos in spawnTilemap.cellBounds.allPositionsWithin)
        {
            if (spawnTilemap.GetTile(pos) is not SpawnPointTile st) continue;

            if (!tilesByDirection.ContainsKey(st.entryDirection))
                tilesByDirection[st.entryDirection] = new List<Vector3Int>();

            tilesByDirection[st.entryDirection].Add(pos);
        }

        foreach (var dir in tilesByDirection.Keys.ToList())
        {
            bool horizontal = dir == LevelGenerator.Direction.East
                           || dir == LevelGenerator.Direction.West;

            tilesByDirection[dir].Sort((a, b) =>
                horizontal ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        }
    }

    /// <summary>How many spawn tiles exist for this direction (= door width in tiles).</summary>
    public int GetMouthWidth(LevelGenerator.Direction dir)
    {
        Scan();
        return tilesByDirection.TryGetValue(dir, out var list) ? list.Count : 0;
    }

    /// <summary>
    /// All tilemap-local cell positions for this direction.
    /// Returns empty list if direction has no spawn tiles.
    /// </summary>
    public IReadOnlyList<Vector3Int> GetTiles(LevelGenerator.Direction dir)
    {
        Scan();
        return tilesByDirection.TryGetValue(dir, out var list)
            ? list
            : System.Array.Empty<Vector3Int>();
    }

    /// <summary>
    /// World-space centre of the door mouth for this direction.
    /// Averages all tile centres.
    /// </summary>
    public Vector3 GetMouthCentreWorld(LevelGenerator.Direction dir)
    {
        Scan();
        var tiles = GetTiles(dir);
        if (tiles.Count == 0) return transform.position;

        Tilemap spawnTilemap = null;
        foreach (Tilemap tm in GetComponentsInChildren<Tilemap>(includeInactive: true))
            if (tm.gameObject.name == "SpawnPoints") { spawnTilemap = tm; break; }

        if (spawnTilemap == null) return transform.position;

        Vector3 sum = Vector3.zero;
        foreach (var t in tiles) sum += spawnTilemap.GetCellCenterWorld(t);
        return sum / tiles.Count;
    }

    /// <summary>
    /// True if any spawn tiles were found for this direction.
    /// </summary>
    public bool HasDoor(LevelGenerator.Direction dir)
    {
        Scan();
        return tilesByDirection.ContainsKey(dir) && tilesByDirection[dir].Count > 0;
    }
}
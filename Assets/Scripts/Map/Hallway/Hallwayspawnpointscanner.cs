using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

public class HallwaySpawnPointScanner : MonoBehaviour
{
    private readonly Dictionary<LevelGenerator.Direction, List<Vector3Int>> tilesByDirection = new();
    private bool scanned;

    // ── Public API ─────────────────────────────────────────────────────────

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

    public int GetMouthWidth(LevelGenerator.Direction dir)
    {
        Scan();
        return tilesByDirection.TryGetValue(dir, out var list) ? list.Count : 0;
    }

    public IReadOnlyList<Vector3Int> GetTiles(LevelGenerator.Direction dir)
    {
        Scan();
        return tilesByDirection.TryGetValue(dir, out var list)
            ? list
            : System.Array.Empty<Vector3Int>();
    }

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

    public bool HasDoor(LevelGenerator.Direction dir)
    {
        Scan();
        return tilesByDirection.ContainsKey(dir) && tilesByDirection[dir].Count > 0;
    }
}
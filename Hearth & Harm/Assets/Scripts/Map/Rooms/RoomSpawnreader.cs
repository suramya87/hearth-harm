using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Reads SpawnPointTile assets from the "SpawnPoints" child tilemap.
/// Used by RoomNavigationUI to find the correct entry tile when the player
/// moves into a room from a specific direction.
/// </summary>
public class RoomSpawnPointReader : MonoBehaviour
{
    private Tilemap spawnTilemap;
    private readonly Dictionary<LevelGenerator.Direction, GridPosition> spawnPositions = new();
    private bool scanned;

    public void Initialize()
    {
        foreach (Tilemap tm in GetComponentsInChildren<Tilemap>())
            if (tm.gameObject.name == "SpawnPoints") { spawnTilemap = tm; break; }
    }

    private void EnsureScanned()
    {
        if (scanned) return;
        scanned = true;
        if (spawnTilemap == null) { Initialize(); if (spawnTilemap == null) return; }

        var r = spawnTilemap.GetComponent<TilemapRenderer>();
        if (r) r.enabled = false;

        spawnPositions.Clear();
        foreach (Vector3Int pos in spawnTilemap.cellBounds.allPositionsWithin)
        {
            if (spawnTilemap.GetTile(pos) is SpawnPointTile st)
                spawnPositions[st.entryDirection] = new GridPosition(pos.x, pos.y);
        }
    }

    public GridPosition GetSpawnPosition(LevelGenerator.Direction dir, RoomGrid room)
    {
        EnsureScanned();
        if (spawnPositions.TryGetValue(dir, out var pos)) return pos;
        return new GridPosition(room.GetWidth() / 2, room.GetHeight() / 2);
    }

    public bool HasSpawnPoint(LevelGenerator.Direction dir) { EnsureScanned(); return spawnPositions.ContainsKey(dir); }
    public IReadOnlyDictionary<LevelGenerator.Direction, GridPosition> GetAll() { EnsureScanned(); return spawnPositions; }
}
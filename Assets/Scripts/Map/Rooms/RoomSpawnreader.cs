using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class RoomSpawnPointReader : MonoBehaviour
{
    [SerializeField] private Tilemap spawnTilemap;
    private readonly Dictionary<LevelGenerator.Direction, GridPosition> spawnPositions = new();
    private bool scanned;

    public void Initialize()
    {
        if (spawnTilemap != null) return;
        foreach (Tilemap tm in GetComponentsInChildren<Tilemap>())
            if (tm.gameObject.name == "SpawnPoints") { spawnTilemap = tm; break; }
        if (spawnTilemap == null)
            Debug.LogWarning($"[RoomSpawnPointReader] No SpawnPoints tilemap found in {gameObject.name}");
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
            TileBase tile = spawnTilemap.GetTile(pos);
            if (tile == null) continue;

            Debug.Log($"[RoomSpawnPointReader] Tile '{tile.name}' ({tile.GetType().Name}) at {pos}");

            if (tile is SpawnPointTile st)
            {
                spawnPositions[st.entryDirection] = new GridPosition(pos.x, pos.y);
                Debug.Log($"[RoomSpawnPointReader] ✓ {st.entryDirection} → cell {pos} → GridPosition ({pos.x},{pos.y})");
            }
        }

        Debug.Log($"[RoomSpawnPointReader] {gameObject.name} — {spawnPositions.Count} spawn points found.");
    }

    public GridPosition GetSpawnPosition(LevelGenerator.Direction dir, RoomGrid room)
    {
        EnsureScanned();
        if (spawnPositions.TryGetValue(dir, out var pos)) return pos;
        Debug.LogWarning($"[RoomSpawnPointReader] No spawn for {dir} in {gameObject.name}, using center.");
        return new GridPosition(room.GetWidth() / 2, room.GetHeight() / 2);
    }

    public bool HasSpawnPoint(LevelGenerator.Direction dir)
    {
        EnsureScanned();
        return spawnPositions.ContainsKey(dir);
    }

    public IReadOnlyDictionary<LevelGenerator.Direction, GridPosition> GetAll()
    {
        EnsureScanned();
        return spawnPositions;
    }
}
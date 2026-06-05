using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;


public class NetworkedEnemySpawner : NetworkBehaviour
{
    [Header("Table-Based Spawning")]
    [SerializeField] private List<EnemySpawnTable> spawnTables = new();

    [Header("Legacy Fallback")]
    [SerializeField] private List<LegacyEntry> legacyEntries = new();

    [Header("Settings")]
    [SerializeField, Min(1)] private int borderPadding = 2;

    [System.Serializable]
    public class LegacyEntry
    {
        public GameObject prefab;
        [Min(1)] public int count = 1;
        public LevelGenerator.RoomType roomType = LevelGenerator.RoomType.Normal;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return; // Only server spawns enemies

        LevelSyncBridge.OnNetworkLevelReady += OnLevelReady;
    }

    public override void OnNetworkDespawn()
    {
        LevelSyncBridge.OnNetworkLevelReady -= OnLevelReady;
    }

    private void OnLevelReady()
    {
        if (!IsServer) return;
        SpawnAll();
    }

    // ── Spawn logic (server only) ──────────────────────────────────────────

    public void SpawnAll()
    {
        if (!IsServer) return;

        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) { Debug.LogError("[NetworkedEnemySpawner] No LevelGenerator found."); return; }

        int level  = WaveManager.Instance?.CurrentLevel ?? 1;
        int budget = WaveManager.Instance?.GetTotalEnemyBudget() ?? 10;

        if (spawnTables != null && spawnTables.Count > 0)
        {
            foreach (var table in spawnTables)
            {
                if (table == null || !table.IsActiveForLevel(level)) continue;

                var rooms = gen.GetAllRooms().FindAll(r =>
                    r.prefabData.roomType == table.roomType &&
                    r.roomGrid != null && r.roomGrid.IsInitialized());

                foreach (var (prefab, count) in table.CalculateSpawns(budget))
                for (int i = 0; i < count; i++)
                {
                    var room = rooms[Random.Range(0, rooms.Count)];
                    var pos  = RandomWalkableTile(room.roomGrid);
                    if (pos != null) SpawnNetworkedEnemy(prefab, room.roomGrid, pos.Value);
                }
            }
        }
        else
        {
            foreach (var entry in legacyEntries)
            {
                if (entry.prefab == null) continue;
                var rooms = gen.GetAllRooms().FindAll(r =>
                    r.prefabData.roomType == entry.roomType &&
                    r.roomGrid != null && r.roomGrid.IsInitialized());
                if (rooms.Count == 0) continue;

                for (int i = 0; i < entry.count; i++)
                {
                    var room = rooms[Random.Range(0, rooms.Count)];
                    var pos  = RandomWalkableTile(room.roomGrid);
                    if (pos != null) SpawnNetworkedEnemy(entry.prefab, room.roomGrid, pos.Value);
                }
            }
        }
    }

    // ── Core spawn ─────────────────────────────────────────────────────────


    public EnemyUnit SpawnNetworkedEnemy(GameObject prefab, RoomGrid room, GridPosition pos)
    {
        if (!IsServer) return null;
        if (prefab == null || room == null) return null;
        if (!room.IsWalkableIgnoreOccupancy(pos)) return null;

        Vector3 worldPos = room.GetWorldPosition(pos);
        if (!room.IsPositionInRoom(worldPos)) return null;

        var go = Instantiate(prefab, worldPos, Quaternion.identity);

        var netObj = go.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError($"[NetworkedEnemySpawner] {prefab.name} is missing NetworkObject component!");
            Destroy(go);
            return null;
        }

        netObj.Spawn(destroyWithScene: true);

        var eu = go.GetComponent<EnemyUnit>();
        if (eu == null)
        {
            Debug.LogError($"[NetworkedEnemySpawner] {prefab.name} is missing EnemyUnit component!");
            netObj.Despawn();
            return null;
        }

        eu.PlaceOnGrid(room, pos);
        EnemyManager.Instance?.RegisterEnemy(eu);

        Debug.Log($"[NetworkedEnemySpawner] Spawned {prefab.name} at {pos} in {room.gameObject.name}");
        return eu;
    }

    // ── Tile selection ─────────────────────────────────────────────────────

    private GridPosition? RandomWalkableTile(RoomGrid room)
    {
        var floor = room.GetFloorTilemap();
        if (floor == null) return null;

        var b = floor.cellBounds;
        var candidates = new List<GridPosition>();

        for (int x = b.xMin + borderPadding; x < b.xMax - borderPadding; x++)
        for (int y = b.yMin + borderPadding; y < b.yMax - borderPadding; y++)
        {
            var p = new GridPosition(x, y);
            if (!room.IsWalkableIgnoreOccupancy(p)) continue;
            if (!room.IsPositionInRoom(room.GetWorldPosition(p))) continue;
            candidates.Add(p);
        }

        return candidates.Count == 0 ? null : candidates[Random.Range(0, candidates.Count)];
    }
}
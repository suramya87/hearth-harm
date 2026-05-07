using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class EnemySpawner : MonoBehaviour
{
    [Header("Table-Based Spawning (recommended)")]
    [SerializeField] private List<EnemySpawnTable> spawnTables = new();

    [Header("Legacy Fallback")]
    [SerializeField] private List<LegacyEntry> legacyEntries = new();

    [Header("Settings")]
    [SerializeField, Min(1)] private int borderPadding = 2;
    [SerializeField] private bool spawnOnLevelReady = true;

    [System.Serializable]
    public class LegacyEntry
    {
        public GameObject prefab;
        [Min(1)] public int count = 1;
        public LevelGenerator.RoomType roomType = LevelGenerator.RoomType.Normal;
    }

    private void OnEnable()  => LevelGenerator.OnLevelReady += OnLevelReady;
    private void OnDisable() => LevelGenerator.OnLevelReady -= OnLevelReady;

    private void OnLevelReady()
    {
        // With the new hallway walk-in system, enemies spawn on room entry
        // rather than all at once. Set spawnOnLevelReady = false in the Inspector
        // to opt into the new behaviour. Legacy projects can keep it true.
        if (spawnOnLevelReady) SpawnAll();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Spawn all enemies across all rooms at once (legacy / opt-in).</summary>
    public void SpawnAll()
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) { Debug.LogError("[EnemySpawner] No LevelGenerator."); return; }

        if (spawnTables != null && spawnTables.Count > 0)
            SpawnFromTables(gen);
        else
            SpawnLegacy(gen);
    }

    /// <summary>
    /// Spawn enemies for ONE specific room — called by HallwayEntryTrigger
    /// when the player first walks into that room.
    ///
    /// Uses the same spawn-table / legacy logic as SpawnAll() but scoped
    /// to the single PlacedRoom that was just entered.
    /// </summary>
    public void SpawnForRoom(LevelGenerator.PlacedRoom room)
    {
        if (room == null || room.roomGrid == null || !room.roomGrid.IsInitialized())
        {
            Debug.LogWarning("[EnemySpawner] SpawnForRoom: invalid room.");
            return;
        }

        int level  = WaveManager.Instance?.CurrentLevel ?? 1;
        int budget = WaveManager.Instance?.GetTotalEnemyBudget() ?? 10;

        bool anySpawned = false;

        if (spawnTables != null && spawnTables.Count > 0)
        {
            foreach (var table in spawnTables)
            {
                if (table == null) continue;
                if (!table.IsActiveForLevel(level)) continue;
                if (table.roomType != room.prefabData.roomType) continue;

                // Scale the budget down proportionally by room count so the
                // total across all rooms roughly matches the overall budget.
                var gen = FindAnyObjectByType<LevelGenerator>();
                int roomsOfType = gen != null
                    ? gen.GetAllRooms().FindAll(r => r.prefabData.roomType == table.roomType).Count
                    : 1;
                int roomBudget = Mathf.Max(1, budget / Mathf.Max(1, roomsOfType));

                foreach (var (prefab, count) in table.CalculateSpawns(roomBudget))
                for (int i = 0; i < count; i++)
                {
                    var pos = RandomWalkableTile(room.roomGrid);
                    if (pos != null)
                    {
                        SpawnEnemy(prefab, room.roomGrid, pos.Value);
                        anySpawned = true;
                    }
                }
            }
        }
        else
        {
            // Legacy fallback
            foreach (var entry in legacyEntries)
            {
                if (entry.prefab == null) continue;
                if (entry.roomType != room.prefabData.roomType) continue;

                for (int i = 0; i < entry.count; i++)
                {
                    var pos = RandomWalkableTile(room.roomGrid);
                    if (pos != null)
                    {
                        SpawnEnemy(entry.prefab, room.roomGrid, pos.Value);
                        anySpawned = true;
                    }
                }
            }
        }

        if (!anySpawned)
            Debug.Log($"[EnemySpawner] No enemies spawned for {room.roomInstance.name} " +
                      $"(type: {room.prefabData.roomType}) — no matching table/entry.");
    }

    // ── Table spawning (scene-wide) ────────────────────────────────────────

    private void SpawnFromTables(LevelGenerator gen)
    {
        int level  = WaveManager.Instance?.CurrentLevel ?? 1;
        int budget = WaveManager.Instance?.GetTotalEnemyBudget() ?? 10;

        foreach (var table in spawnTables)
        {
            if (table == null || !table.IsActiveForLevel(level)) continue;

            var rooms = gen.GetAllRooms().FindAll(r =>
                r.prefabData.roomType == table.roomType &&
                r.roomGrid != null && r.roomGrid.IsInitialized());
            if (rooms.Count == 0) continue;

            foreach (var (prefab, count) in table.CalculateSpawns(budget))
            for (int i = 0; i < count; i++)
            {
                var room = rooms[Random.Range(0, rooms.Count)];
                var pos  = RandomWalkableTile(room.roomGrid);
                if (pos != null) SpawnEnemy(prefab, room.roomGrid, pos.Value);
            }
        }
    }

    private void SpawnLegacy(LevelGenerator gen)
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
                if (pos != null) SpawnEnemy(entry.prefab, room.roomGrid, pos.Value);
            }
        }
    }

    // ── Core spawn ─────────────────────────────────────────────────────────

    public EnemyUnit SpawnEnemy(GameObject prefab, RoomGrid room, GridPosition pos)
    {
        if (prefab == null || room == null) return null;
        if (!room.IsWalkableIgnoreOccupancy(pos))
        { Debug.LogWarning($"[EnemySpawner] {pos} not walkable."); return null; }

        var worldPos = room.GetWorldPosition(pos);
        if (!room.IsPositionInRoom(worldPos))
        { Debug.LogWarning($"[EnemySpawner] {worldPos} outside room bounds."); return null; }

        var go = Instantiate(prefab, worldPos, Quaternion.identity);
        var eu = go.GetComponent<EnemyUnit>();
        if (eu == null) { Debug.LogError($"[EnemySpawner] {prefab.name} missing EnemyUnit."); Destroy(go); return null; }

        eu.PlaceOnGrid(room, pos);
        EnemyManager.Instance?.RegisterEnemy(eu);
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

        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }
}
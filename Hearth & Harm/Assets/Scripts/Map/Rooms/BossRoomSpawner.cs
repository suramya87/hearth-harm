using UnityEngine;

public class BossRoomSpawner : MonoBehaviour
{
    [Header("Boss")]
    [SerializeField] private GameObject bossPrefab;
    [SerializeField] private int        footprintSize = 2;
    [SerializeField] private bool       showDebugLogs = true;

    // Keep a reference so we can register it when the player actually enters
    private BossUnit pendingBoss;
    private bool     bossSpawned = false;

    private void OnEnable()
    {
        LevelGenerator.OnLevelReady  += OnLevelReady;
        RoomManager.OnAnyRoomChanged += OnRoomChanged;
    }

    private void OnDisable()
    {
        LevelGenerator.OnLevelReady  -= OnLevelReady;
        RoomManager.OnAnyRoomChanged -= OnRoomChanged;
    }

    private void OnLevelReady()
    {
        bossSpawned = false;
        pendingBoss = null;

        // Pre-spawn the boss so it exists, but do NOT register with EnemyManager yet
        var gen      = FindAnyObjectByType<LevelGenerator>();
        var bossRoom = gen?.GetBossRoom();
        if (bossRoom == null) return;

        var room = bossRoom.roomGrid;
        if (room == null || !room.IsInitialized()) return;
        if (room.HasBeenCleared) return;

        GridPosition? origin = FindBossSpawnOrigin(room);
        if (origin == null)
        {
            Debug.LogError("[BossRoomSpawner] No valid 2x2 spawn position!");
            return;
        }

        pendingBoss = SpawnBossUnregistered(room, origin.Value);
    }

    private void OnRoomChanged(LevelGenerator.PlacedRoom entered)
    {
        if (entered == null) return;
        if (entered.prefabData.roomType != LevelGenerator.RoomType.Boss) return;
        if (bossSpawned) return;
        if (pendingBoss == null) return;

        // NOW register — EnemyManager will include it in the turn queue
        var eu = pendingBoss.GetComponent<EnemyUnit>();
        if (eu != null)
        {
            EnemyManager.Instance?.RegisterEnemy(eu);
            bossSpawned = true;
            if (showDebugLogs)
                Debug.Log($"[BossRoomSpawner] Boss registered on room entry.");
        }
    }

    private BossUnit SpawnBossUnregistered(RoomGrid room, GridPosition origin)
    {
        var originWorld = room.GetWorldPosition(origin);
        var spawnWorld  = new Vector3(
            originWorld.x + (footprintSize - 1) * 0.5f,
            originWorld.y + (footprintSize - 1) * 0.5f,
            0f
        );

        var go = Instantiate(bossPrefab, spawnWorld, Quaternion.identity);
        go.name = "Boss";

        var bossUnit = go.GetComponent<BossUnit>();
        if (bossUnit == null)
        {
            Debug.LogError("[BossRoomSpawner] Boss prefab missing BossUnit!");
            Destroy(go);
            return null;
        }

        bossUnit.PlaceOnGrid(room, origin);

        if (showDebugLogs)
            Debug.Log($"[BossRoomSpawner] Boss pre-spawned at {origin}, " +
                      $"waiting for room entry to register.");
        return bossUnit;
    }

    private GridPosition? FindBossSpawnOrigin(RoomGrid room)
    {
        var floor = room.GetFloorTilemap();
        if (floor == null) return null;

        var bounds  = floor.cellBounds;
        int centerX = (bounds.xMin + bounds.xMax) / 2 - footprintSize / 2;
        int centerY = (bounds.yMin + bounds.yMax) / 2 - footprintSize / 2;

        int maxRadius = Mathf.Max(bounds.size.x, bounds.size.y);
        for (int r = 0; r <= maxRadius; r++)
        for (int ox = -r; ox <= r; ox++)
        for (int oy = -r; oy <= r; oy++)
        {
            if (r > 0 && Mathf.Abs(ox) != r && Mathf.Abs(oy) != r) continue;
            var origin = new GridPosition(centerX + ox, centerY + oy);
            if (IsValidBossOrigin(origin, room)) return origin;
        }
        return null;
    }

    private bool IsValidBossOrigin(GridPosition origin, RoomGrid room)
    {
        for (int dx = 0; dx < footprintSize; dx++)
        for (int dy = 0; dy < footprintSize; dy++)
        {
            var cell = new GridPosition(origin.x + dx, origin.y + dy);
            if (!room.IsValidGridPosition(cell))       return false;
            if (!room.IsWalkableIgnoreOccupancy(cell)) return false;
        }
        return true;
    }
}
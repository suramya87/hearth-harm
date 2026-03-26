using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Random = UnityEngine.Random;

/// <summary>
/// Procedurally generates a dungeon made of room prefabs connected by
/// tilemap-painted hallways.
///
/// CHANGES FROM 3D VERSION
///   - All room placement is on the 2D XY plane.
///   - Hallways are painted directly onto a shared "HallwayTilemap" using
///     a configurable floor tile — no separate hallway prefabs required
///     (though you can still use them if you prefer).
///   - LevelGrid has been removed; use RoomManager.GetCurrentRoom() or
///     RoomManager.GetCurrentRoomGrid() wherever you previously used LevelGrid.
///   - WaveManager-driven room/enemy budget scaling is preserved.
/// </summary>
public class LevelGenerator : MonoBehaviour
{
    // ── Types ──────────────────────────────────────────────────────────────

    [Serializable]
    public class RoomPrefabData
    {
        public GameObject prefab;
        public RoomType   roomType;
        [Range(0f,1f)] public float spawnWeight = 1f;

        [HideInInspector] public int    width      = 20;
        [HideInInspector] public int    height     = 20;
        [HideInInspector] public float  cellSize   = 1f;
    }

    public enum RoomType  { Start, End, Normal, Special, Boss }
    public enum Direction { North, South, East, West }

    public class PlacedRoom
    {
        public GameObject     roomInstance;
        public RoomPrefabData prefabData;
        public RoomConnector  connector;
        public Vector3        worldPosition;
        public Vector2Int     gridPosition;    // layout grid (not tilemap)
        public RoomGrid       roomGrid;
    }

    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Room Prefabs")]
    [SerializeField] private List<RoomPrefabData> roomPrefabs;

    [Header("Hallways (tilemap-based)")]
    [Tooltip("Parent grid / tilemap used for hallway painting. " +
             "Create a separate Grid + Tilemap in your scene named 'HallwayTilemap'.")]
    [SerializeField] private Tilemap hallwayTilemap;
    [Tooltip("Floor tile painted for hallway cells.")]
    [SerializeField] private TileBase hallwayFloorTile;
    [Tooltip("Width of the hallway corridor in tiles.")]
    [SerializeField, Min(1)] private int hallwayWidth = 3;

    [Header("Generation")]
    [SerializeField] private int   minRooms          = 5;
    [SerializeField] private int   maxRooms          = 10;
    [SerializeField] private float specialRoomChance = 0.3f;
    [SerializeField] private bool  spawnBossRoom     = true;

    [Header("Layout spacing (world units between room centres)")]
    [Tooltip("How far apart room centres are placed. Set this larger than your biggest room.")]
    [SerializeField] private float roomSpacing = 25f;

    [Header("Player Prefabs")]
    [Tooltip("Index matches character selection (0=Knight, 1=Rogue …)")]
    [SerializeField] private List<GameObject> playerPrefabs;
    [SerializeField] private bool spawnPlayerOnGenerate = true;

    // ── Events ─────────────────────────────────────────────────────────────

    public static Action OnLevelReady;

    // ── Runtime state ──────────────────────────────────────────────────────

    private List<PlacedRoom>                                               placedRooms;
    private Dictionary<Vector2Int, PlacedRoom>                             roomLayoutGrid;
    private Dictionary<(PlacedRoom, Direction), PlacedRoom>                connections;
    private GameObject                                                     spawnedPlayer;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        ReadPrefabDimensions();
        Invoke(nameof(GenerateLevel), 0.1f);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void GenerateLevel()
    {
        ClearLevel();

        placedRooms    = new();
        roomLayoutGrid = new();
        connections    = new();

        if (!GenerateLayout()) { Debug.LogError("[LevelGenerator] Layout failed."); return; }

        ConfigureDoors();
        InitRoomGrids();
        InitDoors();
        PaintHallways();

        PlacedRoom start = placedRooms.Find(r =>
            r.prefabData.roomType == RoomType.Start && r.roomGrid != null && r.roomGrid.IsInitialized());

        if (start == null)
        {
            Debug.LogError("[LevelGenerator] No valid start room — retrying.");
            GenerateLevel();
            return;
        }

        if (spawnPlayerOnGenerate && playerPrefabs != null && playerPrefabs.Count > 0)
            SpawnPlayer(start);

        Debug.Log($"[LevelGenerator] {placedRooms.Count} rooms generated.");
        OnLevelReady?.Invoke();
    }

    public List<PlacedRoom>    GetAllRooms()   => placedRooms;
    public PlacedRoom          GetConnectedRoom(PlacedRoom room, Direction dir)
    {
        connections.TryGetValue((room, dir), out var r);
        return r;
    }
    public Direction GetOppositeDirection(Direction d) => d switch
    {
        Direction.North => Direction.South,
        Direction.South => Direction.North,
        Direction.East  => Direction.West,
        Direction.West  => Direction.East,
        _               => Direction.North
    };

    // ── Clear ──────────────────────────────────────────────────────────────

    private void ClearLevel()
    {
        foreach (Transform c in transform) Destroy(c.gameObject);

        if (spawnedPlayer != null) { Destroy(spawnedPlayer); spawnedPlayer = null; }

        hallwayTilemap?.ClearAllTiles();

        EnemyManager.Instance?.ClearAllEnemies();
        RoomManager.Instance?.ClearCurrentRoom();
    }

    // ── Layout generation ──────────────────────────────────────────────────

    private bool GenerateLayout()
    {
        if (GetRandomPrefab(RoomType.End) == null)
        { Debug.LogError("[LevelGenerator] No End room prefab!"); return false; }

        PlacedRoom start = PlaceRoom(RoomType.Start, Vector2Int.zero, Vector3.zero);
        if (start == null) return false;

        int targetCount = Random.Range(
            WaveManager.Instance?.GetMinRooms() ?? minRooms,
            (WaveManager.Instance?.GetMaxRooms() ?? maxRooms) + 1);

        var queue      = new Queue<PlacedRoom>();
        var lastNormal = start;
        queue.Enqueue(start);

        int count = 1, attempts = 0;
        bool bossPlaced = false;

        while (queue.Count > 0 && count < targetCount && attempts < 200)
        {
            attempts++;
            PlacedRoom current = queue.Dequeue();
            var dirs = GetAvailableDirections(current);
            Shuffle(dirs);

            foreach (var dir in dirs)
            {
                if (count >= targetCount) break;

                RoomType type = DetermineType(count, targetCount, bossPlaced);
                PlacedRoom newRoom = PlaceRoomInDirection(current, dir, type);
                if (newRoom == null) continue;

                Connect(current, newRoom, dir);
                lastNormal = newRoom;

                if (type == RoomType.Boss) bossPlaced = true;
                if (type != RoomType.End) queue.Enqueue(newRoom);
                count++;
            }
        }

        if (!placedRooms.Exists(r => r.prefabData.roomType == RoomType.End))
        {
            // Force-append end room onto last normal room
            var from = lastNormal;
            foreach (var d in GetAvailableDirections(from))
            {
                var er = PlaceRoomInDirection(from, d, RoomType.End);
                if (er != null) { Connect(from, er, d); break; }
            }
        }

        return placedRooms.Exists(r => r.prefabData.roomType == RoomType.End);
    }

    // ── Room placement ─────────────────────────────────────────────────────

    private PlacedRoom PlaceRoom(RoomType type, Vector2Int layoutPos, Vector3 worldPos)
    {
        if (roomLayoutGrid.ContainsKey(layoutPos)) return null;

        RoomPrefabData data = GetRandomPrefab(type);
        if (data == null) return null;

        GameObject inst = Instantiate(data.prefab, worldPos, Quaternion.identity, transform);
        inst.name = $"{type}Room_({layoutPos.x},{layoutPos.y})";

        RoomConnector conn = inst.GetComponent<RoomConnector>();
        if (conn == null)
        {
            Debug.LogError($"[LevelGenerator] {data.prefab.name} missing RoomConnector!");
            Destroy(inst); return null;
        }

        // Read actual dimensions from setup component
        var setup = inst.GetComponent<RoomTilemapSetup>();
        if (setup != null) { data.width = setup.GetWidth(); data.height = setup.GetHeight(); data.cellSize = setup.GetCellSize(); }

        var placed = new PlacedRoom
        {
            roomInstance  = inst,
            prefabData    = data,
            connector     = conn,
            worldPosition = worldPos,
            gridPosition  = layoutPos
        };
        placedRooms.Add(placed);
        roomLayoutGrid[layoutPos] = placed;
        return placed;
    }

    private PlacedRoom PlaceRoomInDirection(PlacedRoom from, Direction dir, RoomType type)
    {
        var exitPt = from.connector.GetConnectionPoint(dir);
        if (exitPt?.transform == null) return null;

        RoomPrefabData newData = GetRandomPrefab(type);
        if (newData == null) return null;

        RoomConnector tempConn = newData.prefab.GetComponent<RoomConnector>();
        if (tempConn == null) return null;

        Direction opp = GetOppositeDirection(dir);
        if (!tempConn.HasConnectionPoint(opp)) return null;

        var entryPt = tempConn.GetConnectionPoint(opp);

        // Align new room's entry connection with this room's exit point,
        // then push outward by roomSpacing so rooms don't overlap.
        Vector3 offset    = DirToVector(dir) * roomSpacing;
        Vector3 newWorld  = exitPt.transform.position
                            - entryPt.transform.localPosition
                            + offset;
        Vector2Int newLayout = from.gridPosition + DirOffset(dir);

        return PlaceRoom(type, newLayout, newWorld);
    }

    // Converts a Direction to a normalised world Vector3 (XY plane).
    private static Vector3 DirToVector(Direction d) => d switch
    {
        Direction.North => Vector3.up,
        Direction.South => Vector3.down,
        Direction.East  => Vector3.right,
        Direction.West  => Vector3.left,
        _               => Vector3.zero
    };

    private void Connect(PlacedRoom a, PlacedRoom b, Direction dir)
    {
        Direction opp = GetOppositeDirection(dir);
        connections[(a, dir)] = b;
        connections[(b, opp)] = a;
        a.connector.MarkConnectionUsed(dir);
        b.connector.MarkConnectionUsed(opp);
    }

    // ── Hallway painting ───────────────────────────────────────────────────

    private void PaintHallways()
    {
        if (hallwayTilemap == null || hallwayFloorTile == null)
        {
            Debug.LogWarning("[LevelGenerator] HallwayTilemap or hallwayFloorTile not set — " +
                             "skipping hallway painting. Assign them in the Inspector.");
            return;
        }

        var visited = new HashSet<(PlacedRoom, Direction)>();

        foreach (PlacedRoom room in placedRooms)
        foreach (Direction dir in Enum.GetValues(typeof(Direction)))
        {
            if (!connections.TryGetValue((room, dir), out var neighbour)) continue;
            if (visited.Contains((room, dir))) continue;

            visited.Add((room, dir));
            visited.Add((neighbour, GetOppositeDirection(dir)));

            PaintCorridor(room, neighbour, dir);
        }
    }

    private void PaintCorridor(PlacedRoom a, PlacedRoom b, Direction dir)
    {
        var exitPt  = a.connector.GetConnectionPoint(dir)?.transform?.position ?? a.worldPosition;
        var entryPt = b.connector.GetConnectionPoint(GetOppositeDirection(dir))?.transform?.position ?? b.worldPosition;

        Vector3Int startCell = hallwayTilemap.WorldToCell(exitPt);
        Vector3Int endCell   = hallwayTilemap.WorldToCell(entryPt);

        int half = Mathf.Max(0, hallwayWidth / 2);
        int sx = startCell.x, sy = startCell.y;
        int ex = endCell.x,   ey = endCell.y;

        bool horizontal = dir == Direction.East || dir == Direction.West;

        if (horizontal)
        {
            // Leg 1: horizontal run from start to end X, at start Y
            int minX = Mathf.Min(sx, ex), maxX = Mathf.Max(sx, ex);
            for (int x = minX; x <= maxX; x++)
            for (int y = sy - half; y <= sy + half; y++)
                SetHallwayTile(new Vector3Int(x, y, 0));

            // Leg 2: vertical bend if the two doors are at different Y positions
            if (sy != ey)
            {
                int minY = Mathf.Min(sy, ey), maxY = Mathf.Max(sy, ey);
                for (int y = minY; y <= maxY; y++)
                for (int x = ex - half; x <= ex + half; x++)
                    SetHallwayTile(new Vector3Int(x, y, 0));
            }
        }
        else
        {
            // Leg 1: vertical run from start to end Y, at start X
            int minY = Mathf.Min(sy, ey), maxY = Mathf.Max(sy, ey);
            for (int y = minY; y <= maxY; y++)
            for (int x = sx - half; x <= sx + half; x++)
                SetHallwayTile(new Vector3Int(x, y, 0));

            // Leg 2: horizontal bend if the two doors are at different X positions
            if (sx != ex)
            {
                int minX = Mathf.Min(sx, ex), maxX = Mathf.Max(sx, ex);
                for (int x = minX; x <= maxX; x++)
                for (int y = ey - half; y <= ey + half; y++)
                    SetHallwayTile(new Vector3Int(x, y, 0));
            }
        }
    }

    private void SetHallwayTile(Vector3Int pos)
    {
        if (!hallwayTilemap.HasTile(pos))
            hallwayTilemap.SetTile(pos, hallwayFloorTile);
    }

    // ── Door configuration ─────────────────────────────────────────────────

    private void ConfigureDoors()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            if (room.connector == null) continue;
            room.connector.CloseAllDoors();

            bool isBoss = room.prefabData.roomType == RoomType.Boss;

            foreach (Direction dir in Enum.GetValues(typeof(Direction)))
            {
                if (!connections.TryGetValue((room, dir), out PlacedRoom neighbour)) continue;

                if (isBoss && neighbour.prefabData.roomType == RoomType.End)
                {
                    // Boss room exit → locked door managed by BossRoomDoor
                    var strip = GetStripObject(room.connector, dir);
                    if (strip != null)
                    {
                        var brd = strip.GetComponent<BossRoomDoor>() ?? strip.AddComponent<BossRoomDoor>();
                        brd.Initialize(room.roomGrid);
                    }
                }
                else
                {
                    room.connector.SetDoorOpen(dir, true);
                }
            }
        }
    }

    // ── Room grid init ─────────────────────────────────────────────────────

    private void InitRoomGrids()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            var setup = room.roomInstance.GetComponent<RoomTilemapSetup>()
                     ?? room.roomInstance.AddComponent<RoomTilemapSetup>();
            setup.Initialize();

            // Ensure RoomGrid component exists (RoomTilemapSetup adds it via [RequireComponent])
            room.roomGrid = room.roomInstance.GetComponent<RoomGrid>();
        }
    }

    private void InitDoors()
    {
        foreach (PlacedRoom room in placedRooms)
        foreach (RoomDoor door in room.roomInstance.GetComponentsInChildren<RoomDoor>())
            door.Initialize(room);
    }

    // ── Player spawn ───────────────────────────────────────────────────────

    private void SpawnPlayer(PlacedRoom start)
    {
        RoomManager.Instance?.SetCurrentRoom(start);

        int index = CharacterSelection.Index;
        GameObject prefab = (index >= 0 && index < playerPrefabs.Count)
            ? playerPrefabs[index] : playerPrefabs[0];
        if (prefab == null) { Debug.LogError("[LevelGenerator] Player prefab null!"); return; }

        GridPosition? sp = FindSpawnTile(start.roomGrid);
        if (sp == null) { Debug.LogError("[LevelGenerator] No spawn tile in start room!"); return; }

        spawnedPlayer = Instantiate(prefab);
        spawnedPlayer.name = "Player";

        var unit = spawnedPlayer.GetComponent<Unit>();
        unit?.PlaceInRoom(start.roomGrid, sp.Value);

        Debug.Log($"[LevelGenerator] Player spawned at {sp.Value}");
    }

    private GridPosition? FindSpawnTile(RoomGrid roomGrid)
    {
        var tilemap = roomGrid.GetFloorTilemap();
        if (tilemap == null) return null;

        var bounds = tilemap.cellBounds;
        int cx = (bounds.xMin + bounds.xMax) / 2;
        int cy = (bounds.yMin + bounds.yMax) / 2;

        var center = new GridPosition(cx, cy);
        if (roomGrid.IsWalkable(center)) return center;

        for (int r = 1; r < Mathf.Max(bounds.size.x, bounds.size.y); r++)
        for (int x = cx - r; x <= cx + r; x++)
        for (int y = cy - r; y <= cy + r; y++)
        {
            if (Mathf.Abs(x - cx) != r && Mathf.Abs(y - cy) != r) continue;
            var c = new GridPosition(x, y);
            if (roomGrid.IsWalkable(c)) return c;
        }
        return null;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void ReadPrefabDimensions()
    {
        foreach (var d in roomPrefabs)
        {
            if (d.prefab == null) continue;
            var s = d.prefab.GetComponent<RoomTilemapSetup>();
            if (s == null) continue;
            d.width    = s.GetWidth();
            d.height   = s.GetHeight();
            d.cellSize = s.GetCellSize();
        }
    }

    private RoomType DetermineType(int count, int target, bool bossPlaced)
    {
        bool canBoss = spawnBossRoom && !bossPlaced && GetRandomPrefab(RoomType.Boss) != null;
        if (canBoss && count == target - 2) return RoomType.Boss;
        if (count == target - 1) return RoomType.End;
        if (Random.value < specialRoomChance) return RoomType.Special;
        return RoomType.Normal;
    }

    private List<Direction> GetAvailableDirections(PlacedRoom room)
    {
        var list = new List<Direction>();
        if (room.connector == null) return list;
        foreach (Direction d in Enum.GetValues(typeof(Direction)))
        {
            if (room.connector.IsDirectionAvailable(d) &&
                !roomLayoutGrid.ContainsKey(room.gridPosition + DirOffset(d)))
                list.Add(d);
        }
        return list;
    }

    private static Vector2Int DirOffset(Direction d) => d switch
    {
        Direction.North => new(0,  1),
        Direction.South => new(0, -1),
        Direction.East  => new(1,  0),
        Direction.West  => new(-1, 0),
        _               => Vector2Int.zero
    };

    private RoomPrefabData GetRandomPrefab(RoomType type)
    {
        var valid = roomPrefabs.FindAll(p => p.roomType == type);
        if (valid.Count == 0) return null;
        float total = 0; foreach (var p in valid) total += p.spawnWeight;
        float r = Random.value * total, cur = 0;
        foreach (var p in valid) { cur += p.spawnWeight; if (r <= cur) return p; }
        return valid[0];
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int j = Random.Range(i, list.Count);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static GameObject GetStripObject(RoomConnector conn, Direction dir) => dir switch
    {
        Direction.North => conn.northDoorStrip,
        Direction.South => conn.southDoorStrip,
        Direction.East  => conn.eastDoorStrip,
        Direction.West  => conn.westDoorStrip,
        _               => null
    };

    [ContextMenu("Regenerate Level")]
    public void RegenerateLevel() { ReadPrefabDimensions(); GenerateLevel(); }
}
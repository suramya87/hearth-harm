using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Random = UnityEngine.Random;

public class LevelGenerator : MonoBehaviour
{
    [Serializable]
    public class RoomPrefabData
    {
        public GameObject prefab;
        public RoomType   roomType;
        [Range(0f, 1f)] public float spawnWeight = 1f;

        [HideInInspector] public int   width    = 20;
        [HideInInspector] public int   height   = 20;
        [HideInInspector] public float cellSize = 1f;
    }

    public enum RoomType  { Start, End, Normal, Special, Boss }
    public enum Direction { North, South, East, West }

    public class PlacedRoom
    {
        public GameObject     roomInstance;
        public RoomPrefabData prefabData;
        public RoomConnector  connector;
        public Vector3        worldPosition;
        public Vector2Int     gridPosition;
        public RoomGrid       roomGrid;
    }

    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Room Prefabs")]
    [SerializeField] private List<RoomPrefabData> roomPrefabs;

    [Header("Hallways (PCG)")]
    [Tooltip("ScriptableObject with Floor, WallSide, CornerConvex and CornerConcave tile assets. " +
             "Create one via Assets → Create → Level Generation → Hallway Tile Set.")]
    [SerializeField] private HallwayTileSet hallwayTileSet;

    [Tooltip("Cell size used when sizing hallway entry triggers. " +
             "Match your room Grid's Cell Size (usually 1).")]
    [SerializeField] private float hallwayCellSize = 1f;

    [Tooltip("Fallback hallway width in tiles when a room has no SpawnPointTiles on a door. " +
             "Set to 0 to always use SpawnPointTile count. " +
             "Increase this later to make all hallways wider regardless of spawn points.")]
    [SerializeField] private int defaultHallwayWidth = 3;

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

    private List<PlacedRoom>                                placedRooms;
    private Dictionary<Vector2Int, PlacedRoom>              roomLayoutGrid;
    private Dictionary<(PlacedRoom, Direction), PlacedRoom> connections;
    private GameObject                                      spawnedPlayer;
    private readonly List<HallwayGrid>                      spawnedHallways = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        ReadPrefabDimensions();
        Invoke(nameof(GenerateLevel), 0.1f);
    }

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
        BuildHallways();

        PlacedRoom start = placedRooms.Find(r =>
            r.prefabData.roomType == RoomType.Start
            && r.roomGrid != null
            && r.roomGrid.IsInitialized());

        if (start == null)
        {
            Debug.LogError("[LevelGenerator] No valid start room — retrying.");
            GenerateLevel();
            return;
        }

        if (spawnPlayerOnGenerate && playerPrefabs != null && playerPrefabs.Count > 0)
            SpawnPlayer(start);

        Debug.Log($"[LevelGenerator] {placedRooms.Count} rooms + {spawnedHallways.Count} hallways generated.");
        OnLevelReady?.Invoke();
    }

    // ── Public queries ─────────────────────────────────────────────────────

    public List<PlacedRoom>  GetAllRooms()     => placedRooms;
    public List<HallwayGrid> GetAllHallways()  => spawnedHallways;

    public PlacedRoom GetConnectedRoom(PlacedRoom room, Direction dir)
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
        spawnedHallways.Clear();
        EnemyManager.Instance?.ClearAllEnemies();
        RoomManager.Instance?.ClearCurrentRoom();
    }

    // ── Layout generation ──────────────────────────────────────────────────

    private bool GenerateLayout()
    {
        if (GetRandomPrefab(RoomType.End) == null)
        {
            Debug.LogError("[LevelGenerator] No End room prefab!");
            return false;
        }

        PlacedRoom start = PlaceRoom(RoomType.Start, Vector2Int.zero, Vector3.zero);
        if (start == null) return false;

        int targetNormalRooms = Random.Range(
            WaveManager.Instance?.GetMinRooms() ?? minRooms,
            (WaveManager.Instance?.GetMaxRooms() ?? maxRooms) + 1);

        var  queue        = new Queue<PlacedRoom>();
        int  placedMiddle = 0;
        bool bossPlaced   = false;

        queue.Enqueue(start);

        int        attempts   = 0;
        PlacedRoom lastPlaced = start;

        while (queue.Count > 0 && attempts < 1000)
        {
            attempts++;
            PlacedRoom current = queue.Dequeue();

            var dirs = GetAvailableDirections(current);
            Shuffle(dirs);

            foreach (Direction dir in dirs)
            {
                int  middleNeeded  = targetNormalRooms - placedMiddle;
                bool needBoss      = spawnBossRoom && !bossPlaced
                                     && GetRandomPrefab(RoomType.Boss) != null;
                int  reservedSlots = 1 + (needBoss ? 1 : 0);

                if (middleNeeded <= 0) break;

                RoomType type;
                if (needBoss && middleNeeded == reservedSlots)
                    type = RoomType.Boss;
                else if (Random.value < specialRoomChance)
                    type = RoomType.Special;
                else
                    type = RoomType.Normal;

                PlacedRoom newRoom = PlaceRoomInDirection(current, dir, type);
                if (newRoom == null) continue;

                Connect(current, newRoom, dir);
                lastPlaced = newRoom;
                placedMiddle++;
                if (type == RoomType.Boss) bossPlaced = true;

                queue.Enqueue(newRoom);
                if (placedMiddle >= targetNormalRooms) break;
            }

            if (placedMiddle >= targetNormalRooms) break;
        }

        if (placedMiddle < targetNormalRooms)
            Debug.LogWarning($"[LevelGenerator] Only placed {placedMiddle}/{targetNormalRooms} rooms.");

        // Place end room
        bool endPlaced = false;
        var  candidates = new List<PlacedRoom>(placedRooms);
        candidates.Remove(lastPlaced);
        candidates.Insert(0, lastPlaced);

        foreach (PlacedRoom candidate in candidates)
        {
            if (candidate.prefabData.roomType == RoomType.End) continue;
            var available = GetAvailableDirections(candidate);
            if (available.Count == 0) continue;

            PlacedRoom endRoom = PlaceRoomInDirection(candidate, available[0], RoomType.End);
            if (endRoom != null)
            {
                Connect(candidate, endRoom, available[0]);
                endPlaced = true;
                break;
            }
        }

        if (!endPlaced)
        {
            Debug.LogError("[LevelGenerator] Could not place End room!");
            return false;
        }

        return true;
    }

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
            Destroy(inst);
            return null;
        }

        var setup = inst.GetComponent<RoomTilemapSetup>();
        if (setup != null)
        {
            data.width    = setup.GetWidth();
            data.height   = setup.GetHeight();
            data.cellSize = setup.GetCellSize();
        }

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

        var entryPt  = tempConn.GetConnectionPoint(opp);
        Vector3 offset   = DirToVector(dir) * roomSpacing;
        Vector3 newWorld = exitPt.transform.position
                           - entryPt.transform.localPosition
                           + offset;

        Vector2Int newLayout = from.gridPosition + DirOffset(dir);
        return PlaceRoom(type, newLayout, newWorld);
    }

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

    // ── PCG Hallway building ───────────────────────────────────────────────

    /// <summary>
    /// Builds a procedural tilemap hallway for every connected room pair.
    ///
    /// WIDTH CONTROL:
    ///   Width is read from the SpawnPointTile count on each room's SpawnPoints
    ///   tilemap layer. If a door has 0 spawn tiles, defaultHallwayWidth is used
    ///   as the fallback so you always get a sensible corridor.
    ///   To globally widen all hallways, increase defaultHallwayWidth in the
    ///   Inspector. To widen a specific door, add more SpawnPointTiles to that
    ///   room's SpawnPoints layer.
    /// </summary>
    private void BuildHallways()
    {
        if (hallwayTileSet == null)
        {
            Debug.LogWarning("[LevelGenerator] No HallwayTileSet assigned — skipping hallways.");
            return;
        }

        var visited = new HashSet<(PlacedRoom, Direction)>();

        foreach (PlacedRoom room in placedRooms)
        foreach (Direction  dir  in Enum.GetValues(typeof(Direction)))
        {
            if (!connections.TryGetValue((room, dir), out var neighbour)) continue;
            if (visited.Contains((room, dir))) continue;

            visited.Add((room, dir));
            visited.Add((neighbour, GetOppositeDirection(dir)));

            HallwayGrid hallway = HallwayBuilder.Build(
                roomA:            room,
                roomB:            neighbour,
                dirAtoB:          dir,
                parent:           transform,
                tileSet:          hallwayTileSet,
                cellSize:         hallwayCellSize,
                defaultWidth:     defaultHallwayWidth);

            if (hallway != null)
                spawnedHallways.Add(hallway);
        }
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
                    var strip = GetStripObject(room.connector, dir);
                    if (strip != null)
                    {
                        var brd = strip.GetComponent<BossRoomDoor>()
                               ?? strip.AddComponent<BossRoomDoor>();
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

    private void InitRoomGrids()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            var setup = room.roomInstance.GetComponent<RoomTilemapSetup>()
                     ?? room.roomInstance.AddComponent<RoomTilemapSetup>();
            setup.Initialize();

            room.roomGrid = room.roomInstance.GetComponent<RoomGrid>();

            var spawnReader = room.roomInstance.GetComponent<RoomSpawnPointReader>()
                           ?? room.roomInstance.AddComponent<RoomSpawnPointReader>();
            spawnReader.Initialize();
        }
    }

    private void InitDoors()
    {
        foreach (PlacedRoom room in placedRooms)
        foreach (RoomDoor door in room.roomInstance.GetComponentsInChildren<RoomDoor>())
            door.Initialize(room);
    }

    // ── Player spawning ────────────────────────────────────────────────────

    private void SpawnPlayer(PlacedRoom start)
    {
        if (GameManager.IsMultiplayer) return;
        if (Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.IsListening) return;

        RoomManager.Instance?.SetCurrentRoom(start);

        int        index  = CharacterSelection.Index;
        GameObject prefab = (index >= 0 && index < playerPrefabs.Count)
            ? playerPrefabs[index] : playerPrefabs[0];
        if (prefab == null) { Debug.LogError("[LevelGenerator] Player prefab null!"); return; }

        GridPosition? sp = FindSpawnTileFromSpawnPoints(start)
                        ?? FindSpawnTile(start.roomGrid);

        if (sp == null) { Debug.LogError("[LevelGenerator] No spawn tile in start room!"); return; }

        spawnedPlayer      = Instantiate(prefab);
        spawnedPlayer.name = "Player";

        var unit = spawnedPlayer.GetComponent<Unit>();
        unit?.PlaceInRoomWhenReady(start.roomGrid, sp.Value);

        Debug.Log($"[LevelGenerator] Player spawned at {sp.Value}");
    }

    private GridPosition? FindSpawnTileFromSpawnPoints(PlacedRoom room)
    {
        var reader = room.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader == null) return null;

        var all = reader.GetAll();
        if (all.Count == 0) return null;

        foreach (var preferredDir in new[] { Direction.South, Direction.North, Direction.West, Direction.East })
            if (all.TryGetValue(preferredDir, out var pos)) return pos;

        return null;
    }

    private GridPosition? FindSpawnTile(RoomGrid roomGrid)
    {
        var tilemap = roomGrid.GetFloorTilemap();
        if (tilemap == null) return null;

        var bounds = tilemap.cellBounds;
        int cx     = (bounds.xMin + bounds.xMax) / 2;
        int cy     = (bounds.yMin + bounds.yMax) / 2;

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
        Direction.North => new(0,   1),
        Direction.South => new(0,  -1),
        Direction.East  => new(1,   0),
        Direction.West  => new(-1,  0),
        _               => Vector2Int.zero
    };

    private RoomPrefabData GetRandomPrefab(RoomType type)
    {
        var valid = roomPrefabs.FindAll(p => p.roomType == type);
        if (valid.Count == 0) return null;
        float total = 0;
        foreach (var p in valid) total += p.spawnWeight;
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
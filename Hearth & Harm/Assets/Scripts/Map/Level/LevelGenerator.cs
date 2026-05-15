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
    [SerializeField] private HallwayTileSet hallwayTileSet;
    [SerializeField] private float hallwayCellSize    = 1f;
    [SerializeField] private int   defaultHallwayWidth = 3;

    [Header("Generation")]
    [SerializeField] private int   minRooms          = 5;
    [SerializeField] private int   maxRooms          = 10;
    [SerializeField] private float specialRoomChance = 0.3f;
    [SerializeField] private bool  spawnBossRoom     = true;

    [Header("Layout spacing (world units between room centres)")]
    [SerializeField] private float roomSpacing = 25f;

    [Header("Player Prefabs")]
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
        // STEP 0: UnifiedWorldGrid must exist before anything else runs.
        EnsureUnifiedWorldGrid();
 
        ClearLevel();
 
        placedRooms    = new();
        roomLayoutGrid = new();
        connections    = new();
 
        if (!GenerateLayout()) { Debug.LogError("[LevelGenerator] Layout failed."); return; }
 
        ConfigureDoors();
        InitRoomGrids();        // builds room grids, does NOT register yet
        InitDoors();
        BuildHallways();        // builds hallway tiles, does NOT register yet
        RegisterAllTilemaps();  // ONE clean pass: register everything now that all tiles exist
 
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
 
        Debug.Log($"[LevelGenerator] {placedRooms.Count} rooms + {spawnedHallways.Count} hallways. " +
                  $"UnifiedWorldGrid cells: {UnifiedWorldGrid.Instance?.AllCells.Count ?? 0}");
 
        OnLevelReady?.Invoke();
    }
 
    // ── Replaced: no longer registers with UnifiedWorldGrid ───────────────
 
    // ── Replaced: no longer registers with UnifiedWorldGrid ───────────────
 
    private void InitRoomGrids()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            var setup = room.roomInstance.GetComponent<RoomTilemapSetup>()
                     ?? room.roomInstance.AddComponent<RoomTilemapSetup>();
 
            // Initialize() builds the RoomGrid/TilemapRoomGrid structures.
            // It does NOT register with UnifiedWorldGrid — that is done below
            // in RegisterAllTilemaps() after hallways are also built.
            setup.Initialize();
 
            room.roomGrid = room.roomInstance.GetComponent<RoomGrid>();
 
            var spawnReader = room.roomInstance.GetComponent<RoomSpawnPointReader>()
                           ?? room.roomInstance.AddComponent<RoomSpawnPointReader>();
            spawnReader.Initialize();
        }
 
        RecordDoorStates();
    }
 
    // ── New: single-pass registration after ALL tiles exist ───────────────
 
    /// <summary>
    /// Registers every room and hallway into UnifiedWorldGrid in one pass,
    /// called after BuildHallways() so all tilemaps are fully painted.
    ///
    /// This is the ONLY place RegisterTilemap is called. RoomTilemapSetup
    /// and HallwayGrid.Initialize() deliberately do NOT call it anymore.
    /// </summary>
    private void RegisterAllTilemaps()
    {
        var uwg = UnifiedWorldGrid.Instance;
        if (uwg == null)
        {
            Debug.LogError("[LevelGenerator] UnifiedWorldGrid missing — cannot register tilemaps!");
            return;
        }
 
        // Full clear so a regenerated level doesn't accumulate stale cells.
        uwg.Clear();
 
        int roomCount    = 0;
        int hallwayCount = 0;
 
        foreach (PlacedRoom room in placedRooms)
        {
            if (room.roomGrid == null || !room.roomGrid.IsInitialized()) continue;
 
            Tilemap floor = room.roomGrid.GetFloorTilemap();
            Tilemap walls = room.roomGrid.GetWallsTilemap();
            if (floor == null) continue;
 
            uwg.RegisterTilemap(floor, room.roomGrid, walls);
            roomCount++;
        }
 
        foreach (HallwayGrid hg in spawnedHallways)
        {
            if (!hg.IsReady) continue;
            uwg.RegisterTilemap(hg.FloorTilemap, hg.RoomGrid, hg.WallsTilemap);
            hallwayCount++;
        }
 
        Debug.Log($"[LevelGenerator] RegisterAllTilemaps: " +
                  $"{roomCount} rooms + {hallwayCount} hallways = " +
                  $"{uwg.AllCells.Count} total cells in UnifiedWorldGrid.");
    }



    // ── UnifiedWorldGrid bootstrap ─────────────────────────────────────────

    /// <summary>
    /// Creates the UnifiedWorldGrid singleton if it doesn't already exist,
    /// then clears it so stale cells from a previous level aren't left behind.
    ///
    /// Must be called at the very start of GenerateLevel(), before any room
    /// or hallway is initialised.
    /// </summary>
    private static void EnsureUnifiedWorldGrid()
    {
        if (UnifiedWorldGrid.Instance == null)
        {
            var go = new GameObject("UnifiedWorldGrid");
            go.AddComponent<UnifiedWorldGrid>();
            Debug.Log("[LevelGenerator] Created UnifiedWorldGrid singleton.");
        }
        else
        {
            // Clear stale data from the previous level.
            UnifiedWorldGrid.Instance.Clear();
            Debug.Log("[LevelGenerator] Cleared UnifiedWorldGrid for new level.");
        }
    }

    // ── Public queries ─────────────────────────────────────────────────────

    public List<PlacedRoom>  GetAllRooms()    => placedRooms;
    public List<HallwayGrid> GetAllHallways() => spawnedHallways;

    public PlacedRoom GetBossRoom()
        => placedRooms?.Find(r => r.prefabData.roomType == RoomType.Boss);

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

        var queue        = new Queue<PlacedRoom>();
        int placedMiddle = 0;
        queue.Enqueue(start);

        int normalTarget = spawnBossRoom && GetRandomPrefab(RoomType.Boss) != null
            ? Mathf.Max(0, targetNormalRooms - 1)
            : targetNormalRooms;

        PlacedRoom lastNormal = start;
        int attempts = 0;

        while (queue.Count > 0 && placedMiddle < normalTarget && attempts < 1000)
        {
            attempts++;
            PlacedRoom current = queue.Dequeue();

            var dirs = GetAvailableDirections(current);
            Shuffle(dirs);

            foreach (Direction dir in dirs)
            {
                if (placedMiddle >= normalTarget) break;

                RoomType type = Random.value < specialRoomChance
                    ? RoomType.Special : RoomType.Normal;

                PlacedRoom newRoom = PlaceRoomInDirection(current, dir, type);
                if (newRoom == null) continue;

                Connect(current, newRoom, dir);
                lastNormal = newRoom;
                placedMiddle++;
                queue.Enqueue(newRoom);
            }
        }

        if (placedMiddle < normalTarget)
            Debug.LogWarning($"[LevelGenerator] Placed {placedMiddle}/{normalTarget} normal rooms.");

        PlacedRoom bossRoom = null;
        if (spawnBossRoom && GetRandomPrefab(RoomType.Boss) != null)
        {
            bossRoom = TryPlaceRoomOnto(lastNormal, RoomType.Boss);
            if (bossRoom == null)
            {
                for (int i = placedRooms.Count - 1; i >= 0 && bossRoom == null; i--)
                {
                    if (placedRooms[i].prefabData.roomType == RoomType.Start) continue;
                    bossRoom = TryPlaceRoomOnto(placedRooms[i], RoomType.Boss);
                }
            }
            if (bossRoom == null)
                Debug.LogWarning("[LevelGenerator] Could not place Boss room.");
        }

        PlacedRoom anchorForEnd = bossRoom ?? lastNormal;
        PlacedRoom endRoom = TryPlaceRoomOnto(anchorForEnd, RoomType.End);

        if (endRoom == null)
        {
            for (int i = placedRooms.Count - 1; i >= 0 && endRoom == null; i--)
            {
                if (placedRooms[i].prefabData.roomType == RoomType.Start) continue;
                if (placedRooms[i] == bossRoom) continue;
                endRoom = TryPlaceRoomOnto(placedRooms[i], RoomType.End);
            }
        }

        if (endRoom == null)
        {
            Debug.LogError("[LevelGenerator] Could not place End room!");
            return false;
        }

        return true;
    }

    private PlacedRoom TryPlaceRoomOnto(PlacedRoom anchor, RoomType type)
    {
        var dirs = GetAvailableDirections(anchor);
        Shuffle(dirs);
        foreach (Direction dir in dirs)
        {
            PlacedRoom placed = PlaceRoomInDirection(anchor, dir, type);
            if (placed == null) continue;
            Connect(anchor, placed, dir);
            return placed;
        }
        return null;
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

    // ── Hallways ───────────────────────────────────────────────────────────

    private void BuildHallways()
    {
        if (hallwayTileSet == null)
        {
            Debug.LogWarning("[LevelGenerator] No HallwayTileSet — skipping hallways.");
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
                roomA:        room,
                roomB:        neighbour,
                dirAtoB:      dir,
                parent:       transform,
                tileSet:      hallwayTileSet,
                cellSize:     hallwayCellSize,
                defaultWidth: defaultHallwayWidth);

            if (hallway != null)
                spawnedHallways.Add(hallway);
        }
    }

    // ── Doors ──────────────────────────────────────────────────────────────

    private void ConfigureDoors()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            if (room.connector == null || room.roomGrid == null) continue;

            room.connector.CloseAllDoors();

            foreach (Direction dir in Enum.GetValues(typeof(Direction)))
            {
                if (!connections.TryGetValue((room, dir), out PlacedRoom neighbour))
                {
                    room.roomGrid.SetDoorState(dir, false);
                    continue;
                }

                bool isBossExit = room.prefabData.roomType == RoomType.Boss &&
                                  neighbour.prefabData.roomType == RoomType.End;

                if (isBossExit)
                {
                    room.roomGrid.SetDoorState(dir, false);
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
                    room.roomGrid.SetDoorState(dir, true);
                    room.connector.SetDoorOpen(dir, true);
                }
            }
        }
    }

    private void RecordDoorStates()
    {
        foreach (PlacedRoom room in placedRooms)
        {
            if (room.roomGrid == null || room.connector == null) continue;

            foreach (Direction dir in Enum.GetValues(typeof(Direction)))
            {
                bool hasConnection = connections.ContainsKey((room, dir));
                if (!hasConnection)
                {
                    room.roomGrid.SetDoorState(dir, false);
                    room.connector.SetDoorOpen(dir, false);
                }
                else
                {
                    PlacedRoom neighbour = connections[(room, dir)];
                    bool isBossExit = room.prefabData.roomType == RoomType.Boss &&
                                      neighbour.prefabData.roomType == RoomType.End;
                    room.roomGrid.SetDoorState(dir, !isBossExit);
                    room.connector.SetDoorOpen(dir, !isBossExit);
                }
            }
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

        GridPosition? sp = FindPlayerSpawnTile(start)
                        ?? FindCentralFloorTile(start.roomGrid);

        if (sp == null) { Debug.LogError("[LevelGenerator] No spawn tile in start room!"); return; }

        spawnedPlayer      = Instantiate(prefab);
        spawnedPlayer.name = "Player";

        var unit = spawnedPlayer.GetComponent<Unit>();
        unit?.PlaceInRoomWhenReady(start.roomGrid, sp.Value);

        Debug.Log($"[LevelGenerator] Player spawned at {sp.Value}");
    }

    private static GridPosition? FindPlayerSpawnTile(PlacedRoom room)
    {
        foreach (Tilemap tm in room.roomInstance.GetComponentsInChildren<Tilemap>())
        foreach (Vector3Int pos in tm.cellBounds.allPositionsWithin)
            if (tm.GetTile(pos) is PlayerSpawnTile)
                return new GridPosition(pos.x, pos.y);
        return null;
    }

    private static GridPosition? FindCentralFloorTile(RoomGrid roomGrid)
    {
        var tilemap = roomGrid.GetFloorTilemap();
        if (tilemap == null) return null;

        var spawnCells = new HashSet<GridPosition>();
        var root       = tilemap.transform.parent;
        if (root != null)
        {
            foreach (Tilemap tm in root.GetComponentsInChildren<Tilemap>())
            foreach (Vector3Int pos in tm.cellBounds.allPositionsWithin)
                if (tm.GetTile(pos) is SpawnPointTile)
                    spawnCells.Add(new GridPosition(pos.x, pos.y));
        }

        var bounds = tilemap.cellBounds;
        int cx = (bounds.xMin + bounds.xMax) / 2;
        int cy = (bounds.yMin + bounds.yMax) / 2;

        for (int r = 0; r < Mathf.Max(bounds.size.x, bounds.size.y); r++)
        for (int x = cx - r; x <= cx + r; x++)
        for (int y = cy - r; y <= cy + r; y++)
        {
            if (Mathf.Abs(x - cx) != r && Mathf.Abs(y - cy) != r) continue;
            var gp = new GridPosition(x, y);
            if (spawnCells.Contains(gp)) continue;
            if (!roomGrid.IsWalkable(gp)) continue;
            return gp;
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
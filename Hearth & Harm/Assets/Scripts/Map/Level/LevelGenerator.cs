using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Random = UnityEngine.Random;

/// <summary>
/// Generates the level layout and wires everything together.
///
/// KEY CHANGE — HALLWAYS ARE PART OF ROOM GRIDS:
/// BuildHallways() now calls HallwayBuilder.Build() which paints hallway tiles
/// directly into each room's existing tilemaps and re-initializes TilemapRoomGrid.
/// There are no HallwayGrid objects, no HallwayRoomBridge, no SwitchGrid calls.
/// The player pathfinds across rooms and hallways seamlessly on one continuous grid.
///
/// WorldRoomTracker (attached to the player) does a simple per-frame HasTile check
/// to detect when the player crosses from roomA's tiles into roomB's tiles, firing
/// room activation (enemies, doors) at that moment.
/// </summary>
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

        // True when this descriptor was created synthetically (e.g. by HallwayGrid.AsPlacedRoom).
        // Real rooms always have prefabData set.
        public bool IsHallway => prefabData == null;
    }

    [Header("Room Prefabs")]
    [SerializeField] private List<RoomPrefabData> roomPrefabs;

    [Header("Hallways (PCG)")]
    [SerializeField] private HallwayTileSet hallwayTileSet;
    [SerializeField] private float          hallwayCellSize = 1f; // kept for inspector compat

    [Header("Generation")]
    [SerializeField] private int   minRooms          = 5;
    [SerializeField] private int   maxRooms          = 10;
    [SerializeField] private float specialRoomChance = 0.3f;
    [SerializeField] private bool  spawnBossRoom     = true;

    [Header("Layout spacing")]
    [SerializeField] private float roomSpacing = 25f;

    [Header("Player Prefabs")]
    [SerializeField] private List<GameObject> playerPrefabs;
    [SerializeField] private bool spawnPlayerOnGenerate = true;

    public static Action OnLevelReady;

    private List<PlacedRoom>                                    placedRooms;
    private Dictionary<Vector2Int, PlacedRoom>                  roomLayoutGrid;
    private Dictionary<(PlacedRoom, Direction), PlacedRoom>     connections;
    private GameObject                                          spawnedPlayer;

    // Kept empty — no HallwayGrid objects are created anymore.
    // GetAllHallways() returns this for any code that still calls it.
    private readonly List<HallwayGrid> spawnedHallways = new();

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
        BuildHallways();   // paints into room tilemaps — no HallwayGrid objects

        PlacedRoom start = placedRooms.Find(r =>
            r.prefabData?.roomType == RoomType.Start &&
            r.roomGrid != null && r.roomGrid.IsInitialized());

        if (start == null)
        {
            Debug.LogError("[LevelGenerator] No valid start room — retrying.");
            GenerateLevel();
            return;
        }

        if (spawnPlayerOnGenerate && playerPrefabs?.Count > 0)
            SpawnPlayer(start);

        // Attach WorldRoomTracker — simple per-frame room detection, no grid switching
        var playerGo = spawnedPlayer ?? GameObject.FindWithTag("Player");
        if (playerGo != null)
            WorldRoomTracker.Attach(playerGo, this);

        Debug.Log($"[LevelGenerator] {placedRooms.Count} rooms generated.");
        OnLevelReady?.Invoke();
    }

    // ── Public queries ─────────────────────────────────────────────────────

    public List<PlacedRoom>  GetAllRooms()    => placedRooms;
    public List<HallwayGrid> GetAllHallways() => spawnedHallways; // always empty now

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

    // ── Hallway building ───────────────────────────────────────────────────

    private void BuildHallways()
    {
        if (hallwayTileSet == null || !hallwayTileSet.IsValid)
        {
            Debug.LogError("[BuildHallways] HallwayTileSet null or invalid.");
            return;
        }

        var visited = new HashSet<(PlacedRoom, Direction)>();
        int built   = 0;

        foreach (var room in placedRooms)
        foreach (Direction dir in Enum.GetValues(typeof(Direction)))
        {
            if (!connections.TryGetValue((room, dir), out var neighbour)) continue;
            if (visited.Contains((room, dir))) continue;

            visited.Add((room, dir));
            visited.Add((neighbour, GetOppositeDirection(dir)));

            // Paint hallway tiles directly into both rooms' tilemaps
            HallwayBuilder.Build(room, neighbour, dir, hallwayTileSet);
            built++;
        }

        Debug.Log($"[BuildHallways] {built} hallways painted into room tilemaps.");
    }

    // ── Room grid init ─────────────────────────────────────────────────────

    private void InitRoomGrids()
    {
        foreach (var room in placedRooms)
        {
            var setup = room.roomInstance.GetComponent<RoomTilemapSetup>()
                     ?? room.roomInstance.AddComponent<RoomTilemapSetup>();
            setup.Initialize();

            room.roomGrid = room.roomInstance.GetComponent<RoomGrid>();

            var reader = room.roomInstance.GetComponent<RoomSpawnPointReader>()
                      ?? room.roomInstance.AddComponent<RoomSpawnPointReader>();
            reader.Initialize();
        }
    }

    private void InitDoors()
    {
        foreach (var room in placedRooms)
        foreach (var door in room.roomInstance.GetComponentsInChildren<RoomDoor>())
            door.Initialize(room);
    }

    // ── Door configuration ─────────────────────────────────────────────────

    private void ConfigureDoors()
    {
        foreach (var room in placedRooms)
        {
            room.connector?.CloseAllDoors();
            bool isBoss = room.prefabData.roomType == RoomType.Boss;

            foreach (Direction dir in Enum.GetValues(typeof(Direction)))
            {
                if (!connections.TryGetValue((room, dir), out var neighbour)) continue;

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

    // ── Player spawning ────────────────────────────────────────────────────

    private void SpawnPlayer(PlacedRoom start)
    {
        if (GameManager.IsMultiplayer) return;
        if (Unity.Netcode.NetworkManager.Singleton?.IsListening == true) return;

        RoomManager.Instance?.SetCurrentRoom(start);

        int idx    = CharacterSelection.Index;
        var prefab = (idx >= 0 && idx < playerPrefabs.Count)
            ? playerPrefabs[idx] : playerPrefabs[0];
        if (prefab == null) { Debug.LogError("[LevelGenerator] Player prefab null!"); return; }

        var sp = FindSpawnTileFromSpawnPoints(start) ?? FindSpawnTile(start.roomGrid);
        if (sp == null) { Debug.LogError("[LevelGenerator] No spawn tile!"); return; }

        spawnedPlayer      = Instantiate(prefab);
        spawnedPlayer.name = "Player";
        spawnedPlayer.GetComponent<Unit>()?.PlaceInRoom(start.roomGrid, sp.Value);

        Debug.Log($"[LevelGenerator] Player spawned at {sp.Value}");
    }

    private GridPosition? FindSpawnTileFromSpawnPoints(PlacedRoom room)
    {
        // Prefer the floor centre — it's always inside the room and never on a door seam
        var centre = FindSpawnTile(room.roomGrid);
        if (centre.HasValue)
        {
            Debug.Log($"[LevelGenerator] Spawning at floor centre: {centre.Value}");
            return centre;
        }
    
        // Fallback: use authored spawn point tiles
        var reader = room.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader == null) return null;
        var all = reader.GetAll();
        if (all.Count == 0) return null;
    
        // Pick whichever direction has a spawn tile — order doesn't matter here
        // since we already failed to find a centre tile
        foreach (var dir in new[] { Direction.South, Direction.North,
                                    Direction.West,  Direction.East })
            if (all.TryGetValue(dir, out var pos)) return pos;
        return null;
    }


    private GridPosition? FindSpawnTile(RoomGrid roomGrid)
    {
        var tilemap = roomGrid?.GetFloorTilemap();
        if (tilemap == null) return null;
    
        // Use the original room size to find centre, not the expanded post-hallway bounds.
        // The room prefab is always 18x18, so centre is at roughly (0,0) in tilemap space.
        // We find it by scanning inward from the bounds centre.
        var b  = tilemap.cellBounds;
    
        // Estimate the original room centre by using a fraction of the full bounds.
        // Since hallway tiles only extend outward, the original centre is still valid.
        int cx = (b.xMin + b.xMax) / 2;
        int cy = (b.yMin + b.yMax) / 2;
    
        // Walk outward from centre until we find a walkable tile
        for (int r = 0; r < Mathf.Max(b.size.x, b.size.y); r++)
        for (int x = cx - r; x <= cx + r; x++)
        for (int y = cy - r; y <= cy + r; y++)
        {
            if (Mathf.Abs(x - cx) != r && Mathf.Abs(y - cy) != r) continue;
            var c = new GridPosition(x, y);
            if (roomGrid.IsWalkable(c)) return c;
        }
        return null;
    }


    // ── Layout generation (unchanged) ──────────────────────────────────────

    private bool GenerateLayout()
    {
        if (GetRandomPrefab(RoomType.End) == null)
        { Debug.LogError("[LevelGenerator] No End room prefab!"); return false; }

        var start = PlaceRoom(RoomType.Start, Vector2Int.zero, Vector3.zero);
        if (start == null) return false;

        int target = Random.Range(
            WaveManager.Instance?.GetMinRooms() ?? minRooms,
            (WaveManager.Instance?.GetMaxRooms() ?? maxRooms) + 1);

        var queue       = new Queue<PlacedRoom>();
        int placed      = 0;
        bool bossPlaced = false;
        queue.Enqueue(start);
        var lastPlaced = start;
        int attempts   = 0;

        while (queue.Count > 0 && attempts++ < 1000)
        {
            var current = queue.Dequeue();
            var dirs    = GetAvailableDirections(current);
            Shuffle(dirs);

            foreach (Direction dir in dirs)
            {
                if (placed >= target) break;

                bool needBoss = spawnBossRoom && !bossPlaced
                             && GetRandomPrefab(RoomType.Boss) != null;
                int needed   = target - placed;
                int reserved = 1 + (needBoss ? 1 : 0);

                RoomType type;
                if (needBoss && needed == reserved)      type = RoomType.Boss;
                else if (Random.value < specialRoomChance) type = RoomType.Special;
                else                                       type = RoomType.Normal;

                var newRoom = PlaceRoomInDirection(current, dir, type);
                if (newRoom == null) continue;

                Connect(current, newRoom, dir);
                lastPlaced = newRoom;
                placed++;
                if (type == RoomType.Boss) bossPlaced = true;
                queue.Enqueue(newRoom);
            }
            if (placed >= target) break;
        }

        var candidates = new List<PlacedRoom>(placedRooms);
        candidates.Remove(lastPlaced);
        candidates.Insert(0, lastPlaced);

        foreach (var candidate in candidates)
        {
            if (candidate.prefabData.roomType == RoomType.End) continue;
            var avail = GetAvailableDirections(candidate);
            if (avail.Count == 0) continue;
            var end = PlaceRoomInDirection(candidate, avail[0], RoomType.End);
            if (end == null) continue;
            Connect(candidate, end, avail[0]);
            return true;
        }

        Debug.LogError("[LevelGenerator] Could not place End room!");
        return false;
    }

    private PlacedRoom PlaceRoom(RoomType type, Vector2Int layoutPos, Vector3 worldPos)
    {
        if (roomLayoutGrid.ContainsKey(layoutPos)) return null;
        var data = GetRandomPrefab(type);
        if (data == null) return null;

        var inst = Instantiate(data.prefab, worldPos, Quaternion.identity, transform);
        inst.name = $"{type}Room_({layoutPos.x},{layoutPos.y})";

        var conn = inst.GetComponent<RoomConnector>();
        if (conn == null)
        { Debug.LogError($"[LevelGenerator] {data.prefab.name} missing RoomConnector!"); Destroy(inst); return null; }

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

        var newData = GetRandomPrefab(type);
        if (newData == null) return null;

        var tempConn = newData.prefab.GetComponent<RoomConnector>();
        if (tempConn == null) return null;

        Direction opp = GetOppositeDirection(dir);
        if (!tempConn.HasConnectionPoint(opp)) return null;

        var entryPt  = tempConn.GetConnectionPoint(opp);
        var newWorld = exitPt.transform.position
                     - entryPt.transform.localPosition
                     + DirToVector(dir) * roomSpacing;

        return PlaceRoom(type, from.gridPosition + DirOffset(dir), newWorld);
    }

    private void Connect(PlacedRoom a, PlacedRoom b, Direction dir)
    {
        Direction opp = GetOppositeDirection(dir);
        connections[(a, dir)] = b;
        connections[(b, opp)] = a;
        a.connector.MarkConnectionUsed(dir);
        b.connector.MarkConnectionUsed(opp);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void ReadPrefabDimensions()
    {
        foreach (var d in roomPrefabs)
        {
            if (d.prefab == null) continue;
            var s = d.prefab.GetComponent<RoomTilemapSetup>();
            if (s == null) continue;
            d.width = s.GetWidth(); d.height = s.GetHeight(); d.cellSize = s.GetCellSize();
        }
    }

    private List<Direction> GetAvailableDirections(PlacedRoom room)
    {
        var list = new List<Direction>();
        if (room.connector == null) return list;
        foreach (Direction d in Enum.GetValues(typeof(Direction)))
            if (room.connector.IsDirectionAvailable(d) &&
                !roomLayoutGrid.ContainsKey(room.gridPosition + DirOffset(d)))
                list.Add(d);
        return list;
    }

    private RoomPrefabData GetRandomPrefab(RoomType type)
    {
        var valid = roomPrefabs.FindAll(p => p.roomType == type);
        if (valid.Count == 0) return null;
        float total = 0; foreach (var p in valid) total += p.spawnWeight;
        float r = Random.value * total, cur = 0;
        foreach (var p in valid) { cur += p.spawnWeight; if (r <= cur) return p; }
        return valid[0];
    }

    private static Vector3    DirToVector(Direction d) => d switch
    { Direction.North=>Vector3.up, Direction.South=>Vector3.down,
      Direction.East=>Vector3.right, Direction.West=>Vector3.left, _=>Vector3.zero };

    private static Vector2Int DirOffset(Direction d) => d switch
    { Direction.North=>new(0,1), Direction.South=>new(0,-1),
      Direction.East=>new(1,0),  Direction.West=>new(-1,0), _=>Vector2Int.zero };

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        { int j = Random.Range(i, list.Count); (list[i], list[j]) = (list[j], list[i]); }
    }

    private static GameObject GetStripObject(RoomConnector conn, Direction dir) => dir switch
    { Direction.North=>conn.northDoorStrip, Direction.South=>conn.southDoorStrip,
      Direction.East=>conn.eastDoorStrip,   Direction.West=>conn.westDoorStrip, _=>null };

    [ContextMenu("Regenerate Level")]
    public void RegenerateLevel() { ReadPrefabDimensions(); GenerateLevel(); }
}
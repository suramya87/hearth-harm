using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Owns the Unity Grid + child Tilemaps for one procedural hallway.
///
/// CHANGES FROM ORIGINAL
///   • Exposes AsPlacedRoom() which wraps this hallway in a
///     LevelGenerator.PlacedRoom-compatible descriptor so RoomManager can
///     track hallways identically to normal rooms.
///   • IsReady now also checks TilemapRoomGrid.IsInitialized for completeness.
///
/// ROOT HIERARCHY
///   HallwayRoot  (Grid, HallwayGrid, RoomGrid, TilemapRoomGrid, Tilemap*)
///     Floor      (Tilemap, TilemapRenderer)
///     Walls      (Tilemap, TilemapRenderer)
///
/// *Unity adds a Tilemap to the root automatically because TilemapRoomGrid has
///  [RequireComponent(typeof(Tilemap))].  We leave that root Tilemap empty and
///  invisible — it never gets any tiles painted on it.
/// </summary>
[RequireComponent(typeof(Grid))]
public class HallwayGrid : MonoBehaviour
{
    public Tilemap  FloorTilemap { get; private set; }
    public Tilemap  WallsTilemap { get; private set; }
    public RoomGrid RoomGrid     { get; private set; }

    public LevelGenerator.PlacedRoom RoomA   { get; private set; }
    public LevelGenerator.PlacedRoom RoomB   { get; private set; }
    public LevelGenerator.Direction  DirAtoB { get; private set; }

    // Lazily-created PlacedRoom descriptor for use by RoomManager / TilemapHighlighter.
    private LevelGenerator.PlacedRoom _asPlacedRoom;

    // ── Factory ────────────────────────────────────────────────────────────

    public static HallwayGrid Create(
        Transform                 parent,
        LevelGenerator.PlacedRoom roomA,
        LevelGenerator.PlacedRoom roomB,
        LevelGenerator.Direction  dirAtoB,
        string                    name = "Hallway")
    {
        // ── Root GameObject ────────────────────────────────────────────────
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);

        // Add Grid first so child Tilemaps register to it
        go.AddComponent<Grid>();

        // TilemapRoomGrid [RequireComponent] forces a Tilemap onto root.
        // The root Tilemap stays empty — it's just there to satisfy the constraint.
        go.AddComponent<TilemapRoomGrid>();
        var rootRen = go.GetComponent<TilemapRenderer>();
        if (rootRen == null) rootRen = go.AddComponent<TilemapRenderer>();
        rootRen.enabled = false;

        var hg = go.AddComponent<HallwayGrid>();
        var rg = go.AddComponent<RoomGrid>();

        // ── Floor child ────────────────────────────────────────────────────
        var floorGo  = new GameObject("Floor");
        floorGo.transform.SetParent(go.transform, worldPositionStays: false);
        var floor    = floorGo.AddComponent<Tilemap>();
        var floorRen = floorGo.AddComponent<TilemapRenderer>();
        floorRen.sortingOrder = 0;

        // ── Walls child ────────────────────────────────────────────────────
        var wallsGo  = new GameObject("Walls");
        wallsGo.transform.SetParent(go.transform, worldPositionStays: false);
        var walls    = wallsGo.AddComponent<Tilemap>();
        var wallsRen = wallsGo.AddComponent<TilemapRenderer>();
        wallsRen.sortingOrder = 1;

        hg.FloorTilemap = floor;
        hg.WallsTilemap = walls;
        hg.RoomGrid     = rg;
        hg.RoomA        = roomA;
        hg.RoomB        = roomB;
        hg.DirAtoB      = dirAtoB;

        return hg;
    }

    /// <summary>
    /// Call AFTER HallwayTilemapPainter has finished writing tiles.
    /// </summary>
    public void Initialize()
    {
        if (FloorTilemap == null || WallsTilemap == null)
        {
            Debug.LogError($"[HallwayGrid] Floor or Walls tilemap missing on {gameObject.name}!");
            return;
        }

        var trg = GetComponent<TilemapRoomGrid>();
        if (trg == null)
        {
            Debug.LogError($"[HallwayGrid] No TilemapRoomGrid on {gameObject.name}!");
            return;
        }

        trg.Initialize(WallsTilemap, FloorTilemap);
        RoomGrid.Initialize(WallsTilemap, FloorTilemap);

        if (!RoomGrid.IsInitialized())
            Debug.LogError($"[HallwayGrid] RoomGrid failed to initialize on {gameObject.name}. " +
                           $"Floor tile count: {FloorTilemap.GetTilesBlock(FloorTilemap.cellBounds)?.Length}");
        else
            Debug.Log($"[HallwayGrid] {gameObject.name} ready — " +
                      $"W={RoomGrid.GetWidth()} H={RoomGrid.GetHeight()}");
    }

    public bool IsReady => RoomGrid != null && RoomGrid.IsInitialized();

    // ── PlacedRoom wrapper ─────────────────────────────────────────────────

    /// <summary>
    /// Returns a lightweight PlacedRoom descriptor backed by this hallway's
    /// RoomGrid.  RoomManager.SetCurrentRoom and TilemapHighlighter both accept
    /// this so hallways are treated identically to real rooms for UI and combat.
    ///
    /// Note: prefabData, connector, and roomInstance are null — callers that need
    /// those (door locking, enemy spawning) should check for null or test
    /// PlacedRoom.IsHallway before using them.
    /// </summary>
    public LevelGenerator.PlacedRoom AsPlacedRoom()
    {
        if (_asPlacedRoom != null) return _asPlacedRoom;

        _asPlacedRoom = new LevelGenerator.PlacedRoom
        {
            roomInstance  = gameObject,
            roomGrid      = RoomGrid,
            worldPosition = transform.position,
            // prefabData / connector left null — checked by IsHallway below
        };
        return _asPlacedRoom;
    }
}
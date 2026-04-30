using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Owns the Unity Grid + child Tilemaps that make up one procedural hallway.
///
/// Hierarchy created at runtime:
///   HallwayGrid (GameObject)
///     Grid  (Unity Grid component here, HallwayGrid here, RoomGrid here, TilemapRoomGrid here)
///       Floor  (Tilemap + TilemapRenderer, sortOrder 0)
///       Walls  (Tilemap + TilemapRenderer, sortOrder 1)
///
/// Call Initialize() after painting tiles into Floor/Walls.
/// </summary>
[RequireComponent(typeof(Grid))]
[RequireComponent(typeof(RoomGrid))]
[RequireComponent(typeof(TilemapRoomGrid))]
public class HallwayGrid : MonoBehaviour
{
    // ── Sub-tilemaps (set by factory) ──────────────────────────────────────
    public Tilemap FloorTilemap  { get; private set; }
    public Tilemap WallsTilemap  { get; private set; }
    public RoomGrid RoomGrid     { get; private set; }

    // Which rooms this hallway connects
    public LevelGenerator.PlacedRoom RoomA { get; private set; }
    public LevelGenerator.PlacedRoom RoomB { get; private set; }
    public LevelGenerator.Direction  DirAtoB { get; private set; }

    // ── Factory ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the full GameObject hierarchy for one hallway and returns
    /// the HallwayGrid component ready for tile painting.
    /// </summary>
    public static HallwayGrid Create(
        Transform parent,
        LevelGenerator.PlacedRoom roomA,
        LevelGenerator.PlacedRoom roomB,
        LevelGenerator.Direction  dirAtoB,
        string name = "Hallway")
    {
        // Root
        var go   = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);

        // Required components on root
        go.AddComponent<Grid>();
        go.AddComponent<TilemapRoomGrid>();          // RoomGrid [RequireComponent] pulls this
        var hg = go.AddComponent<HallwayGrid>();
        var rg = go.AddComponent<RoomGrid>();

        // Floor child
        var floorGo  = new GameObject("Floor");
        floorGo.transform.SetParent(go.transform, worldPositionStays: false);
        var floor    = floorGo.AddComponent<Tilemap>();
        var floorRen = floorGo.AddComponent<TilemapRenderer>();
        floorRen.sortingOrder = 0;

        // Walls child
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
    /// Call this after HallwayTilemapPainter has finished writing tiles.
    /// Wires RoomGrid → TilemapRoomGrid exactly the same way RoomTilemapSetup does.
    /// </summary>
    public void Initialize()
    {
        RoomGrid.Initialize(WallsTilemap, FloorTilemap);

        if (!RoomGrid.IsInitialized())
            Debug.LogError($"[HallwayGrid] RoomGrid failed to initialize on {gameObject.name}!");
        else
            Debug.Log($"[HallwayGrid] {gameObject.name} initialized. " +
                      $"W={RoomGrid.GetWidth()} H={RoomGrid.GetHeight()}");
    }

    /// <summary>Convenience — the RoomGrid is ready to use.</summary>
    public bool IsReady => RoomGrid != null && RoomGrid.IsInitialized();
}
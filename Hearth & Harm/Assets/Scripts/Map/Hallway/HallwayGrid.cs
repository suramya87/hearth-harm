using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Grid))]
[RequireComponent(typeof(RoomGrid))]
[RequireComponent(typeof(TilemapRoomGrid))]
public class HallwayGrid : MonoBehaviour
{
    // ── Sub-tilemaps (set by factory) ──────────────────────────────────────
    public Tilemap  FloorTilemap { get; private set; }
    public Tilemap  WallsTilemap { get; private set; }
    public RoomGrid RoomGrid     { get; private set; }

    // Which rooms this hallway connects and in which direction
    public LevelGenerator.PlacedRoom RoomA    { get; private set; }
    public LevelGenerator.PlacedRoom RoomB    { get; private set; }
    public LevelGenerator.Direction  DirAtoB  { get; private set; }

    // ── Factory ────────────────────────────────────────────────────────────

    public static HallwayGrid Create(
        Transform                 parent,
        LevelGenerator.PlacedRoom roomA,
        LevelGenerator.PlacedRoom roomB,
        LevelGenerator.Direction  dirAtoB,
        string                    name = "Hallway")
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);

        go.AddComponent<Grid>();
        go.AddComponent<TilemapRoomGrid>();
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


    public void Initialize()
    {
        RoomGrid.Initialize(WallsTilemap, FloorTilemap);

        if (!RoomGrid.IsInitialized())
        {
            Debug.LogError($"[HallwayGrid] RoomGrid failed to initialize on {gameObject.name}!");
            return;
        }

        if (UnifiedWorldGrid.Instance != null)
        {
            UnifiedWorldGrid.Instance.RegisterTilemap(FloorTilemap, RoomGrid, WallsTilemap);
            Debug.Log($"[HallwayGrid] {gameObject.name} registered with UnifiedWorldGrid.");
        }
        else
        {
            Debug.LogWarning($"[HallwayGrid] UnifiedWorldGrid not present — " +
                             $"{gameObject.name} won't be part of the unified graph.");
        }

        Debug.Log($"[HallwayGrid] {gameObject.name} initialized. " +
                  $"W={RoomGrid.GetWidth()} H={RoomGrid.GetHeight()}");
    }

    private void OnDestroy()
    {
        if (UnifiedWorldGrid.Instance != null && FloorTilemap != null)
            UnifiedWorldGrid.Instance.Unregister(FloorTilemap);
    }

    /// <summary>True once Initialize() has completed successfully.</summary>
    public bool IsReady => RoomGrid != null && RoomGrid.IsInitialized();
}
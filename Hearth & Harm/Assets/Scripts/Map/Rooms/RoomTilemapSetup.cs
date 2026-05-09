using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Attach this to the ROOT of every room prefab.
///
/// PREFAB SETUP
///   RoomPrefabRoot  (Grid component here, RoomTilemapSetup here, RoomGrid here, TilemapRoomGrid here)
///     └ Floor       (Tilemap + TilemapRenderer, sorting order 0)
///     └ Walls       (Tilemap + TilemapRenderer, sorting order 1)
///     └ SpawnPoints (Tilemap + TilemapRenderer, sorting order 5, renderer disabled at runtime)
///
/// The script auto-finds tilemaps by GameObject name so you don't have to
/// wire them in the Inspector (but you can override if you want).
/// </summary>
public class RoomTilemapSetup : MonoBehaviour
{
    [Header("Override (leave blank to auto-find by name)")]
    [SerializeField] private Tilemap wallsOverride;
    [SerializeField] private Tilemap floorOverride;

    private bool initialized;

    /// <summary>Called once by LevelGenerator after the room is instantiated.</summary>
    public void Initialize()
    {
        if (initialized) return;

        Tilemap walls = wallsOverride;
        Tilemap floor = floorOverride;

        // Auto-find by child name when not overridden
        foreach (Tilemap tm in GetComponentsInChildren<Tilemap>())
        {
            string n = tm.gameObject.name;
            if (walls == null && n == "Walls") walls = tm;
            if (floor == null && n == "Floor") floor = tm;

            // Hide spawn-point layer at runtime
            if (n == "SpawnPoints")
            {
                var r = tm.GetComponent<TilemapRenderer>();
                if (r != null) r.enabled = false;
            }
        }

        if (floor == null)
            Debug.LogError($"[RoomTilemapSetup] No 'Floor' tilemap found in {gameObject.name}!");

        // Wire RoomGrid / TilemapRoomGrid
        var roomGrid = GetComponent<RoomGrid>() ?? gameObject.AddComponent<RoomGrid>();

        // Ensure TilemapRoomGrid exists (RoomGrid [RequireComponent] adds it automatically,
        // but belt-and-braces here in case script order matters at edit time)
        if (GetComponent<TilemapRoomGrid>() == null)
            gameObject.AddComponent<TilemapRoomGrid>();

        roomGrid.Initialize(walls, floor);

        initialized = true;
    }

    public bool IsInitialized => initialized;

    // ── Size helpers (read from floor tilemap) ────────────────────────────

    public int    GetWidth()  => GetFloor()?.cellBounds.size.x ?? 0;
    public int    GetHeight() => GetFloor()?.cellBounds.size.y ?? 0;
    public float  GetCellSize()
    {
        var grid = GetComponent<Grid>();
        return grid != null ? grid.cellSize.x : 1f;
    }

    private Tilemap GetFloor()
    {
        if (floorOverride != null) return floorOverride;
        foreach (Tilemap tm in GetComponentsInChildren<Tilemap>())
            if (tm.gameObject.name == "Floor") return tm;
        return null;
    }
}
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Singleton that owns a single flat walkability graph spanning every room
/// and hallway in the level.
///
/// Add this component to one GameObject in your scene (e.g. on LevelGenerator).
/// Rooms register themselves via RoomTilemapSetup.Initialize().
/// Hallways register themselves via HallwayBuilder.Build().
/// </summary>
public class UnifiedWorldGrid : MonoBehaviour
{
    public static UnifiedWorldGrid Instance { get; private set; }

    // ── Cell data ──────────────────────────────────────────────────────────

    public class CellData
    {
        public RoomGrid OwnerGrid;
        public Vector3  WorldCentre;
        public bool     IsFloor;
    }

    private readonly Dictionary<Vector3Int, CellData>         cells          = new();
    private readonly Dictionary<Tilemap, HashSet<Vector3Int>> registeredKeys = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Registration ───────────────────────────────────────────────────────

    public void RegisterTilemap(Tilemap floorTilemap, RoomGrid ownerGrid,
                                Tilemap wallsTilemap = null)
    {
        if (floorTilemap == null || ownerGrid == null)
        {
            Debug.LogWarning("[UnifiedWorldGrid] RegisterTilemap called with null args.");
            return;
        }

        Unregister(floorTilemap);

        var keys = new HashSet<Vector3Int>();

        // Floor cells — walkable.
        foreach (Vector3Int cell in floorTilemap.cellBounds.allPositionsWithin)
        {
            if (!floorTilemap.HasTile(cell)) continue;

            var    key    = WorldKey(floorTilemap.GetCellCenterWorld(cell));
            cells[key] = new CellData
            {
                OwnerGrid   = ownerGrid,
                WorldCentre = floorTilemap.GetCellCenterWorld(cell),
                IsFloor     = true,
            };
            keys.Add(key);
        }

        // Wall cells — non-walkable blockers.
        // Floor always wins so hallway floor at a room mouth is never
        // overwritten by the room's wall tile.
        if (wallsTilemap != null)
        {
            foreach (Vector3Int cell in wallsTilemap.cellBounds.allPositionsWithin)
            {
                if (!wallsTilemap.HasTile(cell)) continue;

                var key = WorldKey(wallsTilemap.GetCellCenterWorld(cell));
                if (cells.ContainsKey(key)) continue;

                cells[key] = new CellData
                {
                    OwnerGrid   = ownerGrid,
                    WorldCentre = wallsTilemap.GetCellCenterWorld(cell),
                    IsFloor     = false,
                };
                keys.Add(key);
            }
        }

        registeredKeys[floorTilemap] = keys;

        Debug.Log($"[UnifiedWorldGrid] +{keys.Count} cells from " +
                  $"{floorTilemap.transform.parent?.name ?? floorTilemap.name}. " +
                  $"Total: {cells.Count}");
    }

    public void Unregister(Tilemap floorTilemap)
    {
        if (floorTilemap == null) return;
        if (!registeredKeys.TryGetValue(floorTilemap, out var keys)) return;
        foreach (var key in keys) cells.Remove(key);
        registeredKeys.Remove(floorTilemap);
    }

    /// <summary>Wipe all cells — call before loading a new level.</summary>
    public void Clear()
    {
        cells.Clear();
        registeredKeys.Clear();
    }

    // ── Query API ──────────────────────────────────────────────────────────

    /// <summary>Floor tile at worldPos that is also unoccupied.</summary>
    public bool IsWalkable(Vector3 worldPos)
    {
        if (!cells.TryGetValue(WorldKey(worldPos), out var data)) return false;
        if (!data.IsFloor) return false;
        // Delegate occupancy check to the owning RoomGrid.
        return data.OwnerGrid == null || data.OwnerGrid.IsWalkableAtWorld(data.WorldCentre);
    }

    /// <summary>Floor tile at worldPos (occupancy ignored).</summary>
    public bool IsWalkableIgnoreOccupancy(Vector3 worldPos)
    {
        return cells.TryGetValue(WorldKey(worldPos), out var data) && data.IsFloor;
    }

    public bool     HasCell(Vector3 worldPos) => cells.ContainsKey(WorldKey(worldPos));

    public CellData GetCell(Vector3 worldPos)
    {
        cells.TryGetValue(WorldKey(worldPos), out var data);
        return data;
    }

    /// <summary>Returns the RoomGrid that owns the cell at worldPos.</summary>
    public RoomGrid GetOwnerAt(Vector3 worldPos)
    {
        cells.TryGetValue(WorldKey(worldPos), out var data);
        return data?.OwnerGrid;
    }

    public IReadOnlyDictionary<Vector3Int, CellData> AllCells => cells;

    // ── Neighbour query (used by UnifiedPathfinder) ────────────────────────

    private static readonly Vector3Int[] Steps =
    {
        new( 1,  0, 0), new(-1,  0, 0),
        new( 0,  1, 0), new( 0, -1, 0),
    };

    public List<Vector3Int> GetWalkableNeighbours(Vector3Int key, bool ignoreOccupancy = false)
    {
        var result = new List<Vector3Int>(4);
        foreach (var step in Steps)
        {
            var n = key + step;
            if (!cells.TryGetValue(n, out var data)) continue;
            if (!data.IsFloor) continue;

            if (!ignoreOccupancy && data.OwnerGrid != null)
            {
                if (!data.OwnerGrid.IsWalkableAtWorld(data.WorldCentre)) continue;
            }

            result.Add(n);
        }
        return result;
    }

    // ── Key helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a world position to an integer dictionary key.
    /// RoundToInt handles sub-pixel floating-point drift so positions
    /// slightly off-centre still land on the correct cell.
    /// </summary>
    public static Vector3Int WorldKey(Vector3 worldPos) =>
        new(Mathf.RoundToInt(worldPos.x),
            Mathf.RoundToInt(worldPos.y),
            0);
}
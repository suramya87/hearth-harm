using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Wraps a Unity 2D Tilemap and provides:
///   - GridPosition ↔ world position conversion
///   - Wall / walkability queries
///   - Unit and enemy occupancy tracking per cell
///
/// This is the SINGLE source of truth for the grid.
/// LevelGrid has been removed — ask RoomManager for the current RoomGrid
/// and use TilemapRoomGrid through it.
///
/// SETUP (on the room prefab)
///   Add a Grid component at the root, then child Tilemaps named "Floor" and "Walls".
///   Attach RoomTilemapSetup to the root; it finds and wires everything automatically.
/// </summary>
[RequireComponent(typeof(Tilemap))]
public class TilemapRoomGrid : MonoBehaviour
{
    [Header("Tilemaps (auto-resolved by RoomTilemapSetup)")]
    [SerializeField] private Tilemap wallsTilemap;
    [SerializeField] private Tilemap floorTilemap;

    private Tilemap primaryTilemap;
    private readonly Dictionary<Vector3Int, TilemapCell> cells = new();
    private bool initialized;

    // ── Init ───────────────────────────────────────────────────────────────

    /// <summary>Called by RoomTilemapSetup once tilemaps are resolved.</summary>
    public void Initialize(Tilemap walls, Tilemap floor)
    {
        wallsTilemap   = walls;
        floorTilemap   = floor;
        primaryTilemap = floor != null ? floor : walls;

        if (primaryTilemap == null)
        {
            Debug.LogError($"[TilemapRoomGrid] No tilemaps on {gameObject.name}");
            return;
        }

        cells.Clear();
        foreach (Vector3Int pos in primaryTilemap.cellBounds.allPositionsWithin)
            cells[pos] = new TilemapCell();

        initialized = true;
        Debug.Log($"[TilemapRoomGrid] {gameObject.name} ready. " +
                  $"Bounds: {primaryTilemap.cellBounds}");
    }

    public bool IsInitialized => initialized;

    // ── Coordinate helpers ─────────────────────────────────────────────────

    /// <summary>Grid position → world centre of that cell (Y = room transform Y).</summary>
    public Vector3 GetWorldPosition(GridPosition gp)
    {
        if (primaryTilemap == null) return Vector3.zero;
        Vector3 cell = primaryTilemap.GetCellCenterWorld(new Vector3Int(gp.x, gp.y, 0));
        // Keep the room's Z so layering works correctly in 2D
        return new Vector3(cell.x, cell.y, transform.position.z);
    }

    /// <summary>World position → grid position.</summary>
    public GridPosition GetGridPosition(Vector3 worldPos)
    {
        if (primaryTilemap == null) return default;
        Vector3Int c = primaryTilemap.WorldToCell(worldPos);
        return new GridPosition(c.x, c.y);
    }

    /// <summary>True if the grid position has a floor tile (is inside the room).</summary>
    public bool IsValidGridPosition(GridPosition gp)
    {
        if (!initialized || primaryTilemap == null) return false;
        return primaryTilemap.HasTile(new Vector3Int(gp.x, gp.y, 0));
    }

    /// <summary>True if the world position maps to a tile inside this room.</summary>
    public bool IsPositionInRoom(Vector3 worldPos)
    {
        if (primaryTilemap == null) return false;
        return primaryTilemap.HasTile(primaryTilemap.WorldToCell(worldPos));
    }

    // ── Walkability ────────────────────────────────────────────────────────

    public bool IsWall(GridPosition gp) =>
        wallsTilemap != null && wallsTilemap.HasTile(new Vector3Int(gp.x, gp.y, 0));

    /// <summary>
    /// A cell is walkable when it is a valid floor tile, is not a wall,
    /// and is not occupied by any unit or enemy.
    /// </summary>
    public bool IsWalkable(GridPosition gp) =>
        IsValidGridPosition(gp) && !IsWall(gp) && !IsOccupied(gp);

    /// <summary>Walkable ignoring occupancy — used for pathfinding destination checks.</summary>
    public bool IsWalkableIgnoreOccupancy(GridPosition gp) =>
        IsValidGridPosition(gp) && !IsWall(gp);

    private bool IsOccupied(GridPosition gp)
    {
        var c = GetCell(gp);
        return c != null && c.IsOccupied();
    }

    // ── Unit occupancy ─────────────────────────────────────────────────────

    public void AddUnit(GridPosition gp, Unit u)           => GetOrCreate(gp).AddUnit(u);
    public void RemoveUnit(GridPosition gp, Unit u)        => GetCell(gp)?.RemoveUnit(u);
    public List<Unit> GetUnitsAt(GridPosition gp)          => GetCell(gp)?.GetUnits() ?? new();
    public bool HasAnyUnit(GridPosition gp)                => GetCell(gp)?.HasUnit() ?? false;

    // ── Enemy occupancy ────────────────────────────────────────────────────

    public void AddEnemy(GridPosition gp, EnemyUnit e)      => GetOrCreate(gp).AddEnemy(e);
    public void RemoveEnemy(GridPosition gp, EnemyUnit e)   => GetCell(gp)?.RemoveEnemy(e);
    public List<EnemyUnit> GetEnemiesAt(GridPosition gp)    => GetCell(gp)?.GetEnemies() ?? new();
    public bool HasAnyEnemy(GridPosition gp)                => GetCell(gp)?.HasEnemy() ?? false;

    public EnemyUnit GetEnemyAt(GridPosition gp)
    {
        var list = GetCell(gp)?.GetEnemies();
        return list != null && list.Count > 0 ? list[0] : null;
    }

    // ── Size helpers ───────────────────────────────────────────────────────

    public int GetWidth()  => primaryTilemap?.cellBounds.size.x ?? 0;
    public int GetHeight() => primaryTilemap?.cellBounds.size.y ?? 0;

    // ── Tilemap accessors ──────────────────────────────────────────────────

    public Tilemap GetFloorTilemap() => floorTilemap;
    public Tilemap GetWallsTilemap() => wallsTilemap;

    // ── Private helpers ────────────────────────────────────────────────────

    private TilemapCell GetCell(GridPosition gp)
    {
        cells.TryGetValue(new Vector3Int(gp.x, gp.y, 0), out var c);
        return c;
    }

    private TilemapCell GetOrCreate(GridPosition gp)
    {
        var key = new Vector3Int(gp.x, gp.y, 0);
        if (!cells.TryGetValue(key, out var c))
        {
            c = new TilemapCell();
            cells[key] = c;
        }
        return c;
    }
}
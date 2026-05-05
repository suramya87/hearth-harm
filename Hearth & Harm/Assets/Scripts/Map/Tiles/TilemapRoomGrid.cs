using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Wraps a Unity 2D Tilemap and provides grid position/walkability/occupancy.
///
/// CHANGE FROM ORIGINAL: removed [RequireComponent(typeof(Tilemap))].
/// For rooms, Tilemap is always present on the same GO (added by Unity's tilemap
/// system). For hallways, TilemapRoomGrid sits on the root while the actual
/// Floor/Walls Tilemaps are children — the Initialize(walls, floor) method
/// receives them directly so no same-GO Tilemap is needed.
///
/// FIX: IsValidGridPosition checks HasTile directly — the source of truth.
///
/// FIX: CompressBounds() is called before scanning cellBounds. Unity does not
/// expand cellBounds automatically when tiles are painted at runtime. Without
/// this, hallway tiles painted outside the original room bounds are invisible
/// to cellBounds.allPositionsWithin, and GetWidth()*GetHeight() returns a
/// value too small to cover the hallway — the Pathfinder hits its iteration
/// limit before reaching hallway cells and returns an empty path.
/// </summary>
public class TilemapRoomGrid : MonoBehaviour
{
    [Header("Tilemaps (auto-resolved by RoomTilemapSetup / HallwayGrid)")]
    [SerializeField] private Tilemap wallsTilemap;
    [SerializeField] private Tilemap floorTilemap;

    private Tilemap primaryTilemap;
    private readonly Dictionary<Vector3Int, TilemapCell> cells = new();
    private bool initialized;

    // ── Init ───────────────────────────────────────────────────────────────

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

        // Force Unity to expand cellBounds to include any tiles painted at
        // runtime after the tilemap was first created. Without this, tiles
        // painted outside the original bounds are invisible to
        // cellBounds.allPositionsWithin, and GetWidth()*GetHeight() returns
        // a stale smaller value — the Pathfinder iteration limit is hit
        // before it can reach hallway cells, returning an empty path.
        primaryTilemap.CompressBounds();
        if (walls != null) walls.CompressBounds();

        cells.Clear();
        foreach (Vector3Int pos in primaryTilemap.cellBounds.allPositionsWithin)
        {
            if (primaryTilemap.HasTile(pos))
                cells[pos] = new TilemapCell();
        }

        initialized = true;
        Debug.Log($"[TilemapRoomGrid] {gameObject.name} ready. " +
                  $"Bounds: {primaryTilemap.cellBounds} " +
                  $"Cells with tiles: {cells.Count}");
    }

    public bool IsInitialized => initialized;

    // ── Coordinate helpers ─────────────────────────────────────────────────

    public Vector3 GetWorldPosition(GridPosition gp)
    {
        if (primaryTilemap == null) return Vector3.zero;
        Vector3 cell = primaryTilemap.GetCellCenterWorld(new Vector3Int(gp.x, gp.y, 0));
        return new Vector3(cell.x, cell.y, transform.position.z);
    }

    public GridPosition GetGridPosition(Vector3 worldPos)
    {
        if (primaryTilemap == null) return default;
        Vector3Int c = primaryTilemap.WorldToCell(worldPos);
        return new GridPosition(c.x, c.y);
    }

    public bool IsValidGridPosition(GridPosition gp)
    {
        if (!initialized || primaryTilemap == null) return false;
        return primaryTilemap.HasTile(new Vector3Int(gp.x, gp.y, 0));
    }

    public bool IsPositionInRoom(Vector3 worldPos)
    {
        if (primaryTilemap == null) return false;
        return primaryTilemap.HasTile(primaryTilemap.WorldToCell(worldPos));
    }

    // ── Walkability ────────────────────────────────────────────────────────

    public bool IsWall(GridPosition gp) =>
        wallsTilemap != null && wallsTilemap.HasTile(new Vector3Int(gp.x, gp.y, 0));

    public bool IsWalkable(GridPosition gp) =>
        IsValidGridPosition(gp) && !IsWall(gp) && !IsOccupied(gp);

    public bool IsWalkableIgnoreOccupancy(GridPosition gp) =>
        IsValidGridPosition(gp) && !IsWall(gp);

    private bool IsOccupied(GridPosition gp)
    {
        var c = GetCell(gp);
        return c != null && c.IsOccupied();
    }

    // ── Unit occupancy ─────────────────────────────────────────────────────

    public void AddUnit(GridPosition gp, Unit u)    => GetOrCreate(gp).AddUnit(u);
    public void RemoveUnit(GridPosition gp, Unit u) => GetCell(gp)?.RemoveUnit(u);
    public List<Unit> GetUnitsAt(GridPosition gp)   => GetCell(gp)?.GetUnits() ?? new();
    public bool HasAnyUnit(GridPosition gp)         => GetCell(gp)?.HasUnit() ?? false;

    // ── Enemy occupancy ────────────────────────────────────────────────────

    public void AddEnemy(GridPosition gp, EnemyUnit e)    => GetOrCreate(gp).AddEnemy(e);
    public void RemoveEnemy(GridPosition gp, EnemyUnit e) => GetCell(gp)?.RemoveEnemy(e);
    public List<EnemyUnit> GetEnemiesAt(GridPosition gp)  => GetCell(gp)?.GetEnemies() ?? new();
    public bool HasAnyEnemy(GridPosition gp)              => GetCell(gp)?.HasEnemy() ?? false;

    public EnemyUnit GetEnemyAt(GridPosition gp)
    {
        var list = GetCell(gp)?.GetEnemies();
        return list != null && list.Count > 0 ? list[0] : null;
    }

    // ── Size helpers ───────────────────────────────────────────────────────

    public int GetWidth()  => primaryTilemap?.cellBounds.size.x ?? 0;
    public int GetHeight() => primaryTilemap?.cellBounds.size.y ?? 0;

    // ── Tilemap accessors ──────────────────────────────────────────────────

    public Tilemap GetFloorTilemap()  => floorTilemap;
    public Tilemap GetWallsTilemap()  => wallsTilemap;

    // ── Private helpers ────────────────────────────────────────────────────

    private TilemapCell GetCell(GridPosition gp)
    {
        cells.TryGetValue(new Vector3Int(gp.x, gp.y, 0), out var c);
        return c;
    }

    private TilemapCell GetOrCreate(GridPosition gp)
    {
        var key = new Vector3Int(gp.x, gp.y, 0);
        if (!cells.TryGetValue(key, out var c)) { c = new TilemapCell(); cells[key] = c; }
        return c;
    }
}
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Tilemap))]
public class TilemapRoomGrid : MonoBehaviour
{
    [Header("Tilemaps (auto-resolved by RoomTilemapSetup)")]
    [SerializeField] private Tilemap wallsTilemap;
    [SerializeField] private Tilemap floorTilemap;

    private Tilemap primaryTilemap;
    private readonly Dictionary<Vector3Int, TilemapCell> cells = new();
    private bool initialized;

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

        primaryTilemap.CompressBounds();

        cells.Clear();
        foreach (Vector3Int pos in primaryTilemap.cellBounds.allPositionsWithin)
        {
            if (primaryTilemap.HasTile(pos))
            {
                cells[pos] = new TilemapCell();
            }
        }

        initialized = true;
        Debug.Log($"[TilemapRoomGrid] {gameObject.name} ready. Bounds: {primaryTilemap.cellBounds}");
    }

    public bool IsInitialized => initialized;

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

    public void AddUnit(GridPosition gp, Unit u)           => GetOrCreate(gp).AddUnit(u);
    public void RemoveUnit(GridPosition gp, Unit u)        => GetCell(gp)?.RemoveUnit(u);
    public List<Unit> GetUnitsAt(GridPosition gp)          => GetCell(gp)?.GetUnits() ?? new();
    public bool HasAnyUnit(GridPosition gp)                => GetCell(gp)?.HasUnit() ?? false;

    public void AddEnemy(GridPosition gp, EnemyUnit e)      => GetOrCreate(gp).AddEnemy(e);
    public void RemoveEnemy(GridPosition gp, EnemyUnit e)   => GetCell(gp)?.RemoveEnemy(e);
    public List<EnemyUnit> GetEnemiesAt(GridPosition gp)    => GetCell(gp)?.GetEnemies() ?? new();
    public bool HasAnyEnemy(GridPosition gp)                => GetCell(gp)?.HasEnemy() ?? false;

    public EnemyUnit GetEnemyAt(GridPosition gp)
    {
        var list = GetCell(gp)?.GetEnemies();
        return list != null && list.Count > 0 ? list[0] : null;
    }

    public int GetWidth()  => primaryTilemap != null ? primaryTilemap.cellBounds.size.x : 0;
    public int GetHeight() => primaryTilemap != null ? primaryTilemap.cellBounds.size.y : 0;

    public Tilemap GetFloorTilemap() => floorTilemap;
    public Tilemap GetWallsTilemap() => wallsTilemap;

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
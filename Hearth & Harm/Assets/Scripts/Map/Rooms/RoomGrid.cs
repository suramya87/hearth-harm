using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Thin adapter component that lives on every room prefab root.
/// Delegates all real work to TilemapRoomGrid.
///
/// WHY KEEP THIS?
///   Many systems (MoveAction, EnemyAI, LevelGenerator …) already hold a
///   reference typed as RoomGrid.  Keeping this wrapper means those scripts
///   need zero changes.
/// </summary>
[RequireComponent(typeof(TilemapRoomGrid))]
public class RoomGrid : MonoBehaviour
{
    private TilemapRoomGrid tilemapGrid;

    // ── Init ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by RoomTilemapSetup after it resolves the child tilemaps.
    /// </summary>
    public void Initialize(Tilemap walls, Tilemap floor)
    {
        tilemapGrid = GetComponent<TilemapRoomGrid>();
        tilemapGrid.Initialize(walls, floor);
    }

    public bool IsInitialized() => tilemapGrid != null && tilemapGrid.IsInitialized;

    // ── Coordinate helpers ─────────────────────────────────────────────────

    public Vector3      GetWorldPosition(GridPosition gp)   => tilemapGrid.GetWorldPosition(gp);
    public GridPosition GetGridPosition(Vector3 worldPos)   => tilemapGrid.GetGridPosition(worldPos);
    public bool         IsValidGridPosition(GridPosition gp) => tilemapGrid.IsValidGridPosition(gp);
    public bool         IsPositionInRoom(Vector3 worldPos)   => tilemapGrid.IsPositionInRoom(worldPos);

    // ── Walkability ────────────────────────────────────────────────────────

    public bool IsWalkable(GridPosition gp)               => tilemapGrid.IsWalkable(gp);
    public bool IsWalkableIgnoreOccupancy(GridPosition gp) => tilemapGrid.IsWalkableIgnoreOccupancy(gp);
    public bool IsWall(GridPosition gp)                   => tilemapGrid.IsWall(gp);

    // ── Unit management ────────────────────────────────────────────────────

    public void         AddUnitAtGridPosition(GridPosition gp, Unit u)    => tilemapGrid.AddUnit(gp, u);
    public void         RemoveUnitAtGridPosition(GridPosition gp, Unit u) => tilemapGrid.RemoveUnit(gp, u);
    public bool         HasAnyUnitOnGridPosition(GridPosition gp)          => tilemapGrid.HasAnyUnit(gp);
    public List<Unit>   GetUnitsAtGridPosition(GridPosition gp)            => tilemapGrid.GetUnitsAt(gp);

    // ── Enemy management ───────────────────────────────────────────────────

    public void             AddEnemyAtGridPosition(GridPosition gp, EnemyUnit e)    => tilemapGrid.AddEnemy(gp, e);
    public void             RemoveEnemyAtGridPosition(GridPosition gp, EnemyUnit e) => tilemapGrid.RemoveEnemy(gp, e);
    public bool             HasAnyEnemyOnGridPosition(GridPosition gp)               => tilemapGrid.HasAnyEnemy(gp);
    public List<EnemyUnit>  GetEnemiesAtGridPosition(GridPosition gp)                => tilemapGrid.GetEnemiesAt(gp);
    public EnemyUnit        GetEnemyAtGridPosition(GridPosition gp)                  => tilemapGrid.GetEnemyAt(gp);

    // ── Tilemap access ─────────────────────────────────────────────────────

    public TilemapRoomGrid GetTilemapRoomGrid() => tilemapGrid;
    public Tilemap         GetFloorTilemap()    => tilemapGrid?.GetFloorTilemap();
    public Tilemap         GetWallsTilemap()    => tilemapGrid?.GetWallsTilemap();

    // ── Size helpers ───────────────────────────────────────────────────────

    public int GetWidth()  => tilemapGrid?.GetWidth()  ?? 0;
    public int GetHeight() => tilemapGrid?.GetHeight() ?? 0;
}
using System.Collections.Generic;

/// <summary>
/// Data container for one tilemap cell.
/// Stores which units and enemies occupy it.
/// </summary>
public class TilemapCell
{
    private readonly List<Unit>      units   = new();
    private readonly List<EnemyUnit> enemies = new();

    // ── Units ─────────────────────────────────────────────────────────────
    public void AddUnit(Unit u)     { if (!units.Contains(u))   units.Add(u);   }
    public void RemoveUnit(Unit u)  { units.Remove(u); }
    public List<Unit> GetUnits()    => new(units);
    public bool HasUnit()           => units.Count > 0;

    // ── Enemies ───────────────────────────────────────────────────────────
    public void AddEnemy(EnemyUnit e)    { if (!enemies.Contains(e)) enemies.Add(e); }
    public void RemoveEnemy(EnemyUnit e) { enemies.Remove(e); }
    public List<EnemyUnit> GetEnemies()  => new(enemies);
    public bool HasEnemy()               => enemies.Count > 0;

    public bool IsOccupied() => HasUnit() || HasEnemy();

    public override string ToString() =>
        $"units:{units.Count} enemies:{enemies.Count}";
}
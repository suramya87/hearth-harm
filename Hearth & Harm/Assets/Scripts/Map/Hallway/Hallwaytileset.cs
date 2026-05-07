using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// ScriptableObject that bundles the tile assets used by HallwayTilemapPainter.
///
/// CREATE ONE:  Assets → Create → Level Generation → Hallway Tile Set
///
/// TILE GUIDE
///   FloorTile        — walkable corridor interior (required)
///   WallSideTile     — straight wall edge tile    (required)
///   CornerConvexTile — outer 90° corner           (optional, falls back to WallSideTile)
///   CornerConcaveTile— inner 270° corner at bends (optional, falls back to WallSideTile)
/// </summary>
[CreateAssetMenu(menuName = "Level Generation/Hallway Tile Set", fileName = "HallwayTileSet")]
public class HallwayTileSet : ScriptableObject
{
    [Header("Required")]
    [Tooltip("Tile painted on the walkable floor of every hallway corridor.")]
    public TileBase FloorTile;

    [Tooltip("Tile painted on straight wall edges (sides of the corridor).")]
    public TileBase WallSideTile;

    [Header("Optional — falls back to WallSideTile if not assigned")]
    [Tooltip("Outer convex corner tile (the 'pointy' outside corner of a bend).")]
    public TileBase CornerConvexTile;

    [Tooltip("Inner concave corner tile (the 'scooped' inside corner of a bend).")]
    public TileBase CornerConcaveTile;
}
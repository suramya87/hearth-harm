using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Bundles tile assets for HallwayTilemapPainter.
///
/// CREATE:  Assets → Create → Level Generation → Hallway Tile Set
///
/// WALL SLOTS — assign one tile per direction/corner.
/// The painter picks the correct slot automatically based on each wall cell's
/// position relative to the corridor floor.
///
///   WallTop         — wall tile on the NORTH side of a horizontal corridor
///   WallBottom      — wall tile on the SOUTH side of a horizontal corridor
///   WallLeft        — wall tile on the WEST  side of a vertical corridor
///   WallRight       — wall tile on the EAST  side of a vertical corridor
///
///   CapTop          — end cap closing the NORTH end of a vertical corridor
///   CapBottom       — end cap closing the SOUTH end of a vertical corridor
///   CapLeft         — end cap closing the WEST  end of a horizontal corridor
///   CapRight        — end cap closing the EAST  end of a horizontal corridor
///
///   CornerConvex_NE, _NW, _SE, _SW
///                   — outer (pointy) corners at hallway bends or junctions
///
///   CornerConcave_NE, _NW, _SE, _SW
///                   — inner (scooped) corners where corridors turn
///
/// FALLBACK CHAIN
///   Any unassigned directional slot falls back to WallSideTile so you can
///   start with just one tile and progressively add detail.
///
/// FLOOR SLOTS (unchanged from before)
///   FloorTile / FloorVariants[] / AccentTiles[]
/// </summary>
[CreateAssetMenu(menuName = "Level Generation/Hallway Tile Set", fileName = "HallwayTileSet")]
public class HallwayTileSet : ScriptableObject
{
    // ── Floor ──────────────────────────────────────────────────────────────

    [Header("Floor — Required")]
    [Tooltip("Base walkable floor tile used when no variants are assigned.")]
    public TileBase FloorTile;

    [Header("Floor — PCG Variation (optional)")]
    [Tooltip("Additional floor tiles mixed in by world-position noise.")]
    public TileBase[] FloorVariants;

    [Tooltip("Rare accent tiles placed at high Perlin-noise positions.")]
    public TileBase[] AccentTiles;

    [Tooltip("Noise threshold above which an AccentTile is used. " +
             "0.74 ≈ top 26 % of cells get accents. Raise toward 1 for fewer.")]
    [Range(0f, 1f)]
    public float AccentThreshold = 0.74f;

    // ── Straight wall edges ────────────────────────────────────────────────

    [Header("Walls — Straight Edges")]
    [Tooltip("Fallback for any wall slot that is left empty.")]
    public TileBase WallSideTile;

    [Tooltip("North-facing wall (top edge of a horizontal corridor or room).")]
    public TileBase WallTop;

    [Tooltip("South-facing wall (bottom edge of a horizontal corridor or room).")]
    public TileBase WallBottom;

    [Tooltip("West-facing wall (left edge of a vertical corridor or room).")]
    public TileBase WallLeft;

    [Tooltip("East-facing wall (right edge of a vertical corridor or room).")]
    public TileBase WallRight;

    // ── End caps ───────────────────────────────────────────────────────────

    [Header("Walls — End Caps")]
    [Tooltip("Cap tile closing the north end of a vertical corridor " +
             "(painted one cell above the last floor tile heading north).")]
    public TileBase CapTop;

    [Tooltip("Cap tile closing the south end of a vertical corridor.")]
    public TileBase CapBottom;

    [Tooltip("Cap tile closing the west end of a horizontal corridor.")]
    public TileBase CapLeft;

    [Tooltip("Cap tile closing the east end of a horizontal corridor.")]
    public TileBase CapRight;

    // ── Convex (outer) corners ─────────────────────────────────────────────

    [Header("Walls — Convex (outer) Corners")]
    [Tooltip("Outer corner where North and East walls meet.")]
    public TileBase CornerConvex_NE;

    [Tooltip("Outer corner where North and West walls meet.")]
    public TileBase CornerConvex_NW;

    [Tooltip("Outer corner where South and East walls meet.")]
    public TileBase CornerConvex_SE;

    [Tooltip("Outer corner where South and West walls meet.")]
    public TileBase CornerConvex_SW;

    // ── Concave (inner) corners ────────────────────────────────────────────

    [Header("Walls — Concave (inner) Corners")]
    [Tooltip("Inner corner facing the NE quadrant (floor to the north AND east).")]
    public TileBase CornerConcave_NE;

    [Tooltip("Inner corner facing the NW quadrant.")]
    public TileBase CornerConcave_NW;

    [Tooltip("Inner corner facing the SE quadrant.")]
    public TileBase CornerConcave_SE;

    [Tooltip("Inner corner facing the SW quadrant.")]
    public TileBase CornerConcave_SW;

    // ── Resolved accessors (fallback chain) ───────────────────────────────

    /// <summary>Returns the best tile for a wall on the north side of a corridor.</summary>
    public TileBase GetWallTop()    => WallTop    ?? WallSideTile;
    public TileBase GetWallBottom() => WallBottom ?? WallSideTile;
    public TileBase GetWallLeft()   => WallLeft   ?? WallSideTile;
    public TileBase GetWallRight()  => WallRight  ?? WallSideTile;

    public TileBase GetCapTop()    => CapTop    ?? GetWallTop();
    public TileBase GetCapBottom() => CapBottom ?? GetWallBottom();
    public TileBase GetCapLeft()   => CapLeft   ?? GetWallLeft();
    public TileBase GetCapRight()  => CapRight  ?? GetWallRight();

    public TileBase GetConvex_NE() => CornerConvex_NE ?? WallSideTile;
    public TileBase GetConvex_NW() => CornerConvex_NW ?? WallSideTile;
    public TileBase GetConvex_SE() => CornerConvex_SE ?? WallSideTile;
    public TileBase GetConvex_SW() => CornerConvex_SW ?? WallSideTile;

    public TileBase GetConcave_NE() => CornerConcave_NE ?? GetConvex_NE();
    public TileBase GetConcave_NW() => CornerConcave_NW ?? GetConvex_NW();
    public TileBase GetConcave_SE() => CornerConcave_SE ?? GetConvex_SE();
    public TileBase GetConcave_SW() => CornerConcave_SW ?? GetConvex_SW();

    // Keep old accessors so HallwayBuilder still compiles unchanged
    /// <summary>Legacy — painter now uses GetWallTop/Bottom/Left/Right directly.</summary>
    public TileBase CornerConvexTile  => CornerConvex_NE  ?? WallSideTile;
    public TileBase CornerConcaveTile => CornerConcave_NE ?? WallSideTile;
}
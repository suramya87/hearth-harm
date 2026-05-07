using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Procedurally paints hallway tiles into a HallwayGrid.
///
/// SUPPORTED SHAPES
///   Straight  — doors perfectly aligned, single corridor segment.
///   L-bend    — one 90° turn (offset on one axis only).
///   S-bend    — two 90° turns (offset on both axes, or large offset).
///
/// TILE SLOTS (assign in Inspector on LevelGenerator)
///   FloorTile        — fills walkable corridor interior
///   WallSideTile     — straight wall edge (N/S sides of H corridor, E/W of V corridor)
///   CornerConvexTile — outer corner where two wall segments meet (convex, 90° exterior)
///   CornerConcaveTile— inner corner where corridor bends (concave, 270° exterior)
///                      If null, WallSideTile is used as fallback.
///
/// COORDINATE SYSTEM
///   All painting uses world integer tile coords via Tilemap.WorldToCell, so the
///   hallway can live anywhere in world space regardless of room positions.
/// </summary>
public static class HallwayTilemapPainter
{
    // ── Public entry point ─────────────────────────────────────────────────

    /// <param name="hallway">HallwayGrid to paint into (Floor + Walls tilemaps).</param>
    /// <param name="exitWorld">World position of room A's exit connection point.</param>
    /// <param name="entryWorld">World position of room B's entry connection point.</param>
    /// <param name="exitWidth">Mouth width of room A's door in tiles.</param>
    /// <param name="entryWidth">Mouth width of room B's door in tiles.</param>
    /// <param name="dirAtoB">Cardinal direction from room A toward room B.</param>
    /// <param name="tiles">Tile assets to use.</param>
    public static void Paint(
        HallwayGrid              hallway,
        Vector3                  exitWorld,
        Vector3                  entryWorld,
        int                      exitWidth,
        int                      entryWidth,
        LevelGenerator.Direction dirAtoB,
        HallwayTileSet           tiles)
    {
        if (tiles == null || tiles.FloorTile == null)
        {
            Debug.LogError("[HallwayTilemapPainter] No floor tile assigned — aborting.");
            return;
        }

        Tilemap floor = hallway.FloorTilemap;
        Tilemap walls = hallway.WallsTilemap;

        // Convert world mouth centres to integer tile coords
        Vector3Int exitCell  = floor.WorldToCell(exitWorld);
        Vector3Int entryCell = floor.WorldToCell(entryWorld);

        // Build the centreline path (list of tile coords, mouth-to-mouth)
        List<Segment> segments = BuildSegments(exitCell, entryCell, dirAtoB,
                                               exitWidth, entryWidth);

        // Paint each segment
        foreach (var seg in segments)
            PaintSegment(floor, walls, seg, tiles);

        // Paint junctions between adjacent segments (corners)
        for (int i = 0; i < segments.Count - 1; i++)
            PaintJunction(floor, walls, segments[i], segments[i + 1], tiles);
    }

    // ── Segment definition ─────────────────────────────────────────────────

    /// <summary>One rectangular corridor run.</summary>
    private struct Segment
    {
        public Vector3Int Start;      // tile coord of corridor centre at start
        public Vector3Int End;        // tile coord of corridor centre at end
        public int        Width;      // corridor width in tiles (perpendicular to travel)
        public bool       Horizontal; // true = East/West travel, false = North/South
    }

    // ── Path building ──────────────────────────────────────────────────────

    private static List<Segment> BuildSegments(
        Vector3Int               exitCell,
        Vector3Int               entryCell,
        LevelGenerator.Direction dirAtoB,
        int                      exitWidth,
        int                      entryWidth)
    {
        bool primaryHorizontal = dirAtoB == LevelGenerator.Direction.East
                              || dirAtoB == LevelGenerator.Direction.West;

        int dx = entryCell.x - exitCell.x;
        int dy = entryCell.y - exitCell.y;

        // Average the two widths for transition segments; keep individual widths at mouths
        int midWidth = Mathf.Max(1, (exitWidth + entryWidth + 1) / 2);

        // Check alignment
        bool aligned = primaryHorizontal ? (dy == 0) : (dx == 0);

        if (aligned)
        {
            // ── Straight ───────────────────────────────────────────────────
            return new List<Segment>
            {
                new() {
                    Start      = exitCell,
                    End        = entryCell,
                    Width      = midWidth,
                    Horizontal = primaryHorizontal
                }
            };
        }

        // ── L-bend or S-bend ───────────────────────────────────────────────
        // We always travel in the primary direction first, then turn.
        // For large offsets we use an S (two turns).

        var segs = new List<Segment>();

        if (primaryHorizontal)
        {
            // Segment 1: horizontal from exitCell until we're at entryCell.x
            int midX = entryCell.x;
            Vector3Int corner1 = new(midX, exitCell.y, exitCell.z);

            segs.Add(new Segment {
                Start = exitCell, End = corner1,
                Width = exitWidth, Horizontal = true
            });

            // Is the vertical offset large enough to warrant an S?
            // We use an S if |dy| > exitWidth * 2  (otherwise L is fine visually)
            bool useS = Mathf.Abs(dy) > exitWidth * 2;

            if (!useS)
            {
                // L-bend: one vertical segment
                segs.Add(new Segment {
                    Start = corner1, End = entryCell,
                    Width = midWidth, Horizontal = false
                });
            }
            else
            {
                // S-bend: go half-way vertically, horizontal again, then rest of vertical
                int halfY    = exitCell.y + dy / 2;
                Vector3Int c2 = new(midX,        halfY,      exitCell.z);
                Vector3Int c3 = new(entryCell.x, halfY,      exitCell.z);

                segs.Add(new Segment {
                    Start = corner1, End = c2,
                    Width = midWidth, Horizontal = false
                });
                segs.Add(new Segment {
                    Start = c2, End = c3,
                    Width = midWidth, Horizontal = true
                });
                segs.Add(new Segment {
                    Start = c3, End = entryCell,
                    Width = entryWidth, Horizontal = false
                });
            }
        }
        else
        {
            // Primary direction is vertical (North/South)
            int midY = entryCell.y;
            Vector3Int corner1 = new(exitCell.x, midY, exitCell.z);

            segs.Add(new Segment {
                Start = exitCell, End = corner1,
                Width = exitWidth, Horizontal = false
            });

            bool useS = Mathf.Abs(dx) > exitWidth * 2;

            if (!useS)
            {
                segs.Add(new Segment {
                    Start = corner1, End = entryCell,
                    Width = midWidth, Horizontal = true
                });
            }
            else
            {
                int halfX    = exitCell.x + dx / 2;
                Vector3Int c2 = new(halfX,      midY,       exitCell.z);
                Vector3Int c3 = new(halfX,      entryCell.y, exitCell.z);

                segs.Add(new Segment {
                    Start = corner1, End = c2,
                    Width = midWidth, Horizontal = true
                });
                segs.Add(new Segment {
                    Start = c2, End = c3,
                    Width = midWidth, Horizontal = false
                });
                segs.Add(new Segment {
                    Start = c3, End = entryCell,
                    Width = entryWidth, Horizontal = true
                });
            }
        }

        return segs;
    }

    // ── Segment painting ───────────────────────────────────────────────────

    private static void PaintSegment(
        Tilemap floor, Tilemap walls,
        Segment seg, HallwayTileSet tiles)
    {
        int half = seg.Width / 2;

        Vector3Int start = seg.Start;
        Vector3Int end   = seg.End;

        // Iterate along travel axis
        if (seg.Horizontal)
        {
            int xMin = Mathf.Min(start.x, end.x);
            int xMax = Mathf.Max(start.x, end.x);

            for (int x = xMin; x <= xMax; x++)
            {
                // Floor strip
                for (int y = start.y - half; y <= start.y + half; y++)
                    SetFloor(floor, new Vector3Int(x, y, 0), tiles.FloorTile);

                // Wall tiles above and below
                if (tiles.WallSideTile != null)
                {
                    SetWall(walls, new Vector3Int(x, start.y + half + 1, 0), tiles.WallSideTile);
                    SetWall(walls, new Vector3Int(x, start.y - half - 1, 0), tiles.WallSideTile);
                }
            }
        }
        else
        {
            int yMin = Mathf.Min(start.y, end.y);
            int yMax = Mathf.Max(start.y, end.y);

            for (int y = yMin; y <= yMax; y++)
            {
                for (int x = start.x - half; x <= start.x + half; x++)
                    SetFloor(floor, new Vector3Int(x, y, 0), tiles.FloorTile);

                if (tiles.WallSideTile != null)
                {
                    SetWall(walls, new Vector3Int(start.x + half + 1, y, 0), tiles.WallSideTile);
                    SetWall(walls, new Vector3Int(start.x - half - 1, y, 0), tiles.WallSideTile);
                }
            }
        }
    }

    // ── Junction / corner painting ─────────────────────────────────────────

    /// <summary>
    /// Fills the rectangular gap between two perpendicular segments and
    /// places corner tiles on the outer and inner edges.
    /// </summary>
    private static void PaintJunction(
        Tilemap floor, Tilemap walls,
        Segment segA, Segment segB,
        HallwayTileSet tiles)
    {
        // The junction point is segA.End == segB.Start (they share it)
        Vector3Int pivot = segA.End; // == segB.Start

        int halfA = segA.Width / 2;
        int halfB = segB.Width / 2;

        // Fill the junction box with floor tiles
        int xMin = Mathf.Min(pivot.x - halfA, pivot.x - halfB);
        int xMax = Mathf.Max(pivot.x + halfA, pivot.x + halfB);
        int yMin = Mathf.Min(pivot.y - halfA, pivot.y - halfB);
        int yMax = Mathf.Max(pivot.y + halfA, pivot.y + halfB);

        for (int x = xMin; x <= xMax; x++)
        for (int y = yMin; y <= yMax; y++)
            SetFloor(floor, new Vector3Int(x, y, 0), tiles.FloorTile);

        // Outer convex corners
        TileBase convex  = tiles.CornerConvexTile  ?? tiles.WallSideTile;
        TileBase concave = tiles.CornerConcaveTile ?? tiles.WallSideTile;

        if (convex != null)
        {
            // Place convex corners at the four outside corners of the junction box
            SetWall(walls, new Vector3Int(xMin - 1, yMin - 1, 0), convex);
            SetWall(walls, new Vector3Int(xMax + 1, yMin - 1, 0), convex);
            SetWall(walls, new Vector3Int(xMin - 1, yMax + 1, 0), convex);
            SetWall(walls, new Vector3Int(xMax + 1, yMax + 1, 0), convex);
        }

        if (concave != null && segA.Horizontal != segB.Horizontal)
        {
            // Determine which two of the four corners are "inside" (concave)
            // by checking which quadrant is NOT covered by either segment.
            // segA travel direction tells us which axis it runs along.
            bool aGoesRight = segA.End.x > segA.Start.x;
            bool bGoesUp    = segB.End.y > segB.Start.y;

            // The concave corner is where the corridor "bends inward"
            if (segA.Horizontal)
            {
                int cx = aGoesRight ? xMax + 1 : xMin - 1;
                int cy = bGoesUp    ? yMin - 1 : yMax + 1;
                SetWall(walls, new Vector3Int(cx, cy, 0), concave);
            }
            else
            {
                int cx = bGoesUp    ? xMax + 1 : xMin - 1; // repurposed for V→H
                int cy = aGoesRight ? yMin - 1 : yMax + 1;
                SetWall(walls, new Vector3Int(cx, cy, 0), concave);
            }
        }

        // Fill wall tiles along the exposed edges of the junction box
        // (covers the gap where segment wall tiles don't reach)
        if (tiles.WallSideTile != null)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                TrySetWall(walls, floor, new Vector3Int(x, yMin - 1, 0), tiles.WallSideTile);
                TrySetWall(walls, floor, new Vector3Int(x, yMax + 1, 0), tiles.WallSideTile);
            }
            for (int y = yMin; y <= yMax; y++)
            {
                TrySetWall(walls, floor, new Vector3Int(xMin - 1, y, 0), tiles.WallSideTile);
                TrySetWall(walls, floor, new Vector3Int(xMax + 1, y, 0), tiles.WallSideTile);
            }
        }
    }

    // ── Tile helpers ───────────────────────────────────────────────────────

    private static void SetFloor(Tilemap floor, Vector3Int cell, TileBase tile)
    {
        // Floor always wins — don't overwrite with null
        if (tile != null) floor.SetTile(cell, tile);
    }

    private static void SetWall(Tilemap walls, Vector3Int cell, TileBase tile)
    {
        if (tile != null) walls.SetTile(cell, tile);
    }

    /// <summary>Only sets a wall tile if there is no floor tile at that cell.</summary>
    private static void TrySetWall(Tilemap walls, Tilemap floor, Vector3Int cell, TileBase tile)
    {
        if (tile == null) return;
        if (!floor.HasTile(cell)) walls.SetTile(cell, tile);
    }
}
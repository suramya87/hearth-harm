using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

 
public static class HallwayTilemapPainter
{

    public static void Paint(
        HallwayGrid hallway,
        Vector3 exitWorld,
        Vector3 entryWorld,
        int exitWidth,
        int entryWidth,
        LevelGenerator.Direction dirAtoB,
        HallwayTileSet tiles,
        int trimTiles = 1)
    {
        if (tiles == null || tiles.FloorTile == null)
        {
            Debug.LogError("[HallwayTilemapPainter] No FloorTile assigned — aborting.");
            return;
        }

        Tilemap floor = hallway.FloorTilemap;
        Tilemap walls = hallway.WallsTilemap;

        Vector3Int exitCell  = floor.WorldToCell(exitWorld);
        Vector3Int entryCell = floor.WorldToCell(entryWorld);

        int trim = Mathf.Max(0, trimTiles);

        Vector3Int exitTrimmed  = TrimCell(exitCell,  dirAtoB,           trim);
        Vector3Int entryTrimmed = TrimCell(entryCell, Opposite(dirAtoB), trim);

        List<Segment> segments = BuildSegments(
            exitTrimmed, entryTrimmed, dirAtoB, exitWidth, entryWidth);

        foreach (var seg in segments)
            PaintSegment(floor, walls, seg, tiles);

        for (int i = 0; i < segments.Count - 1; i++)
            PaintJunction(floor, walls, segments[i], segments[i + 1], tiles);

    }

    // ── Width helpers ──────────────────────────────────────────────────────

    private static (int min, int max) FloorOffsets(int width)
    {
        int half = width / 2;
        int min  = -half;
        int max  = (width % 2 == 0) ? half - 1 : half;
        return (min, max);
    }

    // ── Segment definition ─────────────────────────────────────────────────

    private struct Segment
    {
        public Vector3Int Start;
        public Vector3Int End;
        public int        Width;
        public bool       Horizontal;
    }

    // ── Path building ──────────────────────────────────────────────────────

    private static List<Segment> BuildSegments(
        Vector3Int               exitCell,
        Vector3Int               entryCell,
        LevelGenerator.Direction dirAtoB,
        int                      exitWidth,
        int                      entryWidth)
    {
        bool primaryH = dirAtoB == LevelGenerator.Direction.East
                     || dirAtoB == LevelGenerator.Direction.West;

        int dx = entryCell.x - exitCell.x;
        int dy = entryCell.y - exitCell.y;
        int bendWidth = Mathf.Min(exitWidth, entryWidth);

        bool aligned = primaryH ? (dy == 0) : (dx == 0);

        if (aligned)
        {
            return new List<Segment> { new() {
                Start = exitCell, End = entryCell,
                Width = exitWidth, Horizontal = primaryH
            }};
        }

        var segs = new List<Segment>();

        if (primaryH)
        {
            int turnX   = exitCell.x + dx / 2;
            var corner1 = new Vector3Int(turnX, exitCell.y,  0);
            var corner2 = new Vector3Int(turnX, entryCell.y, 0);
            segs.Add(new Segment { Start = exitCell,  End = corner1,   Width = exitWidth,  Horizontal = true  });
            segs.Add(new Segment { Start = corner1,   End = corner2,   Width = bendWidth,  Horizontal = false });
            segs.Add(new Segment { Start = corner2,   End = entryCell, Width = entryWidth, Horizontal = true  });
        }
        else
        {
            int turnY   = exitCell.y + dy / 2;
            var corner1 = new Vector3Int(exitCell.x,  turnY, 0);
            var corner2 = new Vector3Int(entryCell.x, turnY, 0);
            segs.Add(new Segment { Start = exitCell,  End = corner1,   Width = exitWidth,  Horizontal = false });
            segs.Add(new Segment { Start = corner1,   End = corner2,   Width = bendWidth,  Horizontal = true  });
            segs.Add(new Segment { Start = corner2,   End = entryCell, Width = entryWidth, Horizontal = false });
        }

        return segs;
    }

    // ── Segment painting ───────────────────────────────────────────────────

    private static void PaintSegment(
        Tilemap floor, Tilemap walls, Segment seg, HallwayTileSet tiles)
    {
        var (pMin, pMax) = FloorOffsets(seg.Width);

        if (seg.Horizontal)
        {
            int xMin = Mathf.Min(seg.Start.x, seg.End.x);
            int xMax = Mathf.Max(seg.Start.x, seg.End.x);
            int cy   = seg.Start.y;

            for (int x = xMin; x <= xMax; x++)
            {
                // Floor strip - Always paints the full length
                for (int p = pMin; p <= pMax; p++)
                    SetFloor(floor, new Vector3Int(x, cy + p, 0), PickFloor(tiles, x, cy + p));

                // Walls - Only paint if NOT at the very start or very end of the segment
                if (x > xMin && x < xMax)
                {
                    SetWall(walls, floor, new Vector3Int(x, cy + pMax + 1, 0), tiles.GetWallTop());
                    SetWall(walls, floor, new Vector3Int(x, cy + pMin - 1, 0), tiles.GetWallBottom());
                }
            }
        }
        else
        {
            int yMin = Mathf.Min(seg.Start.y, seg.End.y);
            int yMax = Mathf.Max(seg.Start.y, seg.End.y);
            int cx   = seg.Start.x;

            for (int y = yMin; y <= yMax; y++)
            {
                for (int p = pMin; p <= pMax; p++)
                    SetFloor(floor, new Vector3Int(cx + p, y, 0), PickFloor(tiles, cx + p, y));

                if (y > yMin && y < yMax)
                {
                    SetWall(walls, floor, new Vector3Int(cx + pMax + 1, y, 0), tiles.GetWallRight());
                    SetWall(walls, floor, new Vector3Int(cx + pMin - 1, y, 0), tiles.GetWallLeft());
                }
            }
        }
    }

    // ── End cap painting ───────────────────────────────────────────────────

    private static void PaintEndCap(
        Tilemap walls, Tilemap floor, Segment seg, bool isStart, HallwayTileSet tiles)
    {
        var (pMin, pMax) = FloorOffsets(seg.Width);
        Vector3Int pivot = isStart ? seg.Start : seg.End;

        if (seg.Horizontal)
        {
            int capX = isStart ? pivot.x - 1 : pivot.x + 1;
            TileBase capTile = isStart ? tiles.GetCapLeft() : tiles.GetCapRight();

            for (int p = pMin; p <= pMax; p++)
                SetWall(walls, floor, new Vector3Int(capX, pivot.y + p, 0), capTile);

            SetWall(walls, floor, new Vector3Int(capX, pivot.y + pMax + 1, 0),
                isStart ? tiles.GetConvex_NW() : tiles.GetConvex_NE());
            SetWall(walls, floor, new Vector3Int(capX, pivot.y + pMin - 1, 0),
                isStart ? tiles.GetConvex_SW() : tiles.GetConvex_SE());
        }
        else
        {
            int capY = isStart ? pivot.y - 1 : pivot.y + 1;
            TileBase capTile = isStart ? tiles.GetCapBottom() : tiles.GetCapTop();

            for (int p = pMin; p <= pMax; p++)
                SetWall(walls, floor, new Vector3Int(pivot.x + p, capY, 0), capTile);

            SetWall(walls, floor, new Vector3Int(pivot.x + pMax + 1, capY, 0),
                isStart ? tiles.GetConvex_SE() : tiles.GetConvex_NE());
            SetWall(walls, floor, new Vector3Int(pivot.x + pMin - 1, capY, 0),
                isStart ? tiles.GetConvex_SW() : tiles.GetConvex_NW());
        }
    }

    // ── Junction painting ──────────────────────────────────────────────────

    private static void PaintJunction(
        Tilemap floor, Tilemap walls,
        Segment segA, Segment segB,
        HallwayTileSet tiles)
    {
        Vector3Int pivot = segA.End;

        var (aMin, aMax) = FloorOffsets(segA.Width);
        var (bMin, bMax) = FloorOffsets(segB.Width);

        int xMin, xMax, yMin, yMax;

        if (segA.Horizontal)
        {
            xMin = pivot.x + aMin;
            xMax = pivot.x + aMax;
            yMin = pivot.y + bMin;
            yMax = pivot.y + bMax;
        }
        else
        {
            xMin = pivot.x + bMin;
            xMax = pivot.x + bMax;
            yMin = pivot.y + aMin;
            yMax = pivot.y + aMax;
        }

        // Fill junction floor
        for (int x = xMin; x <= xMax; x++)
        for (int y = yMin; y <= yMax; y++)
            SetFloor(floor, new Vector3Int(x, y, 0), PickFloor(tiles, x, y));

        // ── Four outer convex corners ──────────────────────────────────────
        SetWall(walls, floor, new Vector3Int(xMin - 1, yMax + 1, 0), tiles.GetConvex_NW());
        SetWall(walls, floor, new Vector3Int(xMax + 1, yMax + 1, 0), tiles.GetConvex_NE());
        SetWall(walls, floor, new Vector3Int(xMin - 1, yMin - 1, 0), tiles.GetConvex_SW());
        SetWall(walls, floor, new Vector3Int(xMax + 1, yMin - 1, 0), tiles.GetConvex_SE());

        // ── Straight wall edges around the junction box ────────────────────
        for (int x = xMin; x <= xMax; x++)
        {
            SetWall(walls, floor, new Vector3Int(x, yMax + 1, 0), tiles.GetWallTop());
            SetWall(walls, floor, new Vector3Int(x, yMin - 1, 0), tiles.GetWallBottom());
        }
        for (int y = yMin; y <= yMax; y++)
        {
            SetWall(walls, floor, new Vector3Int(xMin - 1, y, 0), tiles.GetWallLeft());
            SetWall(walls, floor, new Vector3Int(xMax + 1, y, 0), tiles.GetWallRight());
        }

        // ── Inner concave corner ───────────────────────────────────────────
        if (segA.Horizontal != segB.Horizontal)
        {
            bool aGoesRight = segA.End.x >= segA.Start.x;
            bool bGoesUp    = segB.End.y >= segB.Start.y;
            bool aGoesUp    = segA.End.y >= segA.Start.y;
            bool bGoesRight = segB.End.x >= segB.Start.x;

            int icx, icy;
            TileBase concaveTile;

            if (segA.Horizontal)
            {
                icx = aGoesRight ? xMin - 1 : xMax + 1;
                icy = bGoesUp    ? yMin - 1 : yMax + 1;

                // Which quadrant?
                bool isNorth = icy > yMax;
                bool isEast  = icx > xMax;
                concaveTile = (isNorth, isEast) switch
                {
                    (true,  true)  => tiles.GetConcave_NE(),
                    (true,  false) => tiles.GetConcave_NW(),
                    (false, true)  => tiles.GetConcave_SE(),
                    (false, false) => tiles.GetConcave_SW(),
                };
            }
            else
            {
                icx = bGoesRight ? xMin - 1 : xMax + 1;
                icy = aGoesUp    ? yMin - 1 : yMax + 1;

                bool isNorth = icy > yMax;
                bool isEast  = icx > xMax;
                concaveTile = (isNorth, isEast) switch
                {
                    (true,  true)  => tiles.GetConcave_NE(),
                    (true,  false) => tiles.GetConcave_NW(),
                    (false, true)  => tiles.GetConcave_SE(),
                    (false, false) => tiles.GetConcave_SW(),
                };
            }

            SetWall(walls, floor, new Vector3Int(icx, icy, 0), concaveTile);
        }
    }

    // ── PCG floor tile selection ───────────────────────────────────────────

    private static TileBase PickFloor(HallwayTileSet tiles, int wx, int wy)
    {
        if ((tiles.FloorVariants == null || tiles.FloorVariants.Length == 0) &&
            (tiles.AccentTiles   == null || tiles.AccentTiles.Length   == 0))
            return tiles.FloorTile;

        float noise = Mathf.PerlinNoise(wx * 0.37f + 0.5f, wy * 0.37f + 0.5f);

        if (tiles.AccentTiles != null && tiles.AccentTiles.Length > 0
            && noise > tiles.AccentThreshold)
        {
            int idx = Mathf.Abs(wx * 7 + wy * 13) % tiles.AccentTiles.Length;
            if (tiles.AccentTiles[idx] != null) return tiles.AccentTiles[idx];
        }

        if (tiles.FloorVariants != null && tiles.FloorVariants.Length > 0)
        {
            int idx = Mathf.Abs(wx * 3 + wy * 5) % tiles.FloorVariants.Length;
            if (tiles.FloorVariants[idx] != null) return tiles.FloorVariants[idx];
        }

        return tiles.FloorTile;
    }

    // ── Trim helpers ───────────────────────────────────────────────────────

    private static Vector3Int TrimCell(
        Vector3Int cell, LevelGenerator.Direction dir, int amount)
    {
        if (amount <= 0) return cell;
        return cell + DirectionToStep(dir) * amount;
    }

    private static Vector3Int DirectionToStep(LevelGenerator.Direction d) => d switch
    {
        LevelGenerator.Direction.North => new Vector3Int( 0,  1, 0),
        LevelGenerator.Direction.South => new Vector3Int( 0, -1, 0),
        LevelGenerator.Direction.East  => new Vector3Int( 1,  0, 0),
        LevelGenerator.Direction.West  => new Vector3Int(-1,  0, 0),
        _                              => Vector3Int.zero
    };

    private static LevelGenerator.Direction Opposite(LevelGenerator.Direction d) => d switch
    {
        LevelGenerator.Direction.North => LevelGenerator.Direction.South,
        LevelGenerator.Direction.South => LevelGenerator.Direction.North,
        LevelGenerator.Direction.East  => LevelGenerator.Direction.West,
        LevelGenerator.Direction.West  => LevelGenerator.Direction.East,
        _                              => LevelGenerator.Direction.North
    };

    // ── Tile placement helpers ─────────────────────────────────────────────

    private static void SetFloor(Tilemap floor, Vector3Int cell, TileBase tile)
    {
        if (tile != null) floor.SetTile(cell, tile);
    }

    private static void SetWall(Tilemap walls, Tilemap floor, Vector3Int cell, TileBase tile)
    {
        if (tile == null) return;
        if (floor.HasTile(cell)) return; 
        walls.SetTile(cell, tile);
    }
}
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Paints hallway floor and wall tiles into roomA and roomB's existing tilemaps.
///
/// Cells in the first half of the hallway path go onto roomA's tilemaps.
/// Cells in the second half go onto roomB's tilemaps.
///
/// SPLIT FIX:
///   The original version compared cell world positions against
///   floorA.transform.position and floorB.transform.position (tilemap origins),
///   which could be offset from the room centre if the Tilemap component sits
///   on a child GameObject that isn't centred. We now compare against the
///   cell-bounds centre of each tilemap, which is the true geometric centre of
///   the painted tiles. This gives a correct 50/50 split regardless of how
///   rooms are positioned in the hierarchy.
///
/// WALL SKIP:
///   PaintWalls skips cells that already have a wall tile from the room prefab
///   to avoid overwriting authored corners and edges.
/// </summary>
public static class HallwayTilemapPainter
{
    // ── Public entry point ─────────────────────────────────────────────────

    public static void PaintIntoRooms(
        Tilemap                  floorA,
        Tilemap                  wallsA,
        Tilemap                  floorB,
        Tilemap                  wallsB,
        Vector3                  exitWorld,
        Vector3                  entryWorld,
        int                      exitWidth,
        int                      entryWidth,
        LevelGenerator.Direction dirAtoB,
        HallwayTileSet           tiles)
    {
        if (tiles == null || !tiles.IsValid)
        {
            Debug.LogError("[HallwayTilemapPainter] HallwayTileSet invalid.");
            return;
        }

        // Convert mouth world positions to cell coords in floorA's space.
        // Both tilemaps share world space so WorldToCell is consistent.
        Vector3Int exitCell  = floorA.WorldToCell(exitWorld);
        Vector3Int entryCell = floorA.WorldToCell(entryWorld);

        // Step 1 tile inward from each mouth so spawn tiles stay clear
        exitCell  = StepInward(exitCell,  dirAtoB,           1);
        entryCell = StepInward(entryCell, Opposite(dirAtoB), 1);

        // Build the hallway path segments
        List<Segment> segments = BuildSegments(
            exitCell, entryCell, dirAtoB, exitWidth, entryWidth);

        // Collect every floor cell the hallway needs
        var allFloorCells = new HashSet<Vector3Int>();
        foreach (var seg in segments)
            CollectSegmentFloor(seg, allFloorCells);
        for (int i = 0; i < segments.Count - 1; i++)
            CollectJunctionFloor(segments[i], segments[i + 1], allFloorCells);

        // Split cells between the two rooms by proximity to their tile-bounds centres.
        // Using tile centres (not transform origins) gives a correct geometric split.
        Vector3 centreA = floorA.transform.TransformPoint(floorA.localBounds.center);
        Vector3 centreB = floorB.transform.TransformPoint(floorB.localBounds.center);

        var cellsA = new HashSet<Vector3Int>();
        var cellsB = new HashSet<Vector3Int>();
        SplitCells(allFloorCells, floorA, centreA, centreB, cellsA, cellsB);

        // Paint into each room's tilemaps.
        // Skip cells that already have a tile (don't overwrite room floors).
        PaintFloor(floorA, cellsA, tiles);
        PaintFloor(floorB, cellsB, tiles);

        // Paint walls only around cells we own, using allFloorCells as the
        // "is floor" mask so we don't paint walls between the two halves.
        PaintWalls(wallsA, cellsA, allFloorCells, tiles);
        PaintWalls(wallsB, cellsB, allFloorCells, tiles);

        Debug.Log($"[HallwayTilemapPainter] Painted {cellsA.Count} cells into roomA, " +
                  $"{cellsB.Count} cells into roomB. Total={allFloorCells.Count}");
    }

    // ── Cell splitting ─────────────────────────────────────────────────────

    private static void SplitCells(
        HashSet<Vector3Int> all,
        Tilemap             floorA,
        Vector3             centreA,
        Vector3             centreB,
        HashSet<Vector3Int> cellsA,
        HashSet<Vector3Int> cellsB)
    {
        foreach (var cell in all)
        {
            // GetCellCenterWorld gives the true world-space centre of this cell
            // using floorA's tilemap (they share world space).
            Vector3 world = floorA.GetCellCenterWorld(cell);

            float dA = Vector3.Distance(world, centreA);
            float dB = Vector3.Distance(world, centreB);

            if (dA <= dB)
                cellsA.Add(cell);
            else
                cellsB.Add(cell);
        }
    }

    // ── Floor painting ─────────────────────────────────────────────────────

    private static void PaintFloor(
        Tilemap floor, HashSet<Vector3Int> cells, HallwayTileSet tiles)
    {
        foreach (var cell in cells)
        {
            // Don't overwrite existing room floor tiles
            if (floor.HasTile(cell)) continue;

            var tile = tiles.GetRandomFloorTile();
            if (tile != null) floor.SetTile(cell, tile);
        }
    }

    // ── Wall painting ──────────────────────────────────────────────────────

    private static void PaintWalls(
        Tilemap             walls,
        HashSet<Vector3Int> ownedCells,
        HashSet<Vector3Int> allFloorCells,
        HallwayTileSet      tiles)
    {
        if (tiles.WallTileSet == null || walls == null) return;

        // Candidate wall cells are the 8-neighbours of owned floor cells
        // that are not themselves floor cells.
        var candidates = new HashSet<Vector3Int>();
        foreach (var fc in ownedCells)
            foreach (var nb in Neighbours8(fc))
                if (!allFloorCells.Contains(nb))
                    candidates.Add(nb);

        foreach (var wc in candidates)
        {
            // Don't overwrite existing room wall tiles (preserves authored corners)
            if (walls.HasTile(wc)) continue;

            bool fN  = allFloorCells.Contains(wc + Vector3Int.up);
            bool fS  = allFloorCells.Contains(wc + Vector3Int.down);
            bool fE  = allFloorCells.Contains(wc + Vector3Int.right);
            bool fW  = allFloorCells.Contains(wc + Vector3Int.left);
            bool fNE = allFloorCells.Contains(wc + new Vector3Int( 1,  1, 0));
            bool fNW = allFloorCells.Contains(wc + new Vector3Int(-1,  1, 0));
            bool fSE = allFloorCells.Contains(wc + new Vector3Int( 1, -1, 0));
            bool fSW = allFloorCells.Contains(wc + new Vector3Int(-1, -1, 0));

            int cc = (fN?1:0) + (fS?1:0) + (fE?1:0) + (fW?1:0);
            var wt = ClassifyWall(fN, fS, fE, fW, fNE, fNW, fSE, fSW, cc);

            var sprite = tiles.WallTileSet.Get(wt);
            if (sprite != null) walls.SetTile(wc, MakeRuntimeTile(sprite));
        }
    }

    // ── Segment data ───────────────────────────────────────────────────────

    private struct Segment
    {
        public Vector3Int Start, End;
        public int        Width;
        public bool       Horizontal;
    }

    private static void CollectSegmentFloor(Segment seg, HashSet<Vector3Int> cells)
    {
        int half = seg.Width / 2;
        if (seg.Horizontal)
        {
            int xMin = Mathf.Min(seg.Start.x, seg.End.x);
            int xMax = Mathf.Max(seg.Start.x, seg.End.x);
            for (int x = xMin; x <= xMax; x++)
            for (int y = seg.Start.y - half; y <= seg.Start.y + half; y++)
                cells.Add(new Vector3Int(x, y, 0));
        }
        else
        {
            int yMin = Mathf.Min(seg.Start.y, seg.End.y);
            int yMax = Mathf.Max(seg.Start.y, seg.End.y);
            for (int y = yMin; y <= yMax; y++)
            for (int x = seg.Start.x - half; x <= seg.Start.x + half; x++)
                cells.Add(new Vector3Int(x, y, 0));
        }
    }

    private static void CollectJunctionFloor(
        Segment a, Segment b, HashSet<Vector3Int> cells)
    {
        var pivot = a.End;
        int ha = a.Width / 2, hb = b.Width / 2;
        int xMin = Mathf.Min(pivot.x - ha, pivot.x - hb);
        int xMax = Mathf.Max(pivot.x + ha, pivot.x + hb);
        int yMin = Mathf.Min(pivot.y - ha, pivot.y - hb);
        int yMax = Mathf.Max(pivot.y + ha, pivot.y + hb);
        for (int x = xMin; x <= xMax; x++)
        for (int y = yMin; y <= yMax; y++)
            cells.Add(new Vector3Int(x, y, 0));
    }

    // ── Segment path building ──────────────────────────────────────────────

    private static List<Segment> BuildSegments(
        Vector3Int               exitCell,
        Vector3Int               entryCell,
        LevelGenerator.Direction dirAtoB,
        int                      exitWidth,
        int                      entryWidth)
    {
        bool primaryH = dirAtoB == LevelGenerator.Direction.East
                     || dirAtoB == LevelGenerator.Direction.West;

        int dx       = entryCell.x - exitCell.x;
        int dy       = entryCell.y - exitCell.y;
        int midWidth = Mathf.Max(1, (exitWidth + entryWidth + 1) / 2);
        bool aligned = primaryH ? (dy == 0) : (dx == 0);

        if (aligned)
            return new List<Segment> { new() {
                Start      = exitCell,
                End        = entryCell,
                Width      = midWidth,
                Horizontal = primaryH } };

        var segs = new List<Segment>();

        if (primaryH)
        {
            int midX = entryCell.x;
            var c1   = new Vector3Int(midX, exitCell.y, 0);
            segs.Add(new Segment {
                Start = exitCell, End = c1,
                Width = exitWidth, Horizontal = true });

            if (Mathf.Abs(dy) <= exitWidth * 2)
            {
                segs.Add(new Segment {
                    Start = c1, End = entryCell,
                    Width = midWidth, Horizontal = false });
            }
            else
            {
                int halfY = exitCell.y + dy / 2;
                var c2    = new Vector3Int(midX,        halfY, 0);
                var c3    = new Vector3Int(entryCell.x, halfY, 0);
                segs.Add(new Segment { Start = c1, End = c2, Width = midWidth, Horizontal = false });
                segs.Add(new Segment { Start = c2, End = c3, Width = midWidth, Horizontal = true  });
                segs.Add(new Segment { Start = c3, End = entryCell, Width = entryWidth, Horizontal = false });
            }
        }
        else
        {
            int midY = entryCell.y;
            var c1   = new Vector3Int(exitCell.x, midY, 0);
            segs.Add(new Segment {
                Start = exitCell, End = c1,
                Width = exitWidth, Horizontal = false });

            if (Mathf.Abs(dx) <= exitWidth * 2)
            {
                segs.Add(new Segment {
                    Start = c1, End = entryCell,
                    Width = midWidth, Horizontal = true });
            }
            else
            {
                int halfX = exitCell.x + dx / 2;
                var c2    = new Vector3Int(halfX, midY,        0);
                var c3    = new Vector3Int(halfX, entryCell.y, 0);
                segs.Add(new Segment { Start = c1, End = c2, Width = midWidth, Horizontal = true  });
                segs.Add(new Segment { Start = c2, End = c3, Width = midWidth, Horizontal = false });
                segs.Add(new Segment { Start = c3, End = entryCell, Width = entryWidth, Horizontal = true });
            }
        }

        return segs;
    }

    // ── Wall classification ────────────────────────────────────────────────

    private static HallwayWallTileSet.WallType ClassifyWall(
        bool fN, bool fS, bool fE, bool fW,
        bool fNE, bool fNW, bool fSE, bool fSW, int cc)
    {
        if (cc == 1)
        {
            if (fN) return HallwayWallTileSet.WallType.EndCap_Bottom;
            if (fS) return HallwayWallTileSet.WallType.EndCap_Top;
            if (fE) return HallwayWallTileSet.WallType.EndCap_Left;
            if (fW) return HallwayWallTileSet.WallType.EndCap_Right;
        }
        if (fN && fS && !fE && !fW) return HallwayWallTileSet.WallType.Left;
        if (fE && fW && !fN && !fS) return HallwayWallTileSet.WallType.Top;
        if (fN && fE && fNE) return HallwayWallTileSet.WallType.BottomLeft_Concave;
        if (fN && fW && fNW) return HallwayWallTileSet.WallType.BottomRight_Concave;
        if (fS && fE && fSE) return HallwayWallTileSet.WallType.TopLeft_Concave;
        if (fS && fW && fSW) return HallwayWallTileSet.WallType.TopRight_Concave;
        if (fN && fE) return HallwayWallTileSet.WallType.BottomLeft_Convex;
        if (fN && fW) return HallwayWallTileSet.WallType.BottomRight_Convex;
        if (fS && fE) return HallwayWallTileSet.WallType.TopLeft_Convex;
        if (fS && fW) return HallwayWallTileSet.WallType.TopRight_Convex;
        if (fN || fS) return HallwayWallTileSet.WallType.Left;
        if (fE || fW) return HallwayWallTileSet.WallType.Top;
        return HallwayWallTileSet.WallType.Top;
    }

    // ── Runtime tile cache ─────────────────────────────────────────────────

    private static readonly Dictionary<Sprite, UnityEngine.Tilemaps.Tile> tileCache = new();

    private static UnityEngine.Tilemaps.Tile MakeRuntimeTile(Sprite sprite)
    {
        if (tileCache.TryGetValue(sprite, out var cached)) return cached;
        var t = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
        t.sprite = sprite;
        tileCache[sprite] = t;
        return t;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Vector3Int StepInward(
        Vector3Int cell, LevelGenerator.Direction dir, int steps)
    {
        Vector3Int delta = dir switch
        {
            LevelGenerator.Direction.North => Vector3Int.up,
            LevelGenerator.Direction.South => Vector3Int.down,
            LevelGenerator.Direction.East  => Vector3Int.right,
            LevelGenerator.Direction.West  => Vector3Int.left,
            _                              => Vector3Int.zero
        };
        return cell - delta * steps;
    }

    private static LevelGenerator.Direction Opposite(LevelGenerator.Direction d) => d switch
    {
        LevelGenerator.Direction.North => LevelGenerator.Direction.South,
        LevelGenerator.Direction.South => LevelGenerator.Direction.North,
        LevelGenerator.Direction.East  => LevelGenerator.Direction.West,
        LevelGenerator.Direction.West  => LevelGenerator.Direction.East,
        _                              => LevelGenerator.Direction.North
    };

    private static IEnumerable<Vector3Int> Neighbours8(Vector3Int c)
    {
        yield return c + Vector3Int.up;
        yield return c + Vector3Int.down;
        yield return c + Vector3Int.right;
        yield return c + Vector3Int.left;
        yield return c + new Vector3Int( 1,  1, 0);
        yield return c + new Vector3Int(-1,  1, 0);
        yield return c + new Vector3Int( 1, -1, 0);
        yield return c + new Vector3Int(-1, -1, 0);
    }
}
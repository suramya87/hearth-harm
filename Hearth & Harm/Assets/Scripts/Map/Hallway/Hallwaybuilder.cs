using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Paints hallway tiles directly into roomA and roomB's existing tilemaps,
/// then re-initializes both rooms' RoomGrids so the new tiles are walkable.
///
/// SEAMLESS GRID APPROACH:
///   Hallway tiles are painted INTO the rooms' own tilemaps. The hallway cells
///   become part of roomA's and roomB's RoomGrid. The player never switches grids.
///   Pathfinding, highlighting, and movement all work exactly like normal floor tiles.
///
/// SEAM WALL FIX:
///   ClearSeamWalls now clears walls at the mouth row AND one tile outward
///   (into the hallway), ensuring there is no invisible wall at the crossing.
///   The previous version cleared only inward, which could leave the door wall
///   intact.
///
/// REFRESH ORDER FIX:
///   RefreshRoomGrid is called AFTER painting, which re-scans HasTile so all
///   new hallway floor cells are valid grid positions. The cells dictionary in
///   TilemapRoomGrid is rebuilt from scratch to include hallway tiles.
/// </summary>
public static class HallwayBuilder
{
    public static void Build(
        LevelGenerator.PlacedRoom roomA,
        LevelGenerator.PlacedRoom roomB,
        LevelGenerator.Direction  dirAtoB,
        HallwayTileSet            tileSet)
    {
        if (tileSet == null || !tileSet.IsValid)
        {
            Debug.LogError("[HallwayBuilder] HallwayTileSet not assigned or invalid!");
            return;
        }

        if (roomA.roomGrid == null || roomB.roomGrid == null)
        {
            Debug.LogError("[HallwayBuilder] One or both rooms have null roomGrid!");
            return;
        }

        Tilemap floorA = roomA.roomGrid.GetFloorTilemap();
        Tilemap wallsA = roomA.roomGrid.GetWallsTilemap();
        Tilemap floorB = roomB.roomGrid.GetFloorTilemap();
        Tilemap wallsB = roomB.roomGrid.GetWallsTilemap();

        if (floorA == null || floorB == null)
        {
            Debug.LogError("[HallwayBuilder] Missing floor tilemap on a room.");
            return;
        }

        // ── 1. Scan door mouth widths and world positions ──────────────────
        var scannerA = GetOrAddScanner(roomA.roomInstance);
        var scannerB = GetOrAddScanner(roomB.roomInstance);
        scannerA.Scan();
        scannerB.Scan();

        LevelGenerator.Direction dirBtoA = Opposite(dirAtoB);
        int widthA = Mathf.Max(1, scannerA.GetMouthWidth(dirAtoB));
        int widthB = Mathf.Max(1, scannerB.GetMouthWidth(dirBtoA));

        Vector3 exitWorld = scannerA.HasDoor(dirAtoB)
            ? scannerA.GetMouthCentreWorld(dirAtoB)
            : roomA.connector?.GetConnectionPoint(dirAtoB)?.transform?.position
              ?? roomA.worldPosition;

        Vector3 entryWorld = scannerB.HasDoor(dirBtoA)
            ? scannerB.GetMouthCentreWorld(dirBtoA)
            : roomB.connector?.GetConnectionPoint(dirBtoA)?.transform?.position
              ?? roomB.worldPosition;

        // ── 2. Paint hallway tiles into roomA and roomB tilemaps ──────────
        HallwayTilemapPainter.PaintIntoRooms(
            floorA, wallsA, floorB, wallsB,
            exitWorld, entryWorld,
            widthA, widthB, dirAtoB, tileSet);

        // ── 3. Clear wall tiles at both door seams ────────────────────────
        // Clear walls at the mouth row AND one tile outward into the hallway
        // so there is no invisible wall blocking passage in either direction.
        ClearSeamWalls(wallsA, floorA, exitWorld,  widthA, dirAtoB);
        ClearSeamWalls(wallsB, floorB, entryWorld, widthB, dirBtoA);

        // ── 4. Re-initialize both room grids so new tiles are included ─────
        // Must happen AFTER painting so HasTile returns true for new cells.
        RefreshRoomGrid(roomA);
        RefreshRoomGrid(roomB);

        Debug.Log($"[HallwayBuilder] Hallway painted: {roomA.roomInstance.name} " +
                  $"{dirAtoB} ↔ {roomB.roomInstance.name}. " +
                  $"WidthA={widthA} WidthB={widthB}");
    }

    // ── Seam wall clearing ─────────────────────────────────────────────────

    /// <summary>
    /// Clears wall tiles at and around a door seam so there is no invisible
    /// collision blocking the player from crossing between room and hallway.
    ///
    /// Clears three rows:
    ///   - mouthCell row (the spawn-point / door row itself)
    ///   - one tile INWARD (into the room, away from the hallway direction)
    ///   - one tile OUTWARD (into the hallway, toward the door direction)
    ///
    /// This is more aggressive than strictly necessary but guarantees no wall
    /// tile is missed regardless of how the room was authored.
    /// </summary>
    private static void ClearSeamWalls(
        Tilemap walls, Tilemap floor,
        Vector3 mouthWorld, int width,
        LevelGenerator.Direction doorDir)
    {
        if (walls == null || floor == null) return;

        Vector3Int mouthCell = floor.WorldToCell(mouthWorld);

        // One tile inward (INTO the room, away from the hallway)
        Vector3Int inward    = StepInward(mouthCell, doorDir, 1);
        // One tile outward (INTO the hallway, in the door direction)
        Vector3Int outward   = StepOutward(mouthCell, doorDir, 1);

        int  half  = width / 2;
        bool horiz = doorDir == LevelGenerator.Direction.East
                  || doorDir == LevelGenerator.Direction.West;

        ClearRow(walls, mouthCell, horiz, half);
        ClearRow(walls, inward,    horiz, half);
        ClearRow(walls, outward,   horiz, half);
    }

    private static void ClearRow(Tilemap walls, Vector3Int centre, bool horizontal, int half)
    {
        for (int offset = -half; offset <= half; offset++)
        {
            Vector3Int cell = horizontal
                ? new Vector3Int(centre.x, centre.y + offset, 0)
                : new Vector3Int(centre.x + offset, centre.y, 0);

            if (walls.HasTile(cell))
                walls.SetTile(cell, null);
        }
    }

    // ── Room grid refresh ──────────────────────────────────────────────────

    /// <summary>
    /// Re-initializes the TilemapRoomGrid and RoomGrid after hallway tiles
    /// have been painted in. This re-scans HasTile so all new floor cells
    /// are registered as valid grid positions.
    ///
    /// IMPORTANT: call this AFTER HallwayTilemapPainter.PaintIntoRooms()
    /// and AFTER ClearSeamWalls(), otherwise the re-scan will miss tiles or
    /// include cleared wall cells.
    /// </summary>
    private static void RefreshRoomGrid(LevelGenerator.PlacedRoom room)
    {
        if (room?.roomGrid == null) return;

        var trg = room.roomInstance.GetComponent<TilemapRoomGrid>();
        if (trg == null)
        {
            Debug.LogError($"[HallwayBuilder] No TilemapRoomGrid on '{room.roomInstance.name}'");
            return;
        }

        Tilemap floor = room.roomGrid.GetFloorTilemap();
        Tilemap walls = room.roomGrid.GetWallsTilemap();

        if (floor == null)
        {
            Debug.LogError($"[HallwayBuilder] No floor tilemap on '{room.roomInstance.name}'");
            return;
        }

        // Re-initialize re-scans HasTile so all new hallway floor cells
        // are added to the valid cell set. Both TilemapRoomGrid and RoomGrid
        // must be refreshed so the delegate chain stays consistent.
        trg.Initialize(walls, floor);
        room.roomGrid.Initialize(walls, floor);

        Debug.Log($"[HallwayBuilder] Refreshed '{room.roomInstance.name}' — " +
                  $"floor tiles: {CountTiles(floor)}");
    }

    private static int CountTiles(Tilemap tm)
    {
        if (tm == null) return 0;
        int n = 0;
        foreach (var p in tm.cellBounds.allPositionsWithin)
            if (tm.HasTile(p)) n++;
        return n;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static HallwaySpawnPointScanner GetOrAddScanner(GameObject go)
    {
        var s = go.GetComponent<HallwaySpawnPointScanner>();
        return s ?? go.AddComponent<HallwaySpawnPointScanner>();
    }

    /// <summary>Steps a cell away from the door direction (into the room).</summary>
    private static Vector3Int StepInward(
        Vector3Int cell, LevelGenerator.Direction dir, int steps)
    {
        Vector3Int delta = DirDelta(dir);
        return cell - delta * steps;   // opposite of door direction = into room
    }

    /// <summary>Steps a cell in the door direction (into the hallway).</summary>
    private static Vector3Int StepOutward(
        Vector3Int cell, LevelGenerator.Direction dir, int steps)
    {
        Vector3Int delta = DirDelta(dir);
        return cell + delta * steps;   // same as door direction = into hallway
    }

    private static Vector3Int DirDelta(LevelGenerator.Direction dir) => dir switch
    {
        LevelGenerator.Direction.North => Vector3Int.up,
        LevelGenerator.Direction.South => Vector3Int.down,
        LevelGenerator.Direction.East  => Vector3Int.right,
        LevelGenerator.Direction.West  => Vector3Int.left,
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
}
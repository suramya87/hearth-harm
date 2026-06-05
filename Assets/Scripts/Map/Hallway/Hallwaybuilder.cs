using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Orchestrates building one procedural hallway between two PlacedRooms.
///
/// Trigger layout per hallway:
///
///   [Room A] --WalkTrigger_FromA-- [hallway tiles] --WalkTrigger_FromB-- [Room B]
///                 |                                           |
///            Trigger_ToRoomA                          Trigger_ToRoomB
///
///   WalkTriggers  — swap the player's currentRoomGrid to the hallway grid
///                   so MoveAction pathfinds on hallway tiles.
///   EntryTriggers — at the far mouth, transition the player into the destination
///                   room, spawn enemies, and lock doors.
///
/// PAIRED TRIGGERS:
///   Each mouth has a WalkTrigger and an EntryTrigger.
///   HallwayBuilder wires them together via SetPairedWalkTrigger() so that:
///     • When the player enters a room, the walk trigger at THAT mouth is
///       immediately locked (no rubber-band back into the hallway).
///     • When enemies are present the walk triggers at ALL room mouths are
///       locked (and optional door-strip objects are shown as barriers).
///
/// HALLWAY TILES STOP BEFORE SPAWN TILES:
///   The painter is told to trim `trimTiles` cells from each mouth so that
///   hallway floor tiles never overwrite the room's SpawnPoint tiles.
///   trimTiles = 1 by default (one tile gap at each end).
/// </summary>
public static class HallwayBuilder
{
    /// <summary>
    /// How many tiles to leave clear between the hallway floor and each room's
    /// spawn-point row/column. Set to 1 so hallway tiles never overlap spawn tiles.
    /// Increase if your room has a thick doorstep you want to preserve.
    /// </summary>
    private const int TrimTiles = 1;

    /// <summary>
    /// Build a complete hallway between roomA and roomB.
    /// Returns the HallwayGrid, or null on failure.
    /// </summary>
    public static HallwayGrid Build(
        LevelGenerator.PlacedRoom roomA,
        LevelGenerator.PlacedRoom roomB,
        LevelGenerator.Direction  dirAtoB,
        Transform                 parent,
        HallwayTileSet            tileSet,
        float                     cellSize     = 1f,
        int                       defaultWidth = 3)
    {
        if (tileSet == null || tileSet.FloorTile == null)
        {
            Debug.LogError("[HallwayBuilder] HallwayTileSet not assigned or FloorTile missing!");
            return null;
        }

        // ── 1. Scan spawn point widths ─────────────────────────────────────
        var scannerA = GetOrAddScanner(roomA.roomInstance);
        var scannerB = GetOrAddScanner(roomB.roomInstance);
        scannerA.Scan();
        scannerB.Scan();

        LevelGenerator.Direction dirBtoA = Opposite(dirAtoB);

        int widthA = scannerA.GetMouthWidth(dirAtoB) > 0
            ? scannerA.GetMouthWidth(dirAtoB)
            : Mathf.Max(1, defaultWidth);
        int widthB = scannerB.GetMouthWidth(dirBtoA) > 0
            ? scannerB.GetMouthWidth(dirBtoA)
            : Mathf.Max(1, defaultWidth);

        // ── 2. Get world mouth centres ─────────────────────────────────────
        Vector3 exitWorld = scannerA.HasDoor(dirAtoB)
            ? scannerA.GetMouthCentreWorld(dirAtoB)
            : (roomA.connector.GetConnectionPoint(dirAtoB)?.transform?.position
               ?? roomA.worldPosition);

        Vector3 entryWorld = scannerB.HasDoor(dirBtoA)
            ? scannerB.GetMouthCentreWorld(dirBtoA)
            : (roomB.connector.GetConnectionPoint(dirBtoA)?.transform?.position
               ?? roomB.worldPosition);

        // ── 3. Create HallwayGrid ──────────────────────────────────────────
        string      name    = $"Hallway_{roomA.roomInstance.name}_{dirAtoB}";
        HallwayGrid hallway = HallwayGrid.Create(parent, roomA, roomB, dirAtoB, name);

        // ── 4. Paint tiles (trimmed so hallway stops before spawn tiles) ───
        HallwayTilemapPainter.Paint(
            hallway,
            exitWorld,
            entryWorld,
            widthA,
            widthB,
            dirAtoB,
            tileSet,
            trimTiles: TrimTiles);      // ← new: leave a gap at each mouth

        // ── 5. Initialize RoomGrid (must happen AFTER tiles are painted) ───
        hallway.Initialize();

        if (!hallway.IsReady)
        {
            Debug.LogError($"[HallwayBuilder] HallwayGrid failed to initialize for {name}.");
            UnityEngine.Object.Destroy(hallway.gameObject);
            return null;
        }

        // ── 6. Walk triggers ───────────────────────────────────────────────
        HallwayWalkTrigger walkFromA = AddWalkTrigger(
            hallway, exitWorld,  widthA, cellSize, dirAtoB,  "WalkTrigger_FromA");

        HallwayWalkTrigger walkFromB = AddWalkTrigger(
            hallway, entryWorld, widthB, cellSize, dirBtoA,  "WalkTrigger_FromB");

        // ── 7. Entry triggers ──────────────────────────────────────────────
        HallwayEntryTrigger entryToA = AddEntryTrigger(
            hallway, exitWorld,  widthA, cellSize,
            destinationRoom: roomA,
            entryDirection:  dirAtoB,
            travelDirection: dirBtoA,
            name: "Trigger_ToRoomA");

        HallwayEntryTrigger entryToB = AddEntryTrigger(
            hallway, entryWorld, widthB, cellSize,
            destinationRoom: roomB,
            entryDirection:  dirBtoA,
            travelDirection: dirAtoB,
            name: "Trigger_ToRoomB");

        // ── 8. Wire paired triggers ────────────────────────────────────────
        // Trigger_ToRoomA sits at Room A's mouth. The walk trigger at the same
        // mouth is WalkTrigger_FromA (the one that sends the player INTO the
        // hallway when leaving room A).
        // When the player arrives back at room A via Trigger_ToRoomA we lock
        // WalkTrigger_FromA so they can't be rubber-banded back out.
        if (entryToA != null && walkFromA != null) entryToA.SetPairedWalkTrigger(walkFromA);
        if (entryToB != null && walkFromB != null) entryToB.SetPairedWalkTrigger(walkFromB);

        Debug.Log($"[HallwayBuilder] Built {name}. " +
                  $"WidthA={widthA} WidthB={widthB} " +
                  $"Exit={exitWorld} Entry={entryWorld}");

        return hallway;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static HallwaySpawnPointScanner GetOrAddScanner(UnityEngine.GameObject go)
    {
        var s = go.GetComponent<HallwaySpawnPointScanner>();
        if (s == null) s = go.AddComponent<HallwaySpawnPointScanner>();
        return s;
    }


    /// Shallow trigger at a hallway mouth that hands the player off to the
    /// hallway's RoomGrid.
    /// Returns the created HallwayWalkTrigger component.
    /// </summary>
    private static HallwayWalkTrigger AddWalkTrigger(
        HallwayGrid              hallway,
        Vector3                  mouthWorldPos,
        int                      mouthWidthTiles,
        float                    cellSize,
        LevelGenerator.Direction travelDirection,
        string                   name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(hallway.transform, worldPositionStays: false);
        go.transform.position = mouthWorldPos;

        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;

        bool travelHorizontal = travelDirection == LevelGenerator.Direction.East
                             || travelDirection == LevelGenerator.Direction.West;

        float w = travelHorizontal ? cellSize * 2f              : mouthWidthTiles * cellSize;
        float h = travelHorizontal ? mouthWidthTiles * cellSize : cellSize * 2f;
        col.size = new Vector2(w, h);

        var trigger = go.AddComponent<HallwayWalkTrigger>();
        trigger.Initialize(hallway);
        return trigger;
    }

    /// <summary>
    /// Trigger at the far end of a hallway that fires when the player arrives
    /// at the destination room mouth.
    /// Returns the created HallwayEntryTrigger component.
    /// </summary>
    private static HallwayEntryTrigger AddEntryTrigger(
        HallwayGrid                  hallway,
        Vector3                      mouthWorldPos,
        int                          mouthWidthTiles,
        float                        cellSize,
        LevelGenerator.PlacedRoom    destinationRoom,
        LevelGenerator.Direction     entryDirection,
        LevelGenerator.Direction     travelDirection,
        string                       name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(hallway.transform, worldPositionStays: false);
        go.transform.position = mouthWorldPos;
        go.tag = "HallwayTrigger";

        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;

        bool travelHorizontal = travelDirection == LevelGenerator.Direction.East
                             || travelDirection == LevelGenerator.Direction.West;

        float w = travelHorizontal ? cellSize * 1.5f            : mouthWidthTiles * cellSize;
        float h = travelHorizontal ? mouthWidthTiles * cellSize : cellSize * 1.5f;
        col.size = new Vector2(w, h);

        var trigger = go.AddComponent<HallwayEntryTrigger>();
        trigger.Initialize(hallway, destinationRoom, entryDirection);
        return trigger;
    }

    private static LevelGenerator.Direction Opposite(LevelGenerator.Direction d) => d switch
    {
        LevelGenerator.Direction.North => LevelGenerator.Direction.South,
        LevelGenerator.Direction.South => LevelGenerator.Direction.North,
        LevelGenerator.Direction.East  => LevelGenerator.Direction.West,
        LevelGenerator.Direction.West  => LevelGenerator.Direction.East,
        _                              => LevelGenerator.Direction.North
    };
}
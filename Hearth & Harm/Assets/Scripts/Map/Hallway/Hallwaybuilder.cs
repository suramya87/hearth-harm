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
/// Each mouth therefore has TWO overlapping triggers:
///   • a WalkTrigger  (shallow, fires on entering the hallway)
///   • an EntryTrigger (fires when arriving at the destination end)
/// </summary>
public static class HallwayBuilder
{
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
        float                     cellSize = 1f)
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

        int widthA = Mathf.Max(1, scannerA.GetMouthWidth(dirAtoB));
        int widthB = Mathf.Max(1, scannerB.GetMouthWidth(dirBtoA));

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
        string name = $"Hallway_{roomA.roomInstance.name}_{dirAtoB}";
        HallwayGrid hallway = HallwayGrid.Create(parent, roomA, roomB, dirAtoB, name);

        // ── 4. Paint tiles ─────────────────────────────────────────────────
        HallwayTilemapPainter.Paint(
            hallway,
            exitWorld,
            entryWorld,
            widthA,
            widthB,
            dirAtoB,
            tileSet);

        // ── 5. Initialize RoomGrid (must happen AFTER tiles are painted) ───
        hallway.Initialize();

        if (!hallway.IsReady)
        {
            Debug.LogError($"[HallwayBuilder] HallwayGrid failed to initialize for {name}.");
            UnityEngine.Object.Destroy(hallway.gameObject);
            return null;
        }

        // ── 6. Walk triggers — swap player onto hallway grid on entry ──────
        // Sits at room A's mouth so the player is handed to the hallway
        // as soon as they step off room A's tiles.
        AddWalkTrigger(hallway, exitWorld,  widthA, cellSize, "WalkTrigger_FromA");

        // Sits at room B's mouth so the player is handed to the hallway
        // when they come back through from room B.
        AddWalkTrigger(hallway, entryWorld, widthB, cellSize, "WalkTrigger_FromB");

        // ── 7. Entry triggers — transition into destination room ───────────
        // Trigger_ToRoomA: at room A's mouth, player arriving back at A.
        // EntryDirection = dirAtoB because they're entering room A
        // from the direction that leads toward B.
        AddEntryTrigger(hallway, exitWorld,  widthA, cellSize,
            destinationRoom: roomA,
            entryDirection:  dirAtoB,
            name: "Trigger_ToRoomA");

        // Trigger_ToRoomB: at room B's mouth, player arriving at B.
        // EntryDirection = dirBtoA because they're entering room B
        // from the direction that leads back toward A.
        AddEntryTrigger(hallway, entryWorld, widthB, cellSize,
            destinationRoom: roomB,
            entryDirection:  dirBtoA,
            name: "Trigger_ToRoomB");

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

    /// <summary>
    /// Shallow trigger at a hallway mouth that hands the player off to the
    /// hallway's RoomGrid so they can walk on hallway tiles.
    /// </summary>
    private static void AddWalkTrigger(
        HallwayGrid hallway,
        Vector3     mouthWorldPos,
        int         mouthWidthTiles,
        float       cellSize,
        string      name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(hallway.transform, worldPositionStays: false);
        go.transform.position = mouthWorldPos;

        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;

        // 1.5 tiles deep so it catches the player before they fully step off
        // the room tiles — avoids a gap where neither grid owns them.
        col.size = new Vector2(mouthWidthTiles * cellSize, cellSize * 1.5f);

        var trigger = go.AddComponent<HallwayWalkTrigger>();
        trigger.Initialize(hallway);
    }

    /// <summary>
    /// Deep trigger at the far end of the hallway that fires when the player
    /// arrives at the destination room mouth and transitions them in.
    /// </summary>
    private static void AddEntryTrigger(
        HallwayGrid                  hallway,
        Vector3                      mouthWorldPos,
        int                          mouthWidthTiles,
        float                        cellSize,
        LevelGenerator.PlacedRoom    destinationRoom,
        LevelGenerator.Direction     entryDirection,
        string                       name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(hallway.transform, worldPositionStays: false);
        go.transform.position = mouthWorldPos;
        go.tag = "HallwayTrigger";

        var col = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;

        // Size the collider to the door mouth width × 1 tile tall
        bool horizontal = entryDirection == LevelGenerator.Direction.North
                       || entryDirection == LevelGenerator.Direction.South;
        float w = horizontal ? mouthWidthTiles * cellSize : cellSize;
        float h = horizontal ? cellSize                   : mouthWidthTiles * cellSize;
        col.size = new Vector2(w, h);

        var trigger = go.AddComponent<HallwayEntryTrigger>();
        trigger.Initialize(hallway, destinationRoom, entryDirection);
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
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
///
/// COLLIDER ORIENTATION FIX:
///   Trigger width/height is based on the travel axis, not the entry direction.
///   A North-facing door has a horizontal mouth → wide trigger, shallow depth.
///   A East-facing door has a vertical mouth   → tall trigger, shallow depth.
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

        // Use SpawnPointTile count as width; fall back to defaultWidth if the
        // room has no spawn tiles on this door.
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

        // ── 6. Walk triggers ───────────────────────────────────────────────
        // WalkTrigger_FromA — at room A's mouth, player steps into hallway going toward B.
        // Travel direction is dirAtoB, so we size based on that axis.
        AddWalkTrigger(hallway, exitWorld,  widthA, cellSize, dirAtoB,  "WalkTrigger_FromA");

        // WalkTrigger_FromB — at room B's mouth, player steps into hallway coming back.
        // Travel direction back is dirBtoA.
        AddWalkTrigger(hallway, entryWorld, widthB, cellSize, dirBtoA,  "WalkTrigger_FromB");

        // ── 7. Entry triggers ──────────────────────────────────────────────
        // Trigger_ToRoomA: player has walked back to room A's end of the hallway.
        // They are entering room A, arriving from the direction of B (dirAtoB).
        // Travel direction at this mouth is dirBtoA (they walked back toward A).
        AddEntryTrigger(hallway, exitWorld,  widthA, cellSize,
            destinationRoom: roomA,
            entryDirection:  dirAtoB,   // direction used to find spawn point in room A
            travelDirection: dirBtoA,   // which axis the corridor runs at this mouth
            name: "Trigger_ToRoomA");

        // Trigger_ToRoomB: player has walked to room B's end of the hallway.
        // They are entering room B, arriving from the direction of A (dirBtoA).
        // Travel direction at this mouth is dirAtoB (they walked toward B).
        AddEntryTrigger(hallway, entryWorld, widthB, cellSize,
            destinationRoom: roomB,
            entryDirection:  dirBtoA,   // direction used to find spawn point in room B
            travelDirection: dirAtoB,   // which axis the corridor runs at this mouth
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
    ///
    /// travelDirection — the direction the player is moving when they hit this trigger.
    /// Used to correctly orient the collider (wide for horizontal travel, tall for vertical).
    /// </summary>
    private static void AddWalkTrigger(
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

        // Travel is horizontal (East/West) → mouth is vertical (tall), depth is shallow (wide)
        // Travel is vertical (North/South)  → mouth is horizontal (wide), depth is shallow (tall)
        bool travelHorizontal = travelDirection == LevelGenerator.Direction.East
                             || travelDirection == LevelGenerator.Direction.West;

        // Width  = across the mouth opening
        // Height = depth into the hallway (shallow — 2 tiles so player is caught in time)
        float w = travelHorizontal ? cellSize * 2f              : mouthWidthTiles * cellSize;
        float h = travelHorizontal ? mouthWidthTiles * cellSize : cellSize * 2f;
        col.size = new Vector2(w, h);

        var trigger = go.AddComponent<HallwayWalkTrigger>();
        trigger.Initialize(hallway);
    }

    /// <summary>
    /// Trigger at the far end of a hallway that fires when the player arrives
    /// at the destination room mouth and transitions them in.
    ///
    /// entryDirection  — direction used to look up the spawn point in the destination room.
    /// travelDirection — the direction the player is moving when they hit this trigger.
    ///                   Used to correctly orient the collider.
    /// </summary>
    private static void AddEntryTrigger(
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

        // Same orientation logic as WalkTrigger —
        // size the mouth opening wide and keep depth shallow.
        bool travelHorizontal = travelDirection == LevelGenerator.Direction.East
                             || travelDirection == LevelGenerator.Direction.West;

        float w = travelHorizontal ? cellSize * 1.5f            : mouthWidthTiles * cellSize;
        float h = travelHorizontal ? mouthWidthTiles * cellSize : cellSize * 1.5f;
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
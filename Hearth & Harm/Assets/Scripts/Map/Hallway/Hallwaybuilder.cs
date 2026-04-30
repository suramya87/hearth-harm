using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Orchestrates building one procedural hallway between two PlacedRooms.
///
/// Called by LevelGenerator.PaintHallways() for every connection pair.
/// Creates the HallwayGrid, runs HallwayTilemapPainter, then attaches
/// HallwayEntryTrigger colliders at both mouths.
///
/// TRIGGER SIZING
///   Each trigger is a BoxCollider2D sized to the door mouth width × 1 tile tall,
///   centred exactly on the door mouth in world space.
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

        // ── 2. Get world positions of connection points ────────────────────
        // Use the scanner's computed mouth centre (average of all spawn tiles).
        // Fall back to the RoomConnector transform if no spawn tiles found.
        Vector3 exitWorld  = scannerA.HasDoor(dirAtoB)
            ? scannerA.GetMouthCentreWorld(dirAtoB)
            : (roomA.connector.GetConnectionPoint(dirAtoB)?.transform?.position ?? roomA.worldPosition);

        Vector3 entryWorld = scannerB.HasDoor(dirBtoA)
            ? scannerB.GetMouthCentreWorld(dirBtoA)
            : (roomB.connector.GetConnectionPoint(dirBtoA)?.transform?.position ?? roomB.worldPosition);

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
            Object.Destroy(hallway.gameObject);
            return null;
        }

        // ── 6. Place entry triggers ────────────────────────────────────────
        // Trigger A: sits at roomA's door mouth → transitions player INTO roomA when returning
        AddEntryTrigger(hallway, exitWorld,  widthA, cellSize,
                        destinationRoom: roomA,
                        entryDirection:  dirBtoA,   // player enters roomA from B's side
                        name: "Trigger_ToRoomA");

        // Trigger B: sits at roomB's door mouth → transitions player INTO roomB when going forward
        AddEntryTrigger(hallway, entryWorld, widthB, cellSize,
                        destinationRoom: roomB,
                        entryDirection:  dirBtoA,   // player enters roomB from A's side (opposite)
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
        go.tag = "HallwayTrigger"; // optional — set up tag in Unity if you want to query these

        // BoxCollider2D sized to the door mouth
        var col     = go.AddComponent<BoxCollider2D>();
        col.isTrigger = true;

        bool horizontal = entryDirection == LevelGenerator.Direction.North
                       || entryDirection == LevelGenerator.Direction.South;

        // Width covers all mouth tiles; height is 1 tile so it only fires at the threshold
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
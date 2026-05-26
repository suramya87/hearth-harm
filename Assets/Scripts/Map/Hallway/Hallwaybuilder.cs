using UnityEngine;
using UnityEngine.Tilemaps;

public static class HallwayBuilder
{
    private const int TrimTiles = 1;

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

        Vector3 exitWorld = scannerA.HasDoor(dirAtoB)
            ? scannerA.GetMouthCentreWorld(dirAtoB)
            : (roomA.connector.GetConnectionPoint(dirAtoB)?.transform?.position
               ?? roomA.worldPosition);

        Vector3 entryWorld = scannerB.HasDoor(dirBtoA)
            ? scannerB.GetMouthCentreWorld(dirBtoA)
            : (roomB.connector.GetConnectionPoint(dirBtoA)?.transform?.position
               ?? roomB.worldPosition);

        string      name    = $"Hallway_{roomA.roomInstance.name}_{dirAtoB}";
        HallwayGrid hallway = HallwayGrid.Create(parent, roomA, roomB, dirAtoB, name);

        HallwayTilemapPainter.Paint(
            hallway,
            exitWorld,
            entryWorld,
            widthA,
            widthB,
            dirAtoB,
            tileSet,
            trimTiles: TrimTiles);      

        hallway.Initialize();

        if (!hallway.IsReady)
        {
            Debug.LogError($"[HallwayBuilder] HallwayGrid failed to initialize for {name}.");
            UnityEngine.Object.Destroy(hallway.gameObject);
            return null;
        }

        HallwayWalkTrigger walkFromA = AddWalkTrigger(
            hallway, exitWorld,  widthA, cellSize, dirAtoB,  "WalkTrigger_FromA");

        HallwayWalkTrigger walkFromB = AddWalkTrigger(
            hallway, entryWorld, widthB, cellSize, dirBtoA,  "WalkTrigger_FromB");

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

        if (entryToA != null && walkFromA != null) entryToA.SetPairedWalkTrigger(walkFromA);
        if (entryToB != null && walkFromB != null) entryToB.SetPairedWalkTrigger(walkFromB);

        Debug.Log($"[HallwayBuilder] Built {name}. " +
                  $"WidthA={widthA} WidthB={widthB} " +
                  $"Exit={exitWorld} Entry={entryWorld}");

        return hallway;
    }


    private static HallwaySpawnPointScanner GetOrAddScanner(UnityEngine.GameObject go)
    {
        var s = go.GetComponent<HallwaySpawnPointScanner>();
        if (s == null) s = go.AddComponent<HallwaySpawnPointScanner>();
        return s;
    }


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
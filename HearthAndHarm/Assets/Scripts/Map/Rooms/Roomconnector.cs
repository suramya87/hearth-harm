using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Marks connection points and door strips on a room prefab.
///
/// DOOR BLOCKING:
///   Call InitBlockers(roomGrid) after the room's RoomGrid is initialized.
///   This wires all DoorStripBlocker components on the door strips so they
///   automatically register/unregister wall cells in UnifiedWorldGrid
///   whenever SetDoorOpen() is called.
/// </summary>
public class RoomConnector : MonoBehaviour
{
    [System.Serializable]
    public class ConnectionPoint
    {
        public Transform                 transform;
        public LevelGenerator.Direction  direction;
        public bool                      isConnected;
    }

    [Header("Connection Points")]
    public ConnectionPoint northConnection;
    public ConnectionPoint southConnection;
    public ConnectionPoint eastConnection;
    public ConnectionPoint westConnection;

    [Header("Door Strips (active = wall/closed, inactive = open)")]
    public GameObject northDoorStrip;
    public GameObject southDoorStrip;
    public GameObject eastDoorStrip;
    public GameObject westDoorStrip;

    // Directions that are permanent dead-end walls.
    private readonly HashSet<LevelGenerator.Direction> deadEnds = new();

    // ── Blocker initialisation ─────────────────────────────────────────────

    /// <summary>
    /// Call this after the room's RoomGrid is ready (i.e. after
    /// RoomTilemapSetup.Initialize() and RegisterAllTilemaps() have run).
    /// Gives every DoorStripBlocker its owning RoomGrid reference.
    /// </summary>
    public void InitBlockers(RoomGrid grid)
    {
        InitStrip(northDoorStrip, grid);
        InitStrip(southDoorStrip, grid);
        InitStrip(eastDoorStrip,  grid);
        InitStrip(westDoorStrip,  grid);
    }

    private static void InitStrip(GameObject strip, RoomGrid grid)
    {
        if (strip == null) return;
        // Add the component automatically if the designer forgot.
        var blocker = strip.GetComponent<DoorStripBlocker>()
                   ?? strip.AddComponent<DoorStripBlocker>();
        blocker.SetOwnerGrid(grid);
    }

    // ── API ────────────────────────────────────────────────────────────────

    public void PermanentClose(LevelGenerator.Direction dir)
    {
        deadEnds.Add(dir);
        SetDoorOpen(dir, false);
    }

    public void SetDoorOpen(LevelGenerator.Direction dir, bool open)
    {
        if (deadEnds.Contains(dir) && open) return;

        var strip = GetStrip(dir);
        if (strip != null) strip.SetActive(!open);
    }

    public void CloseAllDoors()
    {
        foreach (LevelGenerator.Direction d in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            SetDoorOpen(d, false);
    }

    public void CloseConnectedDoors(System.Collections.Generic.IEnumerable<LevelGenerator.Direction> connectedDirs)
    {
        foreach (var dir in connectedDirs)
        {
            var strip = GetStrip(dir);
            if (strip != null) strip.SetActive(true); // true = closed/visible
        }
    }

    public void OpenAllDoors()
    {
        foreach (LevelGenerator.Direction d in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            SetDoorOpen(d, true);
    }

    public void OpenConnectedDoors(System.Collections.Generic.IEnumerable<LevelGenerator.Direction> connectedDirs)
    {
        foreach (var dir in connectedDirs)
        {
            if (deadEnds.Contains(dir)) continue;
            var strip = GetStrip(dir);
            if (strip != null) strip.SetActive(false); // false = open/hidden
        }
    }

    public ConnectionPoint GetConnectionPoint(LevelGenerator.Direction dir) => dir switch
    {
        LevelGenerator.Direction.North => northConnection,
        LevelGenerator.Direction.South => southConnection,
        LevelGenerator.Direction.East  => eastConnection,
        LevelGenerator.Direction.West  => westConnection,
        _                              => null
    };

    public bool HasConnectionPoint(LevelGenerator.Direction dir)
    {
        var p = GetConnectionPoint(dir);
        return p != null && p.transform != null;
    }

    public bool IsDirectionAvailable(LevelGenerator.Direction dir)
    {
        var p = GetConnectionPoint(dir);
        return p != null && p.transform != null && !p.isConnected;
    }

    public void MarkConnectionUsed(LevelGenerator.Direction dir)
    {
        var p = GetConnectionPoint(dir);
        if (p != null) p.isConnected = true;
    }

    private GameObject GetStrip(LevelGenerator.Direction dir) => dir switch
    {
        LevelGenerator.Direction.North => northDoorStrip,
        LevelGenerator.Direction.South => southDoorStrip,
        LevelGenerator.Direction.East  => eastDoorStrip,
        LevelGenerator.Direction.West  => westDoorStrip,
        _                              => null
    };

    private void OnDrawGizmos()
    {
        Draw(northConnection, Color.blue);
        Draw(southConnection, Color.red);
        Draw(eastConnection,  Color.green);
        Draw(westConnection,  Color.yellow);
    }

    private static void Draw(ConnectionPoint p, Color c)
    {
        if (p?.transform == null) return;
        Gizmos.color = p.isConnected ? Color.gray : c;
        Gizmos.DrawSphere(p.transform.position, 0.2f);
        Gizmos.DrawLine(p.transform.position, p.transform.position + p.transform.up * 0.8f);
    }
}
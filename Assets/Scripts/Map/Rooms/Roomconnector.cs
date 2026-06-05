using UnityEngine;
using System.Collections.Generic; // Added this to fix the HashSet error
using System.Linq;                // Optional, but good practice if using Enums as lists

/// <summary>
/// Marks connection points and door strips on a room prefab.
/// </summary>
public class RoomConnector : MonoBehaviour
{
    [System.Serializable]
    public class ConnectionPoint
    {
        public Transform transform;
        public LevelGenerator.Direction direction;
        public bool isConnected;
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

    // ── API ────────────────────────────────────────────────────────────────

    // This HashSet tracks directions that are permanent walls (dead ends)
    private HashSet<LevelGenerator.Direction> deadEnds = new HashSet<LevelGenerator.Direction>();

    public void PermanentClose(LevelGenerator.Direction dir)
    {
        deadEnds.Add(dir);
        SetDoorOpen(dir, false);
    }

    public void SetDoorOpen(LevelGenerator.Direction dir, bool open)
    {
        // If the direction is a permanent dead end, block any logic trying to open it
        if (deadEnds.Contains(dir) && open) return; 

        var strip = GetStrip(dir);
        if (strip != null) strip.SetActive(!open);
    }

    public void CloseAllDoors()
    {
        foreach (LevelGenerator.Direction d in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            SetDoorOpen(d, false);
    }

    public void OpenAllDoors()
    {
        foreach (LevelGenerator.Direction d in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            SetDoorOpen(d, true);
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
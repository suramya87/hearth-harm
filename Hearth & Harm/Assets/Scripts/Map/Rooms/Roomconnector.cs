using UnityEngine;

/// <summary>
/// Marks connection points and door strips on a room prefab.
/// Unchanged from 3D version except for the RoomType enum being co-located here.
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

    // ── API ────────────────────────────────────────────────────────────────

    public void SetDoorOpen(LevelGenerator.Direction dir, bool open)
    {
        var strip = GetStrip(dir);
        if (strip != null) strip.SetActive(!open);
    }

    public void CloseAllDoors()
    {
        foreach (LevelGenerator.Direction d in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            SetDoorOpen(d, false);
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
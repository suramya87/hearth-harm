using UnityEngine;

/// <summary>
/// Placed in room prefabs. Handles click-based room transition.
/// In multiplayer, routes through NetworkedPlayerBridge so all peers
/// see the room change and RoomManager is updated everywhere.
/// </summary>
public class RoomDoor : MonoBehaviour
{
    private LevelGenerator.PlacedRoom ownerRoom;
    private LevelGenerator.PlacedRoom connectedRoom;
    private LevelGenerator            gen;
    private LevelGenerator.Direction  doorDir;
    private bool                      ready;

    public void Initialize(LevelGenerator.PlacedRoom owner)
    {
        ownerRoom     = owner;
        gen           = FindAnyObjectByType<LevelGenerator>();
        doorDir       = DetermineDirection(owner);
        connectedRoom = gen?.GetConnectedRoom(owner, doorDir);
        ready         = connectedRoom != null;
    }

    private LevelGenerator.Direction DetermineDirection(LevelGenerator.PlacedRoom owner)
    {
        var local = owner.roomInstance.transform.InverseTransformPoint(transform.position);
        return Mathf.Abs(local.x) > Mathf.Abs(local.y)
            ? (local.x > 0 ? LevelGenerator.Direction.East  : LevelGenerator.Direction.West)
            : (local.y > 0 ? LevelGenerator.Direction.North : LevelGenerator.Direction.South);
    }

    private void OnMouseDown()
    {
        if (!ready) return;

        var player = FindAnyObjectByType<Unit>();
        if (player == null) return;

        var entryDir = gen.GetOppositeDirection(doorDir);
        var reader   = connectedRoom.roomInstance.GetComponent<RoomSpawnPointReader>();
        var spawnPos = reader != null && reader.HasSpawnPoint(entryDir)
            ? reader.GetSpawnPosition(entryDir, connectedRoom.roomGrid)
            : new GridPosition(connectedRoom.roomGrid.GetWidth()  / 2,
                               connectedRoom.roomGrid.GetHeight() / 2);

        if (GameManager.IsMultiplayer)
        {
            // In multiplayer only the owning client can trigger their own transition.
            // The bridge handles broadcasting RoomManager changes to all peers.
            var bridge = player.GetComponent<NetworkedPlayerBridge>();
            if (bridge != null && bridge.IsOwner)
            {
                bridge.TransitionToRoom(connectedRoom.roomGrid, spawnPos);
                return;
            }
            // Non-owner clients shouldn't be clicking doors for other players —
            // but fall through to local placement as a safe default.
        }

        // Single-player path (or fallback)
        RoomManager.Instance?.SetCurrentRoom(connectedRoom);
        player.PlaceInRoom(connectedRoom.roomGrid, spawnPos);
        CameraController2D.Instance?.SnapToTarget();
    }

    private void Start()
    {
        if (GetComponent<Collider2D>() == null)
        {
            var c  = gameObject.AddComponent<BoxCollider2D>();
            c.size = new Vector2(1.5f, 1.5f);
        }
    }
}
using UnityEngine;

/// <summary>
/// Placed in room prefabs. Handles click-based room transition.
///
/// In multiplayer, routes through NetworkedPlayerBridge so all peers
/// see the room change and RoomManager is updated everywhere.
///
/// KEY FIX: FindLocalUnit() searches for the NetworkObject.IsOwner unit
/// instead of FindAnyObjectByType<Unit>() which could return any player
/// (including remote ones) causing IsOwner to be false and the transition
/// to silently do nothing.
/// </summary>
public class RoomDoor : MonoBehaviour
{
    private LevelGenerator.PlacedRoom ownerRoom;
    private LevelGenerator.PlacedRoom connectedRoom;
    private LevelGenerator            gen;
    private LevelGenerator.Direction  doorDir;
    private bool                      ready;

    private void Start()
    {
        if (GetComponent<Collider2D>() == null)
        {
            var c  = gameObject.AddComponent<BoxCollider2D>();
            c.size = new Vector2(1.5f, 1.5f);
        }
    }

    public void Initialize(LevelGenerator.PlacedRoom owner)
    {
        ownerRoom     = owner;
        gen           = FindAnyObjectByType<LevelGenerator>();
        doorDir       = DetermineDirection(owner);
        connectedRoom = gen?.GetConnectedRoom(owner, doorDir);
        ready         = connectedRoom != null;
    }

    private void OnMouseDown()
    {
        if (!ready || connectedRoom == null)
        {
            gen = FindAnyObjectByType<LevelGenerator>();
            connectedRoom = gen?.GetConnectedRoom(ownerRoom, doorDir);
            ready = connectedRoom != null;
            if (!ready) return;
        }

        var player = FindLocalUnit();
        if (player == null) return;
        if (!PlayerIsInThisRoom(player)) return;

        // Only act if the player is actually in this room
        if (!PlayerIsInThisRoom(player)) return;

        var entryDir = gen.GetOppositeDirection(doorDir);
        var reader   = connectedRoom.roomInstance.GetComponent<RoomSpawnPointReader>();

        GridPosition spawnPos;
        if (reader != null && reader.HasSpawnPoint(entryDir))
            spawnPos = reader.GetSpawnPosition(entryDir, connectedRoom.roomGrid);
        else
            spawnPos = new GridPosition(
                connectedRoom.roomGrid.GetWidth()  / 2,
                connectedRoom.roomGrid.GetHeight() / 2);

        if (GameManager.IsMultiplayer)
        {
            var bridge = player.GetComponent<NetworkedPlayerBridge>();
            if (bridge == null || !bridge.IsOwner) return;

            bridge.TransitionToRoom(connectedRoom.roomGrid, spawnPos);
            return;
        }

        // Single-player path
        RoomManager.Instance?.SetCurrentRoom(connectedRoom);
        player.PlaceInRoom(connectedRoom.roomGrid, spawnPos);
        CameraController2D.Instance?.SnapToTarget();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// In multiplayer: returns the Unit whose NetworkObject.IsOwner == true.
    /// In single-player: returns the first Unit found.
    /// This prevents the door from acting on a remote player's unit.
    /// </summary>
    private Unit FindLocalUnit()
    {
        if (!GameManager.IsMultiplayer)
            return FindAnyObjectByType<Unit>();

        foreach (var u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var netObj = u.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null && netObj.IsOwner)
                return u;
        }
        return null;
    }

    /// <summary>
    /// Checks that the player's current room matches the door's owner room
    /// so clients can't trigger doors in rooms they haven't entered.
    /// </summary>
    private bool PlayerIsInThisRoom(Unit player)
    {
        var currentRoom = player.GetCurrentRoomGrid();
        if (currentRoom == null || ownerRoom?.roomGrid == null) return false;
        
        // Compare by name since client/host may have different object references
        // for the same logical room
        return currentRoom.gameObject.name == ownerRoom.roomGrid.gameObject.name;
    }

    private LevelGenerator.Direction DetermineDirection(LevelGenerator.PlacedRoom owner)
    {
        var local = owner.roomInstance.transform.InverseTransformPoint(transform.position);
        return Mathf.Abs(local.x) > Mathf.Abs(local.y)
            ? (local.x > 0 ? LevelGenerator.Direction.East  : LevelGenerator.Direction.West)
            : (local.y > 0 ? LevelGenerator.Direction.North : LevelGenerator.Direction.South);
    }
}
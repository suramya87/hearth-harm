using UnityEngine;

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
        ownerRoom = owner;
        gen       = FindAnyObjectByType<LevelGenerator>();
        doorDir   = DetermineDirection(owner);

        connectedRoom = gen?.GetConnectedRoom(owner, doorDir);

        if (owner.roomGrid != null)
        {
            bool isDoorOpen = owner.roomGrid.GetDoorState(doorDir);
            if (owner.connector != null)
                owner.connector.SetDoorOpen(doorDir, isDoorOpen);

            if (connectedRoom == null)
            {
                this.enabled = false;
                return;
            }
        }

        ready = connectedRoom != null;
    }

    private void OnMouseDown()
    {
        if (!ready || connectedRoom == null)
        {
            gen           = FindAnyObjectByType<LevelGenerator>();
            connectedRoom = gen?.GetConnectedRoom(ownerRoom, doorDir);
            ready         = connectedRoom != null;
            if (!ready) return;
        }

        var player = FindLocalUnit();
        if (player == null) return;
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

        SpawnEnemiesViaButton(connectedRoom);

        if (GameManager.IsMultiplayer)
        {
            var bridge = player.GetComponent<NetworkedPlayerBridge>();
            if (bridge == null || !bridge.IsOwner) return;
            bridge.TransitionToRoom(connectedRoom.roomGrid, spawnPos);
            return;
        }

        RoomManager.Instance?.SetCurrentRoom(connectedRoom);
        player.PlaceInRoom(connectedRoom.roomGrid, spawnPos);
        CameraController2D.Instance?.SnapToTarget();

        player.GetMoveAction()?.InvalidateCache();
    }

    private static void SpawnEnemiesViaButton(LevelGenerator.PlacedRoom room)
    {
        if (room == null) return;
        if (room.prefabData.roomType == LevelGenerator.RoomType.Start) return;
        if (room.roomGrid.HasBeenCleared) return;
        if (EnemyManager.Instance == null) return;
        if (EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0) return;

        var spawner = FindAnyObjectByType<EnemySpawner>();
        spawner?.SpawnForRoom(room);

        bool hasEnemies = EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0;
        if (hasEnemies)
            room.connector?.CloseAllDoors();
    }

    private Unit FindLocalUnit()
    {
        if (!GameManager.IsMultiplayer)
            return FindAnyObjectByType<Unit>();

        foreach (var u in FindObjectsByType<Unit>(FindObjectsSortMode.None))
        {
            var netObj = u.GetComponent<Unity.Netcode.NetworkObject>();
            if (netObj != null && netObj.IsOwner) return u;
        }
        return null;
    }

    private bool PlayerIsInThisRoom(Unit player)
    {
        var currentRoom = player.GetCurrentRoomGrid();
        if (currentRoom == null || ownerRoom?.roomGrid == null) return false;
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
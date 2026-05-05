using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class HallwayEntryTrigger : MonoBehaviour
{
    public HallwayGrid               Hallway         { get; private set; }
    public LevelGenerator.PlacedRoom DestinationRoom { get; private set; }
    public LevelGenerator.Direction  EntryDirection  { get; private set; }

    private bool cooling;

    public void Initialize(
        HallwayGrid                  hallway,
        LevelGenerator.PlacedRoom    destinationRoom,
        LevelGenerator.Direction     entryDirection)
    {
        Hallway         = hallway;
        DestinationRoom = destinationRoom;
        EntryDirection  = entryDirection;

        GetComponent<Collider2D>().isTrigger = true;
        cooling = false;
    }

    public void ResetTrigger()
    {
        StopAllCoroutines();
        cooling = false;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (cooling) return;
        if (!other.CompareTag("Player")) return;
        if (DestinationRoom?.roomGrid == null) return;

        var unit = other.GetComponent<Unit>()
                ?? other.GetComponentInParent<Unit>();
        if (unit == null) return;

        if (GameManager.IsMultiplayer)
        {
            var bridge = unit.GetComponent<NetworkedPlayerBridge>();
            if (bridge == null || !bridge.IsOwner) return;
        }

        if (unit.GetCurrentRoomGrid() == DestinationRoom.roomGrid) return;

        StartCoroutine(TransitionAfterMove(unit));
    }

    private IEnumerator TransitionAfterMove(Unit unit)
    {
        cooling = true;

        var move = unit.GetMoveAction();
        if (move != null)
            while (move.IsActive) yield return null;

        yield return new WaitForSeconds(0.05f);

        if (unit.GetCurrentRoomGrid() == DestinationRoom.roomGrid)
        {
            cooling = false;
            yield break;
        }

        GridPosition spawnPos = GetDestinationSpawnPos();

        if (GameManager.IsMultiplayer)
        {
            foreach (var bridge in FindObjectsByType<NetworkedPlayerBridge>(FindObjectsSortMode.None))
            {
                if (!bridge.IsOwner) continue;
                bridge.TransitionToRoom(DestinationRoom.roomGrid, spawnPos);
                break;
            }
        }
        else
        {
            RoomManager.Instance?.SetCurrentRoom(DestinationRoom);
            unit.PlaceInRoom(DestinationRoom.roomGrid, spawnPos);
            CameraController2D.Instance?.SnapToTarget();
        }

        LockRoomAndSpawnEnemies(DestinationRoom);

        Debug.Log($"[HallwayEntryTrigger] {unit.name} entered " +
                  $"{DestinationRoom.roomInstance.name} via {EntryDirection}.");

        yield return new WaitForSeconds(1f);
        cooling = false;
    }

    private GridPosition GetDestinationSpawnPos()
    {
        var reader = DestinationRoom.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader != null && reader.HasSpawnPoint(EntryDirection))
            return reader.GetSpawnPosition(EntryDirection, DestinationRoom.roomGrid);

        Debug.LogWarning($"[HallwayEntryTrigger] No spawn for {EntryDirection} " +
                         $"in {DestinationRoom.roomInstance.name}. Using centre.");
        return new GridPosition(
            DestinationRoom.roomGrid.GetWidth()  / 2,
            DestinationRoom.roomGrid.GetHeight() / 2);
    }

    // ── Lock + spawn ───────────────────────────────────────────────────────

    private static void LockRoomAndSpawnEnemies(LevelGenerator.PlacedRoom room)
    {
        if (room?.connector == null) return;
        if (room.prefabData.roomType == LevelGenerator.RoomType.Start) return;

        bool alreadyActive = EnemyManager.Instance != null &&
                             EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0;
        if (alreadyActive) return;

        room.connector.CloseAllDoors();

        var spawner = FindAnyObjectByType<EnemySpawner>();
        if (spawner != null)
            spawner.SpawnForRoom(room);
        else
            Debug.LogWarning("[HallwayEntryTrigger] No EnemySpawner found.");

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared += OnRoomCleared;
    }

    private static void OnRoomCleared(RoomGrid clearedRoom)
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return;

        foreach (var placed in gen.GetAllRooms())
        {
            if (placed.roomGrid != clearedRoom) continue;
            foreach (LevelGenerator.Direction dir in
                System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            {
                if (gen.GetConnectedRoom(placed, dir) != null)
                    placed.connector.SetDoorOpen(dir, true);
            }
            break;
        }

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= OnRoomCleared;
    }
}
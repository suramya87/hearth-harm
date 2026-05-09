using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class HallwayEntryTrigger : MonoBehaviour
{
    public HallwayGrid               Hallway         { get; private set; }
    public LevelGenerator.PlacedRoom DestinationRoom { get; private set; }
    public LevelGenerator.Direction  EntryDirection  { get; private set; }

    private HallwayWalkTrigger           pairedWalkTrigger;
    private List<HallwayWalkTrigger>     roomBorderTriggers;
    private bool                         cooling;

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

    public void SetPairedWalkTrigger(HallwayWalkTrigger wt) => pairedWalkTrigger = wt;

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

        if (IsOnDestinationGrid(unit)) return;
        if (!IsOnHallwayGrid(unit)) return;

        StartCoroutine(TransitionAfterMove(unit));
    }

    private IEnumerator TransitionAfterMove(Unit unit)
    {
        cooling = true;

        var move = unit.GetMoveAction();
        if (move != null)
            while (move.IsActive) yield return null;

        yield return new WaitForSeconds(0.05f);

        if (IsOnDestinationGrid(unit)) { cooling = false; yield break; }

        GridPosition spawnPos = GetDestinationSpawnPos();

        HallwayWalkTrigger[] allWalkTriggers = FindObjectsByType<HallwayWalkTrigger>(FindObjectsSortMode.None);
        foreach (var wt in allWalkTriggers)
        {
            wt.DisableTemporarily(1.5f); // Increased to 1.5s for safety
        }

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

        bool hadEnemies = LockRoomAndSpawnEnemies(DestinationRoom, this);
        if (hadEnemies)
            LockAllRoomExits(DestinationRoom);

        Debug.Log($"[HallwayEntryTrigger] {unit.name} entered {DestinationRoom.roomInstance.name}.");

        yield return new WaitForSeconds(0.5f);
        cooling = false;
    }

    private bool IsOnDestinationGrid(Unit unit)
    {
        var current = unit.GetCurrentRoomGrid();
        if (current == null) return false;
        if (current == DestinationRoom.roomGrid) return true;
        return current.gameObject.name == DestinationRoom.roomGrid.gameObject.name;
    }

    private bool IsOnHallwayGrid(Unit unit)
    {
        var current = unit.GetCurrentRoomGrid();
        if (current == null || Hallway?.RoomGrid == null) return false;
        if (current == Hallway.RoomGrid) return true;
        return current.gameObject.name == Hallway.RoomGrid.gameObject.name;
    }

    private GridPosition GetDestinationSpawnPos()
    {
        var reader = DestinationRoom.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader != null && reader.HasSpawnPoint(EntryDirection))
            return reader.GetSpawnPosition(EntryDirection, DestinationRoom.roomGrid);

        return new GridPosition(
            DestinationRoom.roomGrid.GetWidth()  / 2,
            DestinationRoom.roomGrid.GetHeight() / 2);
    }

    private static bool LockRoomAndSpawnEnemies(
            LevelGenerator.PlacedRoom room,
            HallwayEntryTrigger       triggerForCallback)
        {
            if (room?.connector == null) return false;
            
            if (room.prefabData.roomType == LevelGenerator.RoomType.Start) return false;
            
            if (EnemyManager.Instance == null) return false;

            bool alreadyHasEnemies = EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count > 0;

            if (!alreadyHasEnemies)
            {
                var spawner = FindAnyObjectByType<EnemySpawner>();
                if (spawner != null)
                {
                    spawner.SpawnForRoom(room);
                }
            }

            int enemyCount = EnemyManager.Instance.GetEnemiesInRoom(room.roomGrid).Count;
            bool hasEnemies = enemyCount > 0;

            if (hasEnemies)
            {
                room.connector.CloseAllDoors();
                
                EnemyManager.Instance.OnRoomCleared -= triggerForCallback.HandleRoomCleared;
                EnemyManager.Instance.OnRoomCleared += triggerForCallback.HandleRoomCleared;
                
                Debug.Log($"[RoomLock] Room {room.roomGrid.name} locked with {enemyCount} enemies.");
            }
            else
            {
                room.connector.OpenAllDoors();
                Debug.Log($"[RoomLock] Room {room.roomGrid.name} is empty. Doors remaining open.");
            }

            return hasEnemies;
        }

        private void LockAllRoomExits(LevelGenerator.PlacedRoom room)
        {
            // Only proceed if the room actually has enemies
            if (!RoomManager.Instance.CurrentRoomHasEnemies()) return;

            roomBorderTriggers = new List<HallwayWalkTrigger>();

            foreach (var et in FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None))
            {
                if (et.DestinationRoom?.roomGrid == null) continue;

                bool isThisRoom = et.DestinationRoom.roomGrid == room.roomGrid ||
                                et.DestinationRoom.roomGrid.gameObject.name == room.roomGrid.gameObject.name;
                
                if (!isThisRoom) continue;
                if (et.pairedWalkTrigger == null) continue;

                // Link the visual door strips to the trigger
                if (et.pairedWalkTrigger.DoorStripObject == null && room.connector != null)
                {
                    GameObject strip = et.EntryDirection switch
                    {
                        LevelGenerator.Direction.North => room.connector.northDoorStrip,
                        LevelGenerator.Direction.South => room.connector.southDoorStrip,
                        LevelGenerator.Direction.East  => room.connector.eastDoorStrip,
                        LevelGenerator.Direction.West  => room.connector.westDoorStrip,
                        _                              => null
                    };
                    et.pairedWalkTrigger.DoorStripObject = strip;
                }

                // PHYSICALLY lock the trigger so the player can't walk back into the hallway
                et.pairedWalkTrigger.SetLocked(true);
                roomBorderTriggers.Add(et.pairedWalkTrigger);
            }
        }

    private void HandleRoomCleared(RoomGrid clearedRoom)
    {
        if (clearedRoom == null) return;

        bool isOurRoom = clearedRoom == DestinationRoom?.roomGrid ||
                         clearedRoom.gameObject.name == DestinationRoom?.roomGrid?.gameObject.name;
        if (!isOurRoom) return;

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= HandleRoomCleared;

        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen != null)
        {
            foreach (var placed in gen.GetAllRooms())
            {
                bool match = placed.roomGrid == clearedRoom ||
                             placed.roomGrid?.gameObject.name == clearedRoom.gameObject.name;
                if (!match) continue;

                foreach (LevelGenerator.Direction dir in System.Enum.GetValues(typeof(LevelGenerator.Direction)))
                {
                    if (gen.GetConnectedRoom(placed, dir) != null)
                        placed.connector.SetDoorOpen(dir, true);
                }
                break;
            }
        }

        if (roomBorderTriggers != null)
        {
            foreach (var wt in roomBorderTriggers)
                wt?.SetLocked(false);
            roomBorderTriggers.Clear();
        }
    }
}
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkedPlayerSpawner : NetworkBehaviour
{
    [Header("Player Prefabs (index matches CharacterSelection.Index)")]
    [SerializeField] private List<GameObject> playerPrefabs;

    [Header("Fallback if selection index is out of range")]
    [SerializeField] private GameObject fallbackPrefab;

    [Header("Spawn spread (grid units apart)")]
    [SerializeField] private int spawnSpread = 1;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        LevelSyncBridge.OnNetworkLevelReady += OnLevelReady;
    }

    public override void OnNetworkDespawn()
    {
        LevelSyncBridge.OnNetworkLevelReady -= OnLevelReady;
    }

    private void OnLevelReady()
    {
        if (!IsServer) return;
        StartCoroutine(SpawnAllPlayersNextFrame());
    }

    private IEnumerator SpawnAllPlayersNextFrame()
    {
        yield return null;
        // SpawnAllPlayers();
    }

    // private void SpawnAllPlayers()
    // {
    //     var gen = FindAnyObjectByType<LevelGenerator>();
    //     if (gen == null) { Debug.LogError("[NetworkedPlayerSpawner] No LevelGenerator!"); return; }

    //     var startRoom = gen.GetAllRooms().Find(r =>
    //         r.prefabData.roomType == LevelGenerator.RoomType.Start &&
    //         r.roomGrid != null && r.roomGrid.IsInitialized());

    //     if (startRoom == null)
    //     {
    //         Debug.LogError("[NetworkedPlayerSpawner] No valid start room found!");
    //         return;
    //     }

    //     RoomManager.Instance?.SetCurrentRoom(startRoom);

    //     var clients = NetworkManager.Singleton.ConnectedClientsList;
    //     int cx = startRoom.roomGrid.GetWidth()  / 2;
    //     int cy = startRoom.roomGrid.GetHeight() / 2;

    //     var offsets = new Vector2Int[]
    //     {
    //         new(0, 0), new(-spawnSpread, 0), new(spawnSpread, 0),
    //         new(0, -spawnSpread), new(0, spawnSpread)
    //     };

    //     for (int i = 0; i < clients.Count; i++)
    //     {
    //         ulong clientId  = clients[i].ClientId;

    //         int charIndex = CharacterSelectionSync.Instance != null
    //             ? CharacterSelectionSync.Instance.GetCharacterIndex(clientId)
    //             : 0;

    //         GameObject prefab = GetPrefab(charIndex);
    //         if (prefab == null) continue;

    //         var offset  = i < offsets.Length ? offsets[i] : new Vector2Int(i, 0);
    //         var spawnGP = new GridPosition(cx + offset.x, cy + offset.y);

    //         if (!startRoom.roomGrid.IsValidGridPosition(spawnGP) ||
    //             !startRoom.roomGrid.IsWalkableIgnoreOccupancy(spawnGP))
    //             spawnGP = new GridPosition(cx, cy);

    //         Vector3 spawnWorld = startRoom.roomGrid.GetWorldPosition(spawnGP);

    //         var go     = Instantiate(prefab, spawnWorld, Quaternion.identity);
    //         var netObj = go.GetComponent<NetworkObject>();

    //         if (netObj == null)
    //         {
    //             Debug.LogError($"[NetworkedPlayerSpawner] {prefab.name} missing NetworkObject!");
    //             Destroy(go);
    //             continue;
    //         }

    //         netObj.SpawnAsPlayerObject(clientId, destroyWithScene: true);

    //         var unit = go.GetComponent<Unit>();
    //         unit?.PlaceInRoom(startRoom.roomGrid, spawnGP);

    //         var bridge = go.GetComponent<NetworkedPlayerBridge>();
    //         bridge?.PlaceInRoom(startRoom.roomGrid, spawnGP);

    //         Debug.Log($"[NetworkedPlayerSpawner] Spawned player {clientId} (char {charIndex}) at {spawnGP}");
    //     }
    // }

    private GameObject GetPrefab(int index)
    {
        if (playerPrefabs != null && index >= 0 && index < playerPrefabs.Count)
            return playerPrefabs[index];
        if (fallbackPrefab != null) return fallbackPrefab;
        Debug.LogError("[NetworkedPlayerSpawner] No valid player prefab for index " + index);
        return null;
    }
}
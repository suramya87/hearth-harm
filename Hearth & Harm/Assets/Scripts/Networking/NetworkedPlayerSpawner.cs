using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Spawns one networked player prefab per connected client after the level is ready.
///
/// SETUP:
///   1. Add this component to the same GameObject as LevelSyncBridge / LevelGenerator.
///   2. Assign your networked player prefabs in the Inspector (must have NetworkObject +
///      NetworkedPlayerBridge + Unit).
///   3. Make sure each prefab is registered in NetworkManager's NetworkPrefabs list.
///
/// FLOW:
///   LevelSyncBridge.OnNetworkLevelReady fires on server only (after the LevelSyncBridge fix)
///   → Host calls SpawnAllPlayers()
///   → For each client: Instantiate prefab, NetworkObject.SpawnAsPlayerObject(clientId)
///   → NetworkedPlayerBridge.InitialPlacement() writes NetworkVariables → replicates to clients
///   → SetStartRoomClientRpc tells every peer which room is the start room via grid coords
/// </summary>
public class NetworkedPlayerSpawner : NetworkBehaviour
{
    [Header("Player Prefabs")]
    [Tooltip("Index matches character selection (0=Knight, 1=Rogue …). " +
             "Must be registered in NetworkManager NetworkPrefabs list.")]
    [SerializeField] private List<GameObject> networkedPlayerPrefabs;

    private LevelGenerator levelGenerator;

    // ── Network lifecycle ──────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        LevelSyncBridge.OnNetworkLevelReady += OnNetworkLevelReady;
    }

    public override void OnNetworkDespawn()
    {
        LevelSyncBridge.OnNetworkLevelReady -= OnNetworkLevelReady;
    }

    private void OnNetworkLevelReady()
    {
        if (!IsServer) return;
        StartCoroutine(SpawnAllPlayers());
    }

    // ── Spawn all players (server only) ───────────────────────────────────

    private IEnumerator SpawnAllPlayers()
    {
        yield return null;

        // Retry finding LevelGenerator
        for (int i = 0; levelGenerator == null && i < 10; i++)
        {
            levelGenerator = FindAnyObjectByType<LevelGenerator>();
            yield return null;
        }

        if (levelGenerator == null)
        {
            Debug.LogError("[NetworkedPlayerSpawner] No LevelGenerator found!");
            yield break;
        }

        // Retry finding the start room
        LevelGenerator.PlacedRoom startRoom = null;
        for (int i = 0; startRoom == null && i < 10; i++)
        {
            startRoom = levelGenerator.GetAllRooms()
                ?.Find(r => r.prefabData.roomType == LevelGenerator.RoomType.Start
                         && r.roomGrid != null
                         && r.roomGrid.IsInitialized());
            if (startRoom == null) yield return null;
        }

        if (startRoom == null)
        {
            Debug.LogError("[NetworkedPlayerSpawner] No valid start room found!");
            yield break;
        }

        var clients = NetworkManager.Singleton.ConnectedClientsList;
        Debug.Log($"[NetworkedPlayerSpawner] Spawning {clients.Count} player(s).");

        int slotIndex = 0;
        foreach (var client in clients)
        {
            int charIndex = 0;
            if (LobbySync.Instance != null)
                charIndex = LobbySync.Instance.GetCharacterIndex(client.ClientId);
            else if (CharacterSelectionSync.Instance != null)
                charIndex = CharacterSelectionSync.Instance.GetCharacterIndex(client.ClientId);

            charIndex = Mathf.Clamp(charIndex, 0, networkedPlayerPrefabs.Count - 1);

            GameObject prefab = networkedPlayerPrefabs[charIndex];
            if (prefab == null)
            {
                Debug.LogError($"[NetworkedPlayerSpawner] Prefab at index {charIndex} is null!");
                slotIndex++;
                continue;
            }

            GridPosition? spawnPos = FindSpawnPosition(startRoom, slotIndex);
            if (spawnPos == null)
            {
                Debug.LogError($"[NetworkedPlayerSpawner] No spawn position for client {client.ClientId}!");
                slotIndex++;
                continue;
            }

            Vector3 worldPos = startRoom.roomGrid.GetWorldPosition(spawnPos.Value);
            var go = Instantiate(prefab, worldPos, Quaternion.identity);
            go.name = $"Player_{client.ClientId}";

            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError("[NetworkedPlayerSpawner] Player prefab is missing a NetworkObject!");
                Destroy(go);
                slotIndex++;
                continue;
            }

            netObj.SpawnAsPlayerObject(client.ClientId);

            var bridge = go.GetComponent<NetworkedPlayerBridge>();
            if (bridge != null)
                bridge.InitialPlacement(startRoom.roomGrid, spawnPos.Value);

            var unit = go.GetComponent<Unit>();
            if (unit != null)
            {
                unit.IsSyncingFromNetwork = true;
                unit.PlaceInRoom(startRoom.roomGrid, spawnPos.Value);
                unit.IsSyncingFromNetwork = false;
            }

            Debug.Log($"[NetworkedPlayerSpawner] Spawned client {client.ClientId} " +
                      $"(char {charIndex}) at {spawnPos.Value}");

            slotIndex++;
            yield return null;
        }

        RoomManager.Instance?.SetCurrentRoom(startRoom);
        
        SetStartRoomClientRpc(startRoom.gridPosition.x, startRoom.gridPosition.y);
    }

    // ── Tell all peers which room to activate ─────────────────────────────

    [ClientRpc]
    private void SetStartRoomClientRpc(int gridX, int gridY)
    {
        // Host already set its room during SpawnAllPlayers — only clients need to act
        if (IsServer) return;

        if (levelGenerator == null)
            levelGenerator = FindAnyObjectByType<LevelGenerator>();

        var rooms = levelGenerator?.GetAllRooms();
        if (rooms == null) return;

        foreach (var placed in rooms)
        {
            if (placed.gridPosition.x != gridX || placed.gridPosition.y != gridY) continue;

            RoomManager.Instance?.SetCurrentRoom(placed);
            Debug.Log($"[NetworkedPlayerSpawner] Client set start room → ({gridX},{gridY})");
            return;
        }

        Debug.LogWarning($"[NetworkedPlayerSpawner] Could not find start room at ({gridX},{gridY})");
    }

    // ── Spawn position helpers ─────────────────────────────────────────────

    private GridPosition? FindSpawnPosition(LevelGenerator.PlacedRoom room, int slotIndex)
    {
        var reader = room.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader != null)
        {
            var all  = reader.GetAll();
            var dirs = new[]
            {
                LevelGenerator.Direction.South,
                LevelGenerator.Direction.North,
                LevelGenerator.Direction.West,
                LevelGenerator.Direction.East
            };

            int dirIndex = slotIndex % dirs.Length;
            for (int i = 0; i < dirs.Length; i++)
            {
                var dir = dirs[(dirIndex + i) % dirs.Length];
                if (all.TryGetValue(dir, out var pos))
                    return pos;
            }
        }

        return FindWalkableTileNearCentre(room.roomGrid, slotIndex);
    }

    private GridPosition? FindWalkableTileNearCentre(RoomGrid roomGrid, int offset)
    {
        var tilemap = roomGrid.GetFloorTilemap();
        if (tilemap == null) return null;

        var bounds = tilemap.cellBounds;
        int cx = (bounds.xMin + bounds.xMax) / 2 + (offset % 3) - 1;
        int cy = (bounds.yMin + bounds.yMax) / 2 + (offset / 3);

        var center = new GridPosition(cx, cy);
        if (roomGrid.IsWalkable(center)) return center;

        for (int r = 1; r < Mathf.Max(bounds.size.x, bounds.size.y); r++)
        for (int x = cx - r; x <= cx + r; x++)
        for (int y = cy - r; y <= cy + r; y++)
        {
            if (Mathf.Abs(x - cx) != r && Mathf.Abs(y - cy) != r) continue;
            var c = new GridPosition(x, y);
            if (roomGrid.IsWalkable(c)) return c;
        }

        return null;
    }
}
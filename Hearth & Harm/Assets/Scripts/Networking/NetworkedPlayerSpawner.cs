using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Spawns one networked player prefab per connected client after the level is ready.
///
/// SETUP:
///   1. Add this component to the same GameObject as LevelSyncBridge / LevelGenerator
///   2. Assign your networked player prefabs in the Inspector (must have NetworkObject +
///      NetworkedPlayerBridge + Unit)
///   3. Make sure each player prefab is registered in NetworkManager's NetworkPrefabs list
///
/// FLOW:
///   LevelSyncBridge.OnNetworkLevelReady fires on all peers
///   → Host calls SpawnAllPlayers()
///   → For each client: Instantiate prefab, NetworkObject.SpawnAsPlayerObject(clientId)
///   → NetworkedPlayerBridge.InitialPlacement() sets starting room + grid position
///   → NetworkVariables replicate to all peers automatically
/// </summary>
public class NetworkedPlayerSpawner : NetworkBehaviour
{
    [Header("Player Prefabs")]
    [Tooltip("Index matches character selection (0=Knight, 1=Rogue …). " +
             "Must be registered in NetworkManager NetworkPrefabs list.")]
    [SerializeField] private List<GameObject> networkedPlayerPrefabs;

    [Tooltip("Offset each player slightly so they don't stack on spawn.")]
    [SerializeField] private bool offsetSpawnPositions = true;

    private LevelGenerator levelGenerator;

    private void Awake()
    {
        levelGenerator = GetComponent<LevelGenerator>();
        if (levelGenerator == null)
            levelGenerator = FindAnyObjectByType<LevelGenerator>();
    }

    private void OnEnable()
    {
        LevelSyncBridge.OnNetworkLevelReady += OnNetworkLevelReady;
    }

    private void OnDisable()
    {
        LevelSyncBridge.OnNetworkLevelReady -= OnNetworkLevelReady;
    }

    private void OnNetworkLevelReady()
    {
        // Only the host spawns players
        if (!IsServer) return;
        StartCoroutine(SpawnAllPlayers());
    }

    // ─────────────────────────────────────────────────────────────────────
    // Spawn all players (host only)
    // ─────────────────────────────────────────────────────────────────────

    private IEnumerator SpawnAllPlayers()
    {
        // Wait one frame to ensure all room grids are fully initialized
        yield return null;

        if (levelGenerator == null)
        {
            Debug.LogError("[NetworkedPlayerSpawner] No LevelGenerator found!");
            yield break;
        }

        var startRoom = levelGenerator.GetAllRooms()
            ?.Find(r => r.prefabData.roomType == LevelGenerator.RoomType.Start
                     && r.roomGrid != null
                     && r.roomGrid.IsInitialized());

        if (startRoom == null)
        {
            Debug.LogError("[NetworkedPlayerSpawner] No valid start room found!");
            yield break;
        }

        var clients = NetworkManager.Singleton.ConnectedClientsList;
        Debug.Log($"[NetworkedPlayerSpawner] Spawning {clients.Count} players.");

        int slotIndex = 0;
        foreach (var client in clients)
        {
            // Get this client's character selection from LobbySync
            int charIndex = 0;
            if (LobbySync.Instance != null)
                charIndex = LobbySync.Instance.GetCharacterIndex(client.ClientId);

            // Clamp to valid prefab range
            charIndex = Mathf.Clamp(charIndex, 0, networkedPlayerPrefabs.Count - 1);

            GameObject prefab = networkedPlayerPrefabs[charIndex];
            if (prefab == null)
            {
                Debug.LogError($"[NetworkedPlayerSpawner] Prefab at index {charIndex} is null!");
                slotIndex++;
                continue;
            }

            // Find a spawn position — try spawn points first, fall back to centre
            GridPosition? spawnPos = FindSpawnPosition(startRoom, slotIndex);
            if (spawnPos == null)
            {
                Debug.LogError($"[NetworkedPlayerSpawner] No spawn position for client {client.ClientId}!");
                slotIndex++;
                continue;
            }

            // Instantiate and network-spawn as the player object for this client
            Vector3 worldPos = startRoom.roomGrid.GetWorldPosition(spawnPos.Value);
            var go = Instantiate(prefab, worldPos, Quaternion.identity);
            go.name = $"Player_{client.ClientId}";

            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null)
            {
                Debug.LogError($"[NetworkedPlayerSpawner] Player prefab missing NetworkObject!");
                Destroy(go);
                slotIndex++;
                continue;
            }

            // SpawnAsPlayerObject assigns ownership to the client automatically
            netObj.SpawnAsPlayerObject(client.ClientId);

            // Set initial grid position via NetworkedPlayerBridge (server-authoritative)
            var bridge = go.GetComponent<NetworkedPlayerBridge>();
            if (bridge != null)
                bridge.InitialPlacement(startRoom.roomGrid, spawnPos.Value);

            // Place the Unit locally on the server too
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
            yield return null; // slight stagger between spawns
        }

        // Tell all clients to set their RoomManager to the start room
        SetStartRoomClientRpc(startRoom.roomInstance.name);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Tell all clients which room to set as current
    // ─────────────────────────────────────────────────────────────────────

    [ClientRpc]
    private void SetStartRoomClientRpc(string startRoomName)
    {
        if (levelGenerator == null)
            levelGenerator = FindAnyObjectByType<LevelGenerator>();

        var rooms = levelGenerator?.GetAllRooms();
        if (rooms == null) return;

        foreach (var placed in rooms)
        {
            if (placed.roomInstance != null &&
                placed.roomInstance.name == startRoomName)
            {
                RoomManager.Instance?.SetCurrentRoom(placed);
                Debug.Log($"[NetworkedPlayerSpawner] Client set start room → {startRoomName}");
                return;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Spawn position helpers
    // ─────────────────────────────────────────────────────────────────────

    private GridPosition? FindSpawnPosition(LevelGenerator.PlacedRoom room, int slotIndex)
    {
        // Try directional spawn points first
        var reader = room.roomInstance.GetComponent<RoomSpawnPointReader>();
        if (reader != null)
        {
            var all = reader.GetAll();
            var dirs = new[]
            {
                LevelGenerator.Direction.South,
                LevelGenerator.Direction.North,
                LevelGenerator.Direction.West,
                LevelGenerator.Direction.East
            };

            // Each player gets a different directional spawn point if available
            int dirIndex = slotIndex % dirs.Length;
            for (int i = 0; i < dirs.Length; i++)
            {
                var dir = dirs[(dirIndex + i) % dirs.Length];
                if (all.TryGetValue(dir, out var pos))
                    return pos;
            }
        }

        // Fall back to walkable tile near centre
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
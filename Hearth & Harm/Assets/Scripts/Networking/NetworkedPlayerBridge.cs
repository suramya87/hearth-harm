using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;


[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Unit))]
public class NetworkedPlayerBridge : NetworkBehaviour
{
    // ── Network state ──────────────────────────────────────────────────────

    /// <summary>Name of the RoomGrid GameObject this player is currently in.</summary>
    private NetworkVariable<FixedString64Bytes> currentRoomName = new("",
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> gridX = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> gridY = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Cached refs ────────────────────────────────────────────────────────

    private Unit unit;

    private void Awake()
    {
        unit = GetComponent<Unit>();
    }

    public override void OnNetworkSpawn()
    {
        if (!GameManager.IsMultiplayer) return;

        currentRoomName.OnValueChanged += OnRoomNameChanged;

        // Non-owner clients: if the room name is already set, apply it
        if (!IsOwner && !string.IsNullOrEmpty(currentRoomName.Value.ToString()))
            ApplyRoomSync(currentRoomName.Value.ToString());
    }

    public override void OnNetworkDespawn()
    {
        currentRoomName.OnValueChanged -= OnRoomNameChanged;
    }

    // ── Public API — replaces Unit.PlaceInRoom() in networked contexts ─────

    /// <summary>
    /// Network-safe placement. Owner calls this; it syncs to all peers.
    /// In SP: just calls Unit.PlaceInRoom() directly.
    /// </summary>
    public void PlaceInRoom(RoomGrid room, GridPosition pos)
    {
        if (!GameManager.IsMultiplayer)
        {
            unit.PlaceInRoom(room, pos);
            return;
        }

        // Apply locally for the owner (feels instant)
        if (IsOwner)
        {
            unit.PlaceInRoom(room, pos);
            // Tell server to validate and broadcast
            RequestPlaceInRoomServerRpc(room.gameObject.name, pos.x, pos.y);
        }
    }

    /// <summary>
    /// Network-safe room transition from a door click.
    /// Owner calls this; server confirms and broadcasts to all.
    /// </summary>
    public void TransitionToRoom(RoomGrid newRoom, GridPosition spawnPos)
    {
        if (!GameManager.IsMultiplayer)
        {
            unit.PlaceInRoom(newRoom, spawnPos);
            RoomManager.Instance?.SetCurrentRoom(
                FindPlacedRoomForGrid(newRoom));
            return;
        }

        if (IsOwner)
            RequestRoomTransitionServerRpc(newRoom.gameObject.name, spawnPos.x, spawnPos.y);
    }

    // ── Server RPCs ────────────────────────────────────────────────────────

    [ServerRpc]
    private void RequestPlaceInRoomServerRpc(string roomName, int x, int y)
    {
        // Update network variables — all clients get notified via OnRoomNameChanged
        currentRoomName.Value = roomName;
        gridX.Value = x;
        gridY.Value = y;
    }

    [ServerRpc]
    private void RequestRoomTransitionServerRpc(string roomName, int spawnX, int spawnY)
    {
        currentRoomName.Value = roomName;
        gridX.Value = spawnX;
        gridY.Value = spawnY;

        // Notify all clients of the room transition
        BroadcastRoomTransitionClientRpc(roomName, spawnX, spawnY);
    }

    // ── Client RPCs ────────────────────────────────────────────────────────

    [ClientRpc]
    private void BroadcastRoomTransitionClientRpc(string roomName, int spawnX, int spawnY)
    {
        if (IsOwner) return; // Owner already applied it locally

        var room = FindRoomGridByName(roomName);
        if (room == null)
        {
            Debug.LogWarning($"[NetworkedPlayerBridge] Could not find room '{roomName}' on client.");
            return;
        }

        var pos = new GridPosition(spawnX, spawnY);
        unit.PlaceInRoom(room, pos);
    }

    // ── NetworkVariable callbacks ──────────────────────────────────────────

    private void OnRoomNameChanged(FixedString64Bytes oldVal, FixedString64Bytes newVal)
    {
        if (IsOwner || IsServer) return;
        ApplyRoomSync(newVal.ToString());
    }

    private void ApplyRoomSync(string roomName)
    {
        var room = FindRoomGridByName(roomName);
        if (room == null) return;
        var pos = new GridPosition(gridX.Value, gridY.Value);
        unit.PlaceInRoom(room, pos);
    }

    // ── Room lookup helpers ────────────────────────────────────────────────

    private RoomGrid FindRoomGridByName(string roomName)
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return null;

        foreach (var placed in gen.GetAllRooms())
            if (placed.roomGrid != null &&
                placed.roomGrid.gameObject.name == roomName)
                return placed.roomGrid;

        return null;
    }

    private LevelGenerator.PlacedRoom FindPlacedRoomForGrid(RoomGrid grid)
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return null;

        foreach (var placed in gen.GetAllRooms())
            if (placed.roomGrid == grid) return placed;

        return null;
    }

    // ── Enemy AI support — expose synced grid position ─────────────────────

    /// <summary>
    /// Returns the authoritative grid position of this player.
    /// Enemy AI on the server should call this instead of unit.GetGridPosition()
    /// if they need to be safe about network order.
    /// </summary>
    public GridPosition GetNetworkGridPosition() =>
        new GridPosition(gridX.Value, gridY.Value);
}
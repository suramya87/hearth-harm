using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Bridges Unit grid position and room state to all peers via NGO.
///
/// HOW MOVEMENT FLOWS (MoveAction needs ZERO changes):
///   1. Owner's Unit.PlaceInRoom() fires (from MoveAction, or anywhere)
///   2. Unit.PlaceInRoom() calls bridge.SyncGridPosition() — owner + multiplayer only
///   3. SyncGridPosition sends RequestMoveServerRpc
///   4. Server updates gridX/gridY/currentRoomName NetworkVariables
///   5. Non-owner peers: OnGridPositionChanged → ApplyRoomSync → Unit.PlaceInRoom
///      (unit.IsSyncingFromNetwork = true so step 2 cannot loop)
///
/// ROOM TRANSITIONS:
///   RoomDoor calls bridge.TransitionToRoom() in multiplayer.
///   Applies locally, sends ServerRpc, broadcasts to all clients.
///   Also calls NotifyRoomChanged() so UnitActionSystem/TilemapHighlighter
///   immediately update their room reference without waiting a full frame.
///
/// PREFAB REQUIREMENTS:
///   NetworkObject + NetworkTransform + Unit + NetworkedPlayerBridge
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Unit))]
public class NetworkedPlayerBridge : NetworkBehaviour
{
    // ── Network state ──────────────────────────────────────────────────────

    private NetworkVariable<FixedString64Bytes> currentRoomName = new("",
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> gridX = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> gridY = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Unit unit;
    private bool isTransitioning;

    private void Awake() => unit = GetComponent<Unit>();

    public override void OnNetworkSpawn()
    {
        if (!GameManager.IsMultiplayer) return;

        currentRoomName.OnValueChanged += OnRoomNameChanged;
        gridX.OnValueChanged           += OnGridPositionChanged;
        gridY.OnValueChanged           += OnGridPositionChanged;

        // Late-join: apply existing state immediately
        string existing = currentRoomName.Value.ToString();
        if (!IsOwner && !string.IsNullOrEmpty(existing))
            ApplyRoomSync(existing, gridX.Value, gridY.Value);
    }

    public override void OnNetworkDespawn()
    {
        currentRoomName.OnValueChanged -= OnRoomNameChanged;
        gridX.OnValueChanged           -= OnGridPositionChanged;
        gridY.OnValueChanged           -= OnGridPositionChanged;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the replicated room name. Used by UnitActionSystem to resolve
    /// which PlacedRoom to set on RoomManager for the local peer.
    /// </summary>
    public string GetCurrentRoomName() => currentRoomName.Value.ToString();

    /// <summary>Server-authoritative grid position — use this for enemy AI targeting.</summary>
    public GridPosition GetNetworkGridPosition() => new(gridX.Value, gridY.Value);

    // ── Called by Unit.PlaceInRoom (owner only) ────────────────────────────

    public void SyncGridPosition(RoomGrid room, GridPosition pos)
    {
        if (isTransitioning) return;
        RequestMoveServerRpc(room.gameObject.name, pos.x, pos.y);
    }

    // ── Called by NetworkedPlayerSpawner after spawn ───────────────────────

    public void InitialPlacement(RoomGrid room, GridPosition pos)
    {
        if (!GameManager.IsMultiplayer || !IsServer) return;
        currentRoomName.Value = room.gameObject.name;
        gridX.Value           = pos.x;
        gridY.Value           = pos.y;
    }

    // ── Called by RoomDoor in multiplayer ─────────────────────────────────

    public void TransitionToRoom(RoomGrid newRoom, GridPosition spawnPos)
    {
        if (!GameManager.IsMultiplayer)
        {
            unit.PlaceInRoom(newRoom, spawnPos);
            var sp = FindPlacedRoomForGrid(newRoom);
            RoomManager.Instance?.SetCurrentRoom(sp);
            return;
        }

        if (!IsOwner) return;

        isTransitioning = true;
        unit.PlaceInRoom(newRoom, spawnPos);
        isTransitioning = false;

        // Update RoomManager immediately so HandleInput and TilemapHighlighter
        // both have the correct room on the very next frame
        var placed = FindPlacedRoomForGrid(newRoom);
        RoomManager.Instance?.SetCurrentRoom(placed);
        CameraController2D.Instance?.SnapToTarget();

        // Sync to server + broadcast to other clients
        RequestRoomTransitionServerRpc(newRoom.gameObject.name, spawnPos.x, spawnPos.y);
    }

    // ── Server RPCs ────────────────────────────────────────────────────────

    [ServerRpc]
    private void RequestMoveServerRpc(string roomName, int x, int y)
    {
        currentRoomName.Value = roomName;
        gridX.Value           = x;
        gridY.Value           = y;
    }

    [ServerRpc]
    private void RequestRoomTransitionServerRpc(string roomName, int spawnX, int spawnY)
    {
        currentRoomName.Value = roomName;
        gridX.Value           = spawnX;
        gridY.Value           = spawnY;
        BroadcastRoomTransitionClientRpc(roomName, spawnX, spawnY);
    }

    // ── Client RPCs ────────────────────────────────────────────────────────

    [ClientRpc]
    private void BroadcastRoomTransitionClientRpc(string roomName, int spawnX, int spawnY)
    {
        if (IsOwner) return; // Owner already applied locally in TransitionToRoom

        ApplyRoomSync(roomName, spawnX, spawnY);

        // If this NetworkObject belongs to the local player (edge case: IsOwner
        // check above handles the normal case, this handles IsLocalPlayer scenarios)
        if (IsLocalPlayer)
        {
            var room = FindRoomGridByName(roomName);
            if (room != null)
            {
                var placed = FindPlacedRoomForGrid(room);
                RoomManager.Instance?.SetCurrentRoom(placed);
            }
            CameraController2D.Instance?.SnapToTarget();
        }
    }

    // ── NetworkVariable callbacks ──────────────────────────────────────────

    private void OnRoomNameChanged(FixedString64Bytes oldVal, FixedString64Bytes newVal)
    {
        if (IsOwner) return;
        ApplyRoomSync(newVal.ToString(), gridX.Value, gridY.Value);
    }

    private void OnGridPositionChanged(int oldVal, int newVal)
    {
        if (IsOwner) return;
        string room = currentRoomName.Value.ToString();
        if (!string.IsNullOrEmpty(room))
            ApplyRoomSync(room, gridX.Value, gridY.Value);
    }

    private void ApplyRoomSync(string roomName, int x, int y)
    {
        var room = FindRoomGridByName(roomName);
        if (room == null) return;

        unit.IsSyncingFromNetwork = true;
        unit.PlaceInRoom(room, new GridPosition(x, y));
        unit.IsSyncingFromNetwork = false;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private RoomGrid FindRoomGridByName(string roomName)
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return null;
        foreach (var placed in gen.GetAllRooms())
            if (placed.roomGrid != null && placed.roomGrid.gameObject.name == roomName)
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
}
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

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

        string existing = currentRoomName.Value.ToString();
        if (!string.IsNullOrEmpty(existing))
            ApplyRoomSync(existing, gridX.Value, gridY.Value);
    }

    public override void OnNetworkDespawn()
    {
        currentRoomName.OnValueChanged -= OnRoomNameChanged;
        gridX.OnValueChanged           -= OnGridPositionChanged;
        gridY.OnValueChanged           -= OnGridPositionChanged;
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public string GetCurrentRoomName() => currentRoomName.Value.ToString();
    public GridPosition GetNetworkGridPosition() => new(gridX.Value, gridY.Value);

    // ── Called by Unit.PlaceInRoom (owner only, not while syncing) ─────────

    public void SyncGridPosition(RoomGrid room, GridPosition pos)
    {
        if (isTransitioning) return;
        RequestMoveServerRpc(room.gameObject.name, pos.x, pos.y);
    }

    // ── Called by NetworkedPlayerSpawner after spawn (server only) ─────────

    public void InitialPlacement(RoomGrid room, GridPosition pos)
    {
        if (!GameManager.IsMultiplayer || !IsServer) return;

        currentRoomName.Value = room.gameObject.name;
        gridX.Value           = pos.x;
        gridY.Value           = pos.y;

        InitialPlacementClientRpc(room.gameObject.name, pos.x, pos.y);
    }

    [ClientRpc]
    private void InitialPlacementClientRpc(string roomName, int x, int y)
    {
        // Server already called PlaceInRoom locally in NetworkedPlayerSpawner.
        // Only clients need to act here.
        if (IsServer) return;

        ApplyRoomSync(roomName, x, y);

        // If this is the local player's object, also update RoomManager so the
        // camera, highlighter, and input system all have the correct room.
        if (IsOwner)
        {
            var room = FindRoomGridByName(roomName);
            if (room != null)
            {
                var placed = FindPlacedRoomForGrid(room);
                if (placed != null)
                    RoomManager.Instance?.SetCurrentRoom(placed);
            }
            Debug.Log($"[NetworkedPlayerBridge] Owner initialized in room {roomName} at ({x},{y})");
        }
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

        var placed = FindPlacedRoomForGrid(newRoom);
        RoomManager.Instance?.SetCurrentRoom(placed);
        CameraController2D.Instance?.SnapToTarget();

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
        ApplyRoomSync(newVal.ToString(), gridX.Value, gridY.Value);
    }

    private void OnGridPositionChanged(int oldVal, int newVal)
    {
        // ── FIX: removed  if (IsOwner) return; ────────────────────────────
        // Same reason as OnRoomNameChanged above.
        string room = currentRoomName.Value.ToString();
        if (!string.IsNullOrEmpty(room))
            ApplyRoomSync(room, gridX.Value, gridY.Value);
    }

    // ── Core sync ──────────────────────────────────────────────────────────

    private void ApplyRoomSync(string roomName, int x, int y)
    {
        var room = FindRoomGridByName(roomName);
        if (room == null)
        {
            Debug.LogWarning($"[NetworkedPlayerBridge] ApplyRoomSync: room '{roomName}' not found.");
            return;
        }

        unit.IsSyncingFromNetwork = true;
        unit.PlaceInRoom(room, new GridPosition(x, y));
        unit.IsSyncingFromNetwork = false;
    }

    // ── Called by CombatAction (owner only) ───────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    public void RequestApplyDamageServerRpc(int[] posX, int[] posY, int damage)
    {
        ApplyDamageOnPeer(posX, posY, damage);
        ApplyDamageClientRpc(posX, posY, damage);
    }

    [ClientRpc]
    private void ApplyDamageClientRpc(int[] posX, int[] posY, int damage)
    {
        if (IsServer) return;
        ApplyDamageOnPeer(posX, posY, damage);
    }


    private void ApplyDamageOnPeer(int[] posX, int[] posY, int damage)
    {
        var room = unit.GetCurrentRoomGrid();
        if (room == null) return;

        for (int i = 0; i < posX.Length; i++)
        {
            var pos = new GridPosition(posX[i], posY[i]);
            if (!room.IsValidGridPosition(pos)) continue;

            foreach (var enemy in room.GetEnemiesAtGridPosition(pos))
            {
                if (enemy == null || enemy.IsDead) continue;
                NetworkedHealthBridge.TakeDamage(enemy.gameObject, damage);
            }

            foreach (var target in room.GetUnitsAtGridPosition(pos))
            {
                NetworkedHealthBridge.TakeDamage(target.gameObject, damage);
            }
        }
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
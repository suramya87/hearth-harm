using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Unit))]
public class NetworkedPlayerBridge : NetworkBehaviour
{
    // ── Network variables ──────────────────────────────────────────────────

    private NetworkVariable<FixedString64Bytes> currentRoomName = new("",
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> gridX = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<int> gridY = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ── Private ────────────────────────────────────────────────────────────

    private Unit unit;
    private bool isTransitioning;
    private bool syncPending;
    private Coroutine syncCoroutine;

    private void Awake() => unit = GetComponent<Unit>();

    // ── Network lifecycle ──────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!GameManager.IsMultiplayer) return;

        currentRoomName.OnValueChanged += OnRoomNameChanged;
        gridX.OnValueChanged           += OnGridPositionChanged;
        gridY.OnValueChanged           += OnGridPositionChanged;

        // Apply any position the server already set before we spawned.
        string existing = currentRoomName.Value.ToString();
        if (!string.IsNullOrEmpty(existing))
            ApplyRoomSync(existing, gridX.Value, gridY.Value);

        // Only the owner needs local systems wired up.
        if (IsOwner)
            StartCoroutine(WireUpOwnerSystems());
    }

    public override void OnNetworkDespawn()
    {
        currentRoomName.OnValueChanged -= OnRoomNameChanged;
        gridX.OnValueChanged           -= OnGridPositionChanged;
        gridY.OnValueChanged           -= OnGridPositionChanged;

        if (syncCoroutine != null) { StopCoroutine(syncCoroutine); syncCoroutine = null; }
        syncPending = false;
    }

    // ── Owner setup ────────────────────────────────────────────────────────

    /// <summary>
    /// Waits until:
    ///   1. The unit is placed in a room (isInitialized)
    ///   2. UnitActionSystem.Instance exists
    /// Then registers the unit so the client can move it.
    /// </summary>
    private IEnumerator WireUpOwnerSystems()
    {
        // Wait for UnitActionSystem to exist (it's a scene singleton).
        float timeout = 30f;
        float elapsed = 0f;
        while (UnitActionSystem.Instance == null && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (UnitActionSystem.Instance == null)
        {
            Debug.LogWarning("[NetworkedPlayerBridge] UnitActionSystem never found.");
            yield break;
        }

        // Wait for the unit to be placed in a room.
        elapsed = 0f;
        while (!unit.IsInitialized() && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (!unit.IsInitialized())
        {
            Debug.LogWarning("[NetworkedPlayerBridge] Unit never initialized.");
            yield break;
        }

        // Register with local systems.
        UnitActionSystem.Instance.SetSelectedUnit(unit);

        // Register PlayerTarget so camera and enemies use the right player.
        var pt = GetComponent<PlayerTarget>();
        if (pt == null) pt = gameObject.AddComponent<PlayerTarget>();
        PlayerTarget.ForceRegister(pt, unit);

        // Snap camera to our player.
        CameraController2D.Instance?.SnapToTarget();

        // Set RoomManager to our current room.
        var roomGrid = unit.GetCurrentRoomGrid();
        if (roomGrid != null)
        {
            var placed = FindPlacedRoomForGrid(roomGrid);
            if (placed != null)
                RoomManager.Instance?.SetCurrentRoom(placed);
        }

        Debug.Log($"[NetworkedPlayerBridge] Owner systems wired for {gameObject.name}");
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public string       GetCurrentRoomName()     => currentRoomName.Value.ToString();
    public GridPosition GetNetworkGridPosition()  => new(gridX.Value, gridY.Value);

    // Called by MoveAction at end of move coroutine (owner only).
    public void SyncGridPosition(RoomGrid room, GridPosition pos)
    {
        if (!IsOwner || isTransitioning) return;
        RequestMoveServerRpc(room.gameObject.name, pos.x, pos.y);
    }

    // Called by NetworkedPlayerSpawner after initial spawn (server only).
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
        if (IsServer) return;

        ApplyRoomSync(roomName, x, y);

        if (IsOwner)
        {
            var room = FindRoomGridByName(roomName);
            if (room != null)
            {
                var placed = FindPlacedRoomForGrid(room);
                if (placed != null) RoomManager.Instance?.SetCurrentRoom(placed);
            }
            CameraController2D.Instance?.SnapToTarget();
            Debug.Log($"[NetworkedPlayerBridge] Owner placed in {roomName} at ({x},{y})");
        }
    }

    // Called by hallway triggers / minimap buttons in multiplayer.
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
        if (IsOwner) return; // Owner already applied locally.

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
        string name = newVal.ToString();
        if (!string.IsNullOrEmpty(name)) QueueApplyRoomSync();
    }

    private void OnGridPositionChanged(int oldVal, int newVal) => QueueApplyRoomSync();

    private void QueueApplyRoomSync()
    {
        if (syncPending) return;
        syncPending   = true;
        syncCoroutine = StartCoroutine(ApplyRoomSyncNextFrame());
    }

    private IEnumerator ApplyRoomSyncNextFrame()
    {
        yield return new WaitForEndOfFrame();
        syncPending   = false;
        syncCoroutine = null;

        string roomName = currentRoomName.Value.ToString();
        if (!string.IsNullOrEmpty(roomName))
            ApplyRoomSync(roomName, gridX.Value, gridY.Value);
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

    // ── Damage RPCs ────────────────────────────────────────────────────────

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
                NetworkedHealthBridge.TakeDamage(target.gameObject, damage);
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
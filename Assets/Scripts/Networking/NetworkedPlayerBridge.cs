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

        // Non-owners apply whatever position the server already has.
        if (!IsOwner)
        {
            string existing = currentRoomName.Value.ToString();
            if (!string.IsNullOrEmpty(existing))
                ApplyRoomSync(existing, gridX.Value, gridY.Value);
        }

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

    private IEnumerator WireUpOwnerSystems()
    {
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

        elapsed = 0f;
        while (elapsed < timeout)
        {
            var  netObj             = GetComponent<NetworkObject>();
            bool ownershipConfirmed = netObj != null && netObj.IsOwner;
            bool unitReady          = unit.IsInitialized();
            if (ownershipConfirmed && unitReady) break;
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

        if (!unit.IsInitialized())
        {
            Debug.LogWarning("[NetworkedPlayerBridge] Unit never initialized.");
            yield break;
        }

        // Snap transform to correct world pos (ApplyRoomSync skips transform).
        var currentRoom = unit.GetCurrentRoomGrid();
        if (currentRoom != null)
        {
            Vector2 vo    = unit.GetVisualOffset();
            Vector3 world = currentRoom.GetWorldPosition(unit.GetGridPosition());
            unit.transform.position = new Vector3(
                world.x + vo.x,
                world.y + vo.y,
                unit.transform.position.z);
        }

        UnitActionSystem.Instance.SetSelectedUnit(unit);

        var pt = GetComponent<PlayerTarget>();
        if (pt == null) pt = gameObject.AddComponent<PlayerTarget>();
        PlayerTarget.ForceRegister(pt, unit);

        CameraController2D.Instance?.SnapToTarget();

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

    public string       GetCurrentRoomName()    => currentRoomName.Value.ToString();
    public GridPosition GetNetworkGridPosition() => new(gridX.Value, gridY.Value);

    /// <summary>Called by MoveAction at end of move — owner only.</summary>
    public void SyncGridPosition(RoomGrid room, GridPosition pos)
    {
        if (!IsOwner || isTransitioning) return;
        RequestMoveServerRpc(room.gameObject.name, pos.x, pos.y);
    }

    /// <summary>Called by NetworkedPlayerSpawner after initial spawn — server only.</summary>
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
                Vector2 vo    = unit.GetVisualOffset();
                Vector3 world = room.GetWorldPosition(new GridPosition(x, y));
                unit.transform.position = new Vector3(
                    world.x + vo.x,
                    world.y + vo.y,
                    unit.transform.position.z);

                var placed = FindPlacedRoomForGrid(room);
                if (placed != null) RoomManager.Instance?.SetCurrentRoom(placed);
            }
            CameraController2D.Instance?.SnapToTarget();
        }
    }

    /// <summary>
    /// Called when the player crosses into a new room via hallway trigger or minimap.
    /// Owner-only: tells the server where this player ended up.
    /// </summary>
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

        // Tell server this player entered this room — server will spawn
        // enemies and lock exits if needed.
        RequestRoomTransitionServerRpc(newRoom.gameObject.name, spawnPos.x, spawnPos.y);
    }

    // ── Server RPCs ────────────────────────────────────────────────────────

    [ServerRpc]
    private void RequestMoveServerRpc(string roomName, int x, int y)
    {
        currentRoomName.Value = roomName;
        gridX.Value           = x;
        gridY.Value           = y;
        // No ClientRpc needed — NetworkVariable change propagates automatically.
    }

    [ServerRpc]
    private void RequestRoomTransitionServerRpc(string roomName, int spawnX, int spawnY)
    {
        currentRoomName.Value = roomName;
        gridX.Value           = spawnX;
        gridY.Value           = spawnY;

        // Broadcast visual transition to non-owner clients.
        BroadcastRoomTransitionClientRpc(roomName, spawnX, spawnY);

        // Server: handle enemy spawn + door lock for this room.
        HandleRoomEntryOnServer(roomName);
    }

    /// <summary>
    /// Server-side logic when a player enters a room:
    ///   1. Spawn enemies if the room hasn't been cleared.
    ///   2. Lock exits (one-way: players can ENTER but not LEAVE while enemies live).
    ///   3. Notify all clients of the lock state.
    /// </summary>
    private void HandleRoomEntryOnServer(string roomName)
    {
        if (!IsServer) return;

        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return;

        LevelGenerator.PlacedRoom placed = null;
        foreach (var r in gen.GetAllRooms())
        {
            if (r.roomGrid != null && r.roomGrid.gameObject.name == roomName)
            { placed = r; break; }
        }

        if (placed == null) return;
        if (placed.prefabData.roomType == LevelGenerator.RoomType.Start) return;
        if (placed.roomGrid.HasBeenCleared) return;

        // Spawn enemies if not already present.
        bool hadEnemies = EnemyManager.Instance != null &&
                          EnemyManager.Instance.GetEnemiesInRoom(placed.roomGrid).Count > 0;

        if (!hadEnemies)
        {
            var spawner = FindAnyObjectByType<EnemySpawner>();
            spawner?.SpawnForRoom(placed);
            hadEnemies = EnemyManager.Instance != null &&
                         EnemyManager.Instance.GetEnemiesInRoom(placed.roomGrid).Count > 0;
        }

        if (hadEnemies)
        {
            // Lock exits on all clients.
            LockRoomExitsClientRpc(roomName);

            // Subscribe to room cleared so we can unlock when enemies die.
            if (EnemyManager.Instance != null)
            {
                EnemyManager.Instance.OnRoomCleared -= OnRoomClearedServer;
                EnemyManager.Instance.OnRoomCleared += OnRoomClearedServer;
            }

            Debug.Log($"[NetworkedPlayerBridge] Server: locked exits for {roomName}");
        }
    }

    private void OnRoomClearedServer(RoomGrid clearedRoom)
    {
        if (!IsServer) return;
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= OnRoomClearedServer;

        UnlockRoomExitsClientRpc(clearedRoom.gameObject.name);
        Debug.Log($"[NetworkedPlayerBridge] Server: unlocked exits for {clearedRoom.name}");
    }

    // ── Client RPCs ────────────────────────────────────────────────────────

    [ClientRpc]
    private void BroadcastRoomTransitionClientRpc(string roomName, int spawnX, int spawnY)
    {
        // Owner already applied locally in TransitionToRoom.
        if (IsOwner) return;

        ApplyRoomSync(roomName, spawnX, spawnY);
    }

    /// <summary>
    /// Closes doors and locks hallway walk triggers for a room on all clients.
    /// Players already inside cannot leave; players outside can still enter.
    /// </summary>
    [ClientRpc]
    private void LockRoomExitsClientRpc(string roomName)
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return;

        LevelGenerator.PlacedRoom placed = null;
        foreach (var r in gen.GetAllRooms())
        {
            if (r.roomGrid != null && r.roomGrid.gameObject.name == roomName)
            { placed = r; break; }
        }

        if (placed == null) return;

        // Close connected doors visually.
        var connectedDirs = new System.Collections.Generic.List<LevelGenerator.Direction>();
        foreach (LevelGenerator.Direction dir in
            System.Enum.GetValues(typeof(LevelGenerator.Direction)))
        {
            if (gen.GetConnectedRoom(placed, dir) != null)
                connectedDirs.Add(dir);
        }
        placed.connector?.CloseConnectedDoors(connectedDirs);

        // Lock walk triggers whose hallways touch this room
        // so players inside CANNOT walk out.
        foreach (var et in FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None))
        {
            if (et.pairedWalkTrigger == null) continue;

            bool hallwayTouchesRoom = false;
            if (et.Hallway != null)
            {
                var roomA = et.Hallway.RoomA?.roomGrid;
                var roomB = et.Hallway.RoomB?.roomGrid;
                if (roomA != null && roomA.gameObject.name == roomName) hallwayTouchesRoom = true;
                if (roomB != null && roomB.gameObject.name == roomName) hallwayTouchesRoom = true;
            }
            if (!hallwayTouchesRoom) continue;

            // Lock ONLY the walk trigger that leads OUT of this room.
            // The entry trigger on the far side (leading IN) stays unlocked.
            et.SetExitLocked(true);
        }

        // Switch camera to combat mode for any player currently in this room.
        var localUnit = UnitActionSystem.FindLocalOwnedUnit();
        if (localUnit != null &&
            localUnit.GetCurrentRoomGrid()?.gameObject.name == roomName)
        {
            CameraController2D.Instance?.SetCombatState(true);
        }

        Debug.Log($"[NetworkedPlayerBridge] Client: exits locked for {roomName}");
    }

    [ClientRpc]
    private void UnlockRoomExitsClientRpc(string roomName)
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return;

        LevelGenerator.PlacedRoom placed = null;
        foreach (var r in gen.GetAllRooms())
        {
            if (r.roomGrid != null && r.roomGrid.gameObject.name == roomName)
            { placed = r; break; }
        }

        if (placed != null)
        {
            var connectedDirs = new System.Collections.Generic.List<LevelGenerator.Direction>();
            foreach (LevelGenerator.Direction dir in
                System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            {
                if (gen.GetConnectedRoom(placed, dir) != null)
                {
                    connectedDirs.Add(dir);
                    placed.roomGrid.SetDoorState(dir, true);
                }
            }
            placed.connector?.OpenConnectedDoors(connectedDirs);
            placed.roomGrid.MarkCleared();
            RoomManager.Instance?.NotifyRoomCleared(placed);
        }

        // Unlock all walk triggers touching this room.
        foreach (var et in FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None))
        {
            bool touches = false;
            if (et.Hallway != null)
            {
                var roomA = et.Hallway.RoomA?.roomGrid;
                var roomB = et.Hallway.RoomB?.roomGrid;
                if (roomA != null && roomA.gameObject.name == roomName) touches = true;
                if (roomB != null && roomB.gameObject.name == roomName) touches = true;
            }
            if (et.DestinationRoom?.roomGrid?.gameObject.name == roomName) touches = true;
            if (!touches) continue;

            et.SetExitLocked(false);
            et.ResetTrigger();
        }

        // Move cache needs rebuilding now that doors are open.
        var localUnit = UnitActionSystem.FindLocalOwnedUnit();
        localUnit?.GetMoveAction()?.InvalidateCache();

        CameraController2D.Instance?.SetCombatState(false);

        Debug.Log($"[NetworkedPlayerBridge] Client: exits unlocked for {roomName}");
    }

    // ── NetworkVariable callbacks — NON-OWNER ONLY ─────────────────────────

    private void OnRoomNameChanged(FixedString64Bytes oldVal, FixedString64Bytes newVal)
    {
        // CRITICAL: owners write these vars — they must NOT react to their own writes
        // or they will rubber-band back to the last synced position mid-move.
        if (IsOwner) return;
        string name = newVal.ToString();
        if (!string.IsNullOrEmpty(name)) QueueApplyRoomSync();
    }

    private void OnGridPositionChanged(int oldVal, int newVal)
    {
        if (IsOwner) return; // same guard
        QueueApplyRoomSync();
    }

    private void QueueApplyRoomSync()
    {
        if (syncPending) return;
        syncPending   = true;
        syncCoroutine = StartCoroutine(ApplyRoomSyncNextFrame());
    }

    private System.Collections.IEnumerator ApplyRoomSyncNextFrame()
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

    // ── Class sync ─────────────────────────────────────────────────────────

    [ClientRpc]
    public void SetPlayerClassClientRpc(int classIndex)
    {
        if (IsServer) return;
        var stats = GetComponent<PlayerStats>();
        if (stats != null)
            stats.InitializeWithClass((PlayerClass)classIndex);
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
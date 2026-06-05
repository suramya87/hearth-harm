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

    private Unit      unit;
    private bool      syncPending;
    private Coroutine syncCoroutine;
    private Coroutine stepLerpCoroutine;
    private bool      isWalking;

    private string registeredCombatRoom = "";

    private void Awake() => unit = GetComponent<Unit>();

    // ── Network lifecycle ──────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!GameManager.IsMultiplayer) return;

        currentRoomName.OnValueChanged += OnRoomNameChanged;
        gridX.OnValueChanged           += OnGridPositionChanged;
        gridY.OnValueChanged           += OnGridPositionChanged;

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

        if (syncCoroutine     != null) { StopCoroutine(syncCoroutine);     syncCoroutine     = null; }
        if (stepLerpCoroutine != null) { StopCoroutine(stepLerpCoroutine); stepLerpCoroutine = null; }
        syncPending = false;
        isWalking   = false;

        if (IsServer && !string.IsNullOrEmpty(registeredCombatRoom))
        {
            NetworkedTurnSystem.Instance?.UnregisterPlayerFromRoomCombat(
                OwnerClientId, registeredCombatRoom);
            registeredCombatRoom = "";
        }
    }

    // ── Owner setup ────────────────────────────────────────────────────────

    private IEnumerator WireUpOwnerSystems()
    {
        float timeout = 30f, elapsed = 0f;

        while (UnitActionSystem.Instance == null && elapsed < timeout)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;
        }

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

        SnapTransformToGridPosition();

        if (UnitActionSystem.Instance != null)
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

    private void SnapTransformToGridPosition()
    {
        var room = unit.GetCurrentRoomGrid();
        if (room == null) return;

        Vector2 vo    = unit.GetVisualOffset();
        Vector3 world = room.GetWorldPosition(unit.GetGridPosition());
        unit.transform.position = new Vector3(
            world.x + vo.x,
            world.y + vo.y,
            unit.transform.position.z);
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public string       GetCurrentRoomName()    => currentRoomName.Value.ToString();
    public GridPosition GetNetworkGridPosition() => new(gridX.Value, gridY.Value);

    public void SetWalking(bool walking) => isWalking = walking;

    public void SyncGridPosition(RoomGrid room, GridPosition pos)
    {
        if (!IsOwner) return;
        isWalking = false;
        RequestMoveServerRpc(room.gameObject.name, pos.x, pos.y);
    }

    public void BroadcastMoveStep(Vector3 worldPos)
    {
        if (!IsOwner) return;
        BroadcastMoveStepClientRpc(worldPos.x, worldPos.y);
    }

    public void InitialPlacement(RoomGrid room, GridPosition pos)
    {
        if (!IsServer) return;

        currentRoomName.Value = room.gameObject.name;
        gridX.Value           = pos.x;
        gridY.Value           = pos.y;

        InitialPlacementClientRpc(room.gameObject.name, pos.x, pos.y);
    }

    [ClientRpc]
    private void InitialPlacementClientRpc(string roomName, int x, int y)
    {
        if (IsServer) return;

        var room = FindRoomGridByName(roomName);
        if (room == null)
        {
            Debug.LogWarning($"[NetworkedPlayerBridge] InitialPlacement: room '{roomName}' not found.");
            return;
        }

        unit.IsSyncingFromNetwork = true;
        unit.PlaceInRoom(room, new GridPosition(x, y));
        unit.IsSyncingFromNetwork = false;

        if (IsOwner)
        {
            SnapTransformToGridPosition();
            var placed = FindPlacedRoomForGrid(room);
            if (placed != null) RoomManager.Instance?.SetCurrentRoom(placed);
            CameraController2D.Instance?.SnapToTarget();
            Debug.Log($"[NetworkedPlayerBridge] Owner initial placement: {roomName} ({x},{y})");
        }
    }

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

        isWalking = false;
        if (stepLerpCoroutine != null) { StopCoroutine(stepLerpCoroutine); stepLerpCoroutine = null; }

        unit.PlaceInRoom(newRoom, spawnPos);

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
        HandleRoomEntryOnServer(roomName);
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestJoinActiveCombatServerRpc(string roomName, int spawnX, int spawnY,
        ServerRpcParams rpcParams = default)
    {
        ulong callerId = rpcParams.Receive.SenderClientId;

        if (!string.IsNullOrEmpty(registeredCombatRoom) && registeredCombatRoom != roomName)
        {
            NetworkedTurnSystem.Instance?.UnregisterPlayerFromRoomCombat(
                callerId, registeredCombatRoom);
        }

        currentRoomName.Value = roomName;
        gridX.Value           = spawnX;
        gridY.Value           = spawnY;

        NetworkedTurnSystem.Instance?.RegisterPlayerInRoomCombat(callerId, roomName);
        registeredCombatRoom = roomName;

        BroadcastRoomTransitionClientRpc(roomName, spawnX, spawnY);

        LockRoomExitsClientRpc(roomName);

        Debug.Log($"[NetworkedPlayerBridge] Client {callerId} joined active combat in '{roomName}'.");
    }

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

        bool hadEnemies = EnemyManager.Instance != null &&
                          EnemyManager.Instance.GetEnemiesInRoom(placed.roomGrid).Count > 0;

        if (!hadEnemies)
        {
            var spawner = FindAnyObjectByType<EnemySpawner>();
            spawner?.SpawnForRoom(placed);
            hadEnemies = EnemyManager.Instance != null &&
                         EnemyManager.Instance.GetEnemiesInRoom(placed.roomGrid).Count > 0;
        }

        if (!hadEnemies) return;

        if (!string.IsNullOrEmpty(registeredCombatRoom) && registeredCombatRoom != roomName)
        {
            NetworkedTurnSystem.Instance?.UnregisterPlayerFromRoomCombat(
                OwnerClientId, registeredCombatRoom);
        }

        NetworkedTurnSystem.Instance?.RegisterPlayerInRoomCombat(OwnerClientId, roomName);
        registeredCombatRoom = roomName;

        bool roomAlreadyInCombat = NetworkedTurnSystem.Instance != null &&
            NetworkedTurnSystem.Instance.IsRoomInCombat(roomName);

        if (!roomAlreadyInCombat)
        {
            LockRoomExitsClientRpc(roomName);

            if (EnemyManager.Instance != null)
            {
                EnemyManager.Instance.OnRoomCleared -= OnRoomClearedServer;
                EnemyManager.Instance.OnRoomCleared += OnRoomClearedServer;
            }
        }
        else
        {
            Debug.Log($"[NetworkedPlayerBridge] Room '{roomName}' already in combat — " +
                      $"skipping re-lock for client {OwnerClientId}.");
        }
    }

    private void OnRoomClearedServer(RoomGrid clearedRoom)
    {
        if (!IsServer) return;
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= OnRoomClearedServer;

        // Clear combat tracking for this room in the turn system
        NetworkedTurnSystem.Instance?.ClearRoomCombat(clearedRoom.gameObject.name);

        if (registeredCombatRoom == clearedRoom.gameObject.name)
            registeredCombatRoom = "";

        UnlockRoomExitsClientRpc(clearedRoom.gameObject.name);
    }

    // ── Client RPCs ────────────────────────────────────────────────────────

    [ClientRpc]
    private void BroadcastMoveStepClientRpc(float wx, float wy)
    {
        if (IsOwner) return;

        Vector2 vo     = unit.GetVisualOffset();
        Vector3 target = new Vector3(wx + vo.x, wy + vo.y, unit.transform.position.z);

        if (stepLerpCoroutine != null) StopCoroutine(stepLerpCoroutine);
        stepLerpCoroutine = StartCoroutine(LerpToWorldPos(target));
    }

    private IEnumerator LerpToWorldPos(Vector3 target)
    {
        const float speed = 8f;
        while (Vector2.Distance(unit.transform.position, target) > 0.01f)
        {
            unit.transform.position = Vector3.MoveTowards(
                unit.transform.position, target, speed * Time.deltaTime);
            yield return null;
        }
        unit.transform.position = target;
        stepLerpCoroutine = null;
    }

    [ClientRpc]
    private void BroadcastRoomTransitionClientRpc(string roomName, int spawnX, int spawnY)
    {
        if (IsOwner) return;

        if (stepLerpCoroutine != null) { StopCoroutine(stepLerpCoroutine); stepLerpCoroutine = null; }

        var room = FindRoomGridByName(roomName);
        if (room == null) return;

        unit.IsSyncingFromNetwork = true;
        unit.PlaceInRoom(room, new GridPosition(spawnX, spawnY));
        unit.IsSyncingFromNetwork = false;
    }

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

        if (placed != null)
        {
            foreach (LevelGenerator.Direction dir in
                System.Enum.GetValues(typeof(LevelGenerator.Direction)))
            {
                if (gen.GetConnectedRoom(placed, dir) == null) continue;

                placed.connector?.SetDoorOpen(dir, false);

                placed.connector?.SetDoorPassable(dir, true);

            }
        }

        foreach (var et in FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None))
        {
            bool touchesCombatRoom = false;
            if (et.Hallway != null)
            {
                var roomA = et.Hallway.RoomA?.roomGrid;
                var roomB = et.Hallway.RoomB?.roomGrid;
                if (roomA != null && roomA.gameObject.name == roomName) touchesCombatRoom = true;
                if (roomB != null && roomB.gameObject.name == roomName) touchesCombatRoom = true;
            }
            if (!touchesCombatRoom) continue;

            bool destinationIsCombatRoom =
                et.DestinationRoom?.roomGrid?.gameObject.name == roomName;

            if (destinationIsCombatRoom)
            {
                et.SetDestinationIsActiveCombat(true);
            }
            else
            {
                et.SetExitLocked(true);
                et.SetDestinationIsActiveCombat(false);
            }
        }

        var localUnit = UnitActionSystem.FindLocalOwnedUnit();
        if (localUnit == null) return;
        string localRoomName = localUnit.GetCurrentRoomGrid()?.gameObject.name;
        if (localRoomName == roomName)
            CameraController2D.Instance?.SetCombatState(true);
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
                if (gen.GetConnectedRoom(placed, dir) == null) continue;
                connectedDirs.Add(dir);
                placed.roomGrid.SetDoorState(dir, true);

                placed.connector?.SetDoorPassable(dir, false);
                placed.connector?.SetDoorOpen(dir, true);
            }
            placed.roomGrid.MarkCleared();
            RoomManager.Instance?.NotifyRoomCleared(placed);
        }

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
            et.SetDestinationIsActiveCombat(false);
            et.ResetTrigger();
        }

        var localUnit = UnitActionSystem.FindLocalOwnedUnit();
        if (localUnit == null) return;

        string localRoomName = localUnit.GetCurrentRoomGrid()?.gameObject.name;
        if (localRoomName == roomName || IsAdjacentToRoom(roomName, localRoomName, gen))
        {
            localUnit.GetMoveAction()?.InvalidateCache();
            if (localRoomName == roomName)
                CameraController2D.Instance?.SetCombatState(false);
        }
    }

    private static bool IsAdjacentToRoom(string combatRoomName, string localRoomName,
        LevelGenerator gen)
    {
        if (gen == null || string.IsNullOrEmpty(localRoomName)) return false;
        LevelGenerator.PlacedRoom placed = null;
        foreach (var r in gen.GetAllRooms())
        {
            if (r.roomGrid != null && r.roomGrid.gameObject.name == combatRoomName)
            { placed = r; break; }
        }
        if (placed == null) return false;
        foreach (LevelGenerator.Direction dir in
            System.Enum.GetValues(typeof(LevelGenerator.Direction)))
        {
            var neighbour = gen.GetConnectedRoom(placed, dir);
            if (neighbour != null && neighbour.roomGrid?.gameObject.name == localRoomName)
                return true;
        }
        return false;
    }

    // ── NetworkVariable callbacks — NON-OWNER ONLY ─────────────────────────

    private void OnRoomNameChanged(FixedString64Bytes oldVal, FixedString64Bytes newVal)
    {
        if (IsOwner) return;
        string name = newVal.ToString();
        if (!string.IsNullOrEmpty(name)) QueueApplyRoomSync();
    }

    private void OnGridPositionChanged(int oldVal, int newVal)
    {
        if (IsOwner) return;
        QueueApplyRoomSync();
    }

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

    private void ApplyRoomSync(string roomName, int x, int y)
    {
        var room = FindRoomGridByName(roomName);
        if (room == null)
        {
            Debug.LogWarning($"[NetworkedPlayerBridge] ApplyRoomSync: room '{roomName}' not found.");
            return;
        }

        if (stepLerpCoroutine != null) { StopCoroutine(stepLerpCoroutine); stepLerpCoroutine = null; }

        unit.IsSyncingFromNetwork = isWalking;
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
        var unitRoom = unit.GetCurrentRoomGrid();
        if (unitRoom == null) return;

        RoomGrid room = unitRoom;
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen != null)
        {
            string roomName = unitRoom.gameObject.name;
            foreach (var placed in gen.GetAllRooms())
            {
                if (placed.roomGrid != null &&
                    placed.roomGrid.gameObject.name == roomName)
                {
                    room = placed.roomGrid;
                    break;
                }
            }
        }

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
using System.Collections;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(EnemyUnit))]
public class NetworkedEnemyBridge : NetworkBehaviour
{
    [SerializeField] private float moveAnimDuration = 0.18f;

    private EnemyUnit enemyUnit;
    private Coroutine moveCoroutine;

    private void Awake() => enemyUnit = GetComponent<EnemyUnit>();

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            StartCoroutine(SyncOnSpawn());
    }

    private IEnumerator SyncOnSpawn()
    {
        yield return null;

        RoomGrid room = null;
        string existingRoomName = enemyUnit.CurrentRoomGrid?.gameObject.name ?? "";

        if (!string.IsNullOrEmpty(existingRoomName))
            room = FindRoomByName(existingRoomName);

        if (room == null)
        {
            var gen = FindAnyObjectByType<LevelGenerator>();
            if (gen != null)
            {
                foreach (var placed in gen.GetAllRooms())
                {
                    if (placed.roomGrid == null) continue;
                    if (placed.roomGrid.IsPositionInRoom(transform.position))
                    { room = placed.roomGrid; break; }
                }
            }
        }

        if (room == null)
        {
            Debug.LogWarning($"[NetworkedEnemyBridge] Could not find room for {gameObject.name} on client.");
            yield break;
        }

        var gp = room.GetGridPosition(transform.position);
        enemyUnit.SyncRoomGrid(room);
        room.AddEnemyAtGridPosition(gp, enemyUnit);
        EnemyManager.Instance?.RegisterEnemy(enemyUnit);

        Debug.Log($"[NetworkedEnemyBridge] Client synced {gameObject.name} into {room.gameObject.name} at {gp}");
    }


    public void ServerBroadcastMove(GridPosition newPos, RoomGrid room)
    {
        if (!IsServer) return;

        Vector3 world = room.GetWorldPosition(newPos);
        BroadcastMoveClientRpc(
            newPos.x, newPos.y,
            world.x,  world.y,  world.z,
            room.gameObject.name);
    }

    public void ServerBroadcastAttack(Vector3 targetWorldPos)
    {
        if (!IsServer) return;
        BroadcastAttackClientRpc(targetWorldPos.x, targetWorldPos.y, targetWorldPos.z);
    }

    // ── Client RPCs ────────────────────────────────────────────────────────

    [ClientRpc]
    private void BroadcastMoveClientRpc(
        int    gx,  int   gy,
        float  wx,  float wy,  float wz,
        string roomName)
    {
        if (IsServer) return;

        var room = FindRoomByName(roomName);
        if (room != null)
        {
            if (enemyUnit.IsInitialized)
                room.RemoveEnemyAtGridPosition(enemyUnit.GridPosition, enemyUnit);
            room.AddEnemyAtGridPosition(new GridPosition(gx, gy), enemyUnit);
            enemyUnit.SyncRoomGrid(room);
        }

        var dest = new Vector3(wx, wy, transform.position.z);
        if (moveCoroutine != null) StopCoroutine(moveCoroutine);
        moveCoroutine = StartCoroutine(AnimateMove(dest));
    }

    private IEnumerator AnimateMove(Vector3 destination)
    {
        Vector3 start   = transform.position;
        float   elapsed = 0f;

        while (elapsed < moveAnimDuration)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.Lerp(start, destination, elapsed / moveAnimDuration);
            yield return null;
        }

        transform.position = destination;
        moveCoroutine = null;
    }

    [ClientRpc]
    private void BroadcastAttackClientRpc(float wx, float wy, float wz)
    {
        if (IsServer) return;
        Debug.Log($"[NetworkedEnemyBridge] Enemy attack at ({wx:F1},{wy:F1})");
    }

    [ClientRpc]
    public void NotifyDeathClientRpc()
    {
        if (IsServer) return;
        if (enemyUnit.IsInitialized && enemyUnit.CurrentRoomGrid != null)
        {
            enemyUnit.CurrentRoomGrid.RemoveEnemyAtGridPosition(
                enemyUnit.GridPosition, enemyUnit);
        }
        EnemyManager.Instance?.UnregisterEnemy(enemyUnit);

        var animator = GetComponent<Animator>();
        if (animator != null)
            animator.SetBool("isDead", true);

        Debug.Log($"[NetworkedEnemyBridge] {gameObject.name} death on client.");
    }

    // ── Multiplayer player targeting ───────────────────────────────────────

    public static Unit FindNearestPlayerInRoom(RoomGrid room, GridPosition from)
    {
        if (room == null) return null;

        if (!GameManager.IsMultiplayer)
        {
            var pt = PlayerTarget.Instance;
            return (pt != null && pt.IsInRoom(room)) ? pt.GetUnit() : null;
        }

        string roomName = room.gameObject.name;
        Unit   nearest  = null;
        int    bestDist = int.MaxValue;

        foreach (var bridge in FindObjectsByType<NetworkedPlayerBridge>(FindObjectsSortMode.None))
        {
            var u = bridge.GetComponent<Unit>();
            if (u == null) continue;

            var hp = u.GetComponent<HealthComponent>();
            if (hp != null && hp.IsDead) continue;

            var playerRoom = u.GetCurrentRoomGrid();
            if (playerRoom == null) continue;

            if (playerRoom.gameObject.name != roomName) continue;

            int d = from.ManhattanDistance(u.GetGridPosition());
            if (d < bestDist) { bestDist = d; nearest = u; }
        }

        return nearest;
    }

    // ── Helper ─────────────────────────────────────────────────────────────

    private static RoomGrid FindRoomByName(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return null;
        foreach (var placed in gen.GetAllRooms())
            if (placed.roomGrid != null && placed.roomGrid.gameObject.name == name)
                return placed.roomGrid;
        return null;
    }
}
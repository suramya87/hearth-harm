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
        // Nothing extra needed — server drives all logic.
    }

    // ── Called by EnemyUnit.MoveToPosition() on server ─────────────────────

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
        int   gx,   int   gy,
        float wx,   float wy,   float wz,
        string roomName)
    {
        if (IsServer) return; // server already applied

        var room = FindRoomByName(roomName);

        if (room != null)
        {
            if (enemyUnit.IsInitialized)
                room.RemoveEnemyAtGridPosition(enemyUnit.GridPosition, enemyUnit);
            room.AddEnemyAtGridPosition(new GridPosition(gx, gy), enemyUnit);
        }

        // Animate toward the new world position
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

        var animator = GetComponent<Animator>();
        if (animator != null)
            animator.SetBool("isDead", true);

        Debug.Log($"[NetworkedEnemyBridge] {gameObject.name} death on client.");
    }

    // ── Multiplayer player targeting ───────────────────────────────────────

    public static Unit FindNearestPlayerInRoom(RoomGrid room, GridPosition from)
    {
        if (!GameManager.IsMultiplayer)
        {
            var pt = PlayerTarget.Instance;
            return (pt != null && pt.IsInRoom(room)) ? pt.GetUnit() : null;
        }

        Unit   nearest = null;
        int    bestDist = int.MaxValue;

        foreach (var bridge in FindObjectsByType<NetworkedPlayerBridge>(FindObjectsSortMode.None))
        {
            var unit = bridge.GetComponent<Unit>();
            if (unit == null) continue;

            var hp = unit.GetComponent<HealthComponent>();
            if (hp != null && hp.IsDead) continue;

            if (unit.GetCurrentRoomGrid() != room) continue;

            int d = from.ManhattanDistance(unit.GetGridPosition());
            if (d < bestDist) { bestDist = d; nearest = unit; }
        }

        return nearest;
    }

    // ── Helper ─────────────────────────────────────────────────────────────

    private static RoomGrid FindRoomByName(string name)
    {
        var gen = FindAnyObjectByType<LevelGenerator>();
        if (gen == null) return null;
        foreach (var placed in gen.GetAllRooms())
            if (placed.roomGrid != null && placed.roomGrid.gameObject.name == name)
                return placed.roomGrid;
        return null;
    }
}
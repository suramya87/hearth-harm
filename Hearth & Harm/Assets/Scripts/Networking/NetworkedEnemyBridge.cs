using Unity.Netcode;
using UnityEngine;


[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(EnemyUnit))]
public class NetworkedEnemyBridge : NetworkBehaviour
{
    private EnemyUnit enemyUnit;

    private void Awake()
    {
        enemyUnit = GetComponent<EnemyUnit>();
    }

    public override void OnNetworkSpawn()
    {
        if (!GameManager.IsMultiplayer) return;

    }

    // ── Called by server after EnemyUnit.MoveToPosition() ─────────────────


    public void ServerBroadcastMove(GridPosition newPos, RoomGrid room)
    {
        if (!IsServer) return;

        Vector3 worldPos = room.GetWorldPosition(newPos);
        BroadcastMoveClientRpc(newPos.x, newPos.y,
            worldPos.x, worldPos.y, worldPos.z,
            room.gameObject.name);
    }

    public void ServerBroadcastAttack(Vector3 targetWorldPos)
    {
        if (!IsServer) return;
        BroadcastAttackClientRpc(targetWorldPos.x, targetWorldPos.y, targetWorldPos.z);
    }

    // ── Client RPCs ────────────────────────────────────────────────────────

    [ClientRpc]
    private void BroadcastMoveClientRpc(int gx, int gy,
                                         float wx, float wy, float wz,
                                         string roomName)
    {
        if (IsServer) return;

        transform.position = new Vector3(wx, wy, wz);


        var room = FindRoomByName(roomName);
        if (room != null)
            enemyUnit.MoveToPosition(new GridPosition(gx, gy));
    }

    [ClientRpc]
    private void BroadcastAttackClientRpc(float wx, float wy, float wz)
    {
        if (IsServer) return;


        Debug.Log($"[NetworkedEnemyBridge] Enemy attack VFX at ({wx},{wy},{wz})");
    }

    [ClientRpc]
    public void NotifyDeathClientRpc()
    {
        if (IsServer) return;

        var animator = GetComponent<UnityEngine.Animator>();
        if (animator != null)
            animator.SetBool("isDead", true);

        Debug.Log($"[NetworkedEnemyBridge] Enemy death notified on client.");
    }

    // ── Helper ─────────────────────────────────────────────────────────────

    private RoomGrid FindRoomByName(string name)
    {
        foreach (var placed in FindAnyObjectByType<LevelGenerator>()?.GetAllRooms()
                 ?? new System.Collections.Generic.List<LevelGenerator.PlacedRoom>())
            if (placed.roomGrid != null && placed.roomGrid.gameObject.name == name)
                return placed.roomGrid;
        return null;
    }
}
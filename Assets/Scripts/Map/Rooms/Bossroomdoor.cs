using UnityEngine;

/// <summary>
/// Attached to the boss room's exit door strip by LevelGenerator.
/// Stays active (blocking) until all enemies in the boss room die.
/// </summary>
public class BossRoomDoor : MonoBehaviour
{
    private RoomGrid bossRoom;
    private bool     unlocked;

    public void Initialize(RoomGrid room)
    {
        bossRoom = room;
        gameObject.SetActive(true);   // start locked

        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared += OnRoomCleared;
    }

    private void OnDestroy()
    {
        if (EnemyManager.Instance != null)
            EnemyManager.Instance.OnRoomCleared -= OnRoomCleared;
    }

    private void OnRoomCleared(RoomGrid cleared)
    {
        if (unlocked || cleared != bossRoom) return;
        unlocked = true;
        gameObject.SetActive(false);
        Debug.Log("[BossRoomDoor] Exit unlocked!");
    }
}
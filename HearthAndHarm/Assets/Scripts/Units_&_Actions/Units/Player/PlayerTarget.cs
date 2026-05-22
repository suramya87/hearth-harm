using UnityEngine;

public class PlayerTarget : MonoBehaviour
{
    public static PlayerTarget Instance { get; private set; }

    private void Awake()     { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    public Unit     GetUnit()        => GetComponent<Unit>();
    public RoomGrid GetCurrentRoom() => GetComponent<Unit>()?.GetCurrentRoomGrid();


    public bool IsInRoom(RoomGrid room)
    {
        if (room == null) return false;
        var current = GetCurrentRoom();
        if (current == null) return false;
        return current == room ||
               current.gameObject.name == room.gameObject.name;
    }
}
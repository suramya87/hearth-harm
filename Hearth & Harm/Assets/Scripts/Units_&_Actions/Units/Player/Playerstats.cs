using UnityEngine;

/// <summary>
/// Singleton marker on the player prefab.
/// Enemy AI uses this to locate the player without a grid search.
/// </summary>
public class PlayerTarget : MonoBehaviour
{
    public static PlayerTarget Instance { get; private set; }

    private void Awake()  { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    public Unit     GetUnit()        => GetComponent<Unit>();
    public RoomGrid GetCurrentRoom() => GetComponent<Unit>()?.GetCurrentRoomGrid();
    public bool     IsInRoom(RoomGrid room) => room != null && GetCurrentRoom() == room;
}
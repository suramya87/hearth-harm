using UnityEngine;

/// <summary>
/// DEPRECATED — kept only so existing scene references don't break.
/// All grid switching and room activation logic has moved to HallwayRoomBridge.
/// HallwayBuilder no longer creates any of these triggers.
/// This component does nothing if it still exists in a scene.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class HallwayEntryTrigger : MonoBehaviour
{
    public HallwayGrid               Hallway         { get; private set; }
    public LevelGenerator.PlacedRoom DestinationRoom { get; private set; }
    public LevelGenerator.Direction  EntryDirection  { get; private set; }
    public bool                      IsHallwayEntry  { get; private set; }

    public void Initialize(
        HallwayGrid                  hallway,
        LevelGenerator.PlacedRoom    destinationRoom,
        LevelGenerator.Direction     entryDirection,
        bool                         isHallwayEntry)
    {
        Hallway         = hallway;
        DestinationRoom = destinationRoom;
        EntryDirection  = entryDirection;
        IsHallwayEntry  = isHallwayEntry;
    }

    public void ResetTrigger() { }

    // Intentionally empty — HallwayRoomBridge handles everything now.
    private void OnTriggerEnter2D(Collider2D other) { }
}
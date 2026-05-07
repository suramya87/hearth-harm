using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Placed at each hallway mouth on the room side.
/// When the player steps through it they are handed off to the hallway's
/// RoomGrid so MoveAction pathfinds on hallway tiles, and the camera
/// switches to free-follow mode (no room bounds clamping).
///
/// LOCK SUPPORT:
///   Call SetLocked(true)  to prevent the trigger from firing (e.g. enemies are alive).
///   Call SetLocked(false) to re-enable it (e.g. room cleared).
///   An optional door-strip GameObject can be supplied; it will be shown/hidden
///   in sync with the lock state so the player sees a physical barrier.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class HallwayWalkTrigger : MonoBehaviour
{
    /// <summary>
    /// Set by HallwayEntryTrigger before transitioning the player into a room.
    /// Prevents this trigger from pulling them back into the hallway while
    /// their physics collider is still overlapping the mouth zone.
    /// Cleared after a delay by HallwayEntryTrigger.
    /// </summary>
    public static bool EntryTransitionInProgress { get; set; }

    private HallwayGrid hallway;
    private bool        cooling;
    private bool        locked;

    /// <summary>
    /// Optional door-strip sprite/object shown when this trigger is locked.
    /// Assign via HallwayBuilder after creation.
    /// </summary>
    public GameObject DoorStripObject { get; set; }

    public void Initialize(HallwayGrid hg)
    {
        hallway = hg;
        GetComponent<Collider2D>().isTrigger = true;
    }

    public void ResetTrigger()
    {
        StopAllCoroutines();
        cooling = false;
    }

    /// <summary>
    /// Lock or unlock this mouth. While locked the player cannot enter the hallway
    /// from this side. The door-strip object (if any) mirrors the lock state.
    /// </summary>
    public void SetLocked(bool isLocked)
    {
        locked = isLocked;
        if (DoorStripObject != null)
            DoorStripObject.SetActive(isLocked);
    }

    // ── Trigger ────────────────────────────────────────────────────────────

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (cooling)  return;
        if (locked)   return;   // ← room has enemies; block exit
        if (EntryTransitionInProgress) return;
        if (!other.CompareTag("Player")) return;
        if (hallway == null || !hallway.IsReady) return;

        var unit = other.GetComponent<Unit>()
                ?? other.GetComponentInParent<Unit>();
        if (unit == null) return;

        if (GameManager.IsMultiplayer)
        {
            var bridge = unit.GetComponent<NetworkedPlayerBridge>();
            if (bridge == null || !bridge.IsOwner) return;
        }

        // Already on this hallway's grid — nothing to do
        if (unit.GetCurrentRoomGrid() == hallway.RoomGrid) return;

        StartCoroutine(HandOffAfterMove(unit));
    }

    // ── Hand off to hallway ────────────────────────────────────────────────

    private IEnumerator HandOffAfterMove(Unit unit)
    {
        cooling = true;

        // Wait for any in-progress move animation to finish
        var move = unit.GetMoveAction();
        if (move != null)
            while (move.IsActive) yield return null;

        yield return new WaitForSeconds(0.05f);

        // Re-check: another trigger may have acted while we waited
        if (unit.GetCurrentRoomGrid() == hallway.RoomGrid || EntryTransitionInProgress || locked)
        {
            cooling = false;
            yield break;
        }

        var roomGrid     = hallway.RoomGrid;
        var floorTilemap = roomGrid.GetFloorTilemap();

        if (floorTilemap == null)
        {
            Debug.LogError($"[HallwayWalkTrigger] No floor tilemap on {hallway.name}!");
            cooling = false;
            yield break;
        }

        GridPosition? bestGP = FindNearestWalkableToWorld(roomGrid, floorTilemap, transform.position)
                            ?? FindAnyWalkable(roomGrid, floorTilemap);

        if (bestGP == null)
        {
            Debug.LogError($"[HallwayWalkTrigger] No walkable cell found in {hallway.name}!");
            cooling = false;
            yield break;
        }

        // ── Switch to hallway ──────────────────────────────────────────────
        // Tell RoomManager the player is in a hallway — this clears room bounds
        // on the camera so it follows the player freely.
        RoomManager.Instance?.SetInHallway();

        // Move the unit onto the hallway grid
        unit.PlaceInRoom(roomGrid, bestGP.Value);

        Debug.Log($"[HallwayWalkTrigger] Player entered hallway '{hallway.name}' at {bestGP.Value}.");

        yield return new WaitForSeconds(0.5f);
        cooling = false;
    }

    // ── Tile search helpers ────────────────────────────────────────────────

    private static GridPosition? FindNearestWalkableToWorld(
        RoomGrid roomGrid, Tilemap floorTilemap, Vector3 targetWorld)
    {
        GridPosition? best     = null;
        float         bestDist = float.MaxValue;

        foreach (var cell in floorTilemap.cellBounds.allPositionsWithin)
        {
            if (!floorTilemap.HasTile(cell)) continue;
            var   gp   = new GridPosition(cell.x, cell.y);
            if (!roomGrid.IsWalkableIgnoreOccupancy(gp)) continue;
            float dist = Vector3.Distance(floorTilemap.GetCellCenterWorld(cell), targetWorld);
            if (dist < bestDist) { bestDist = dist; best = gp; }
        }

        return best;
    }

    private static GridPosition? FindAnyWalkable(RoomGrid roomGrid, Tilemap floorTilemap)
    {
        foreach (var cell in floorTilemap.cellBounds.allPositionsWithin)
        {
            if (!floorTilemap.HasTile(cell)) continue;
            var gp = new GridPosition(cell.x, cell.y);
            if (roomGrid.IsWalkableIgnoreOccupancy(gp)) return gp;
        }
        return null;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            cooling = false;
    }
}
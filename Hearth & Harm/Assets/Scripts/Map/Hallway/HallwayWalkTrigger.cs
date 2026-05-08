using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;


[RequireComponent(typeof(Collider2D))]
public class HallwayWalkTrigger : MonoBehaviour
{

    public static bool EntryTransitionInProgress { get; set; }

    private HallwayGrid hallway;
    private bool        cooling;
    private bool        locked;

    /// <summary>Optional door-strip shown when this trigger is locked.</summary>
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

    public void SetLocked(bool isLocked)
    {
        locked = isLocked;
        if (DoorStripObject != null)
            DoorStripObject.SetActive(isLocked);
    }

    // ── Trigger callbacks ──────────────────────────────────────────────────

    private void OnTriggerEnter2D(Collider2D other) => TryHandOff(other);

    /// <summary>
    /// Fires every physics tick while overlapping.
    /// Handles the case where the player is standing at the door when
    /// the enemy-lock releases — Enter won't re-fire, Stay will.
    /// </summary>
    private void OnTriggerStay2D(Collider2D other) => TryHandOff(other);

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            cooling = false;
    }

    // ── Shared hand-off logic ──────────────────────────────────────────────

    private void TryHandOff(Collider2D other)
    {
        if (cooling)  return;
        if (locked)   return;
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

        // Wait for any in-progress move to finish
        var move = unit.GetMoveAction();
        if (move != null)
            while (move.IsActive) yield return null;

        yield return new WaitForSeconds(0.05f);

        // Re-check — another trigger or a second Stay call may have already
        // handed off this unit.
        if (unit.GetCurrentRoomGrid() == hallway.RoomGrid ||
            EntryTransitionInProgress ||
            locked)
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

        GridPosition? bestGP =
            FindNearestWalkableToWorld(roomGrid, floorTilemap, transform.position)
            ?? FindAnyWalkable(roomGrid, floorTilemap);

        if (bestGP == null)
        {
            Debug.LogError($"[HallwayWalkTrigger] No walkable cell in {hallway.name}!");
            cooling = false;
            yield break;
        }

        // ── Switch to hallway ──────────────────────────────────────────────
        ApplyHallwayCameraBounds();

        RoomManager.Instance?.SetInHallway();

        // Place the unit on the hallway grid
        unit.PlaceInRoom(roomGrid, bestGP.Value);

        Debug.Log($"[HallwayWalkTrigger] '{unit.name}' entered hallway " +
                  $"'{hallway.name}' at {bestGP.Value}.");

        // Hold cooling long enough to ignore repeated Stay callbacks that fire
        // while the unit is still physically overlapping the trigger collider.
        yield return new WaitForSeconds(1f);
        cooling = false;
    }

    // ── Hallway camera bounds ──────────────────────────────────────────────

    /// <summary>
    /// Computes bounds from the hallway's floor tilemap and applies them to
    /// the camera. This gives the hallway the same feel as a room — the player
    /// can pan and zoom but the camera stays around the corridor.
    /// </summary>
    private void ApplyHallwayCameraBounds()
    {
        var cam = CameraController2D.Instance;
        if (cam == null || hallway == null) return;

        var floor = hallway.FloorTilemap;
        if (floor == null) { cam.ClearRoomBounds(); return; }

        // localBounds is in tilemap local space; convert to world space
        var lb     = floor.localBounds;
        var center = floor.transform.TransformPoint(lb.center);
        var size   = new Vector3(
            lb.size.x + 4f,   // add a few units of padding on each axis
            lb.size.y + 4f,
            1f);

        cam.SetRoomBounds(new Bounds(center, size));
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
}
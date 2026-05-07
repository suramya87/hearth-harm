using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Placed at each hallway mouth. When the player steps into the trigger they
/// are handed off to the hallway's RoomGrid so MoveAction pathfinds on
/// hallway tiles.
///
/// DRAG-BACK FIX:
///   After HallwayEntryTrigger transitions the player into a room, the player's
///   physics body is briefly still inside this trigger zone (the colliders
///   overlap at the mouth). Without a proper guard, OnTriggerEnter2D would
///   fire again and drag the player back into the hallway.
///
///   We fix this by checking whether the unit's current grid is ALREADY the
///   hallway grid before doing anything. If HallwayEntryTrigger already placed
///   the unit on the room grid, the unit's currentRoomGrid != hallway.RoomGrid
///   so... wait, that would pass the "already on hallway" guard wrong way.
///
///   Correct logic:
///     • If unit is already ON the hallway grid  → nothing to do (guard at top of HandOff)
///     • If unit is on a ROOM grid               → hand off to hallway UNLESS it just
///       arrived via HallwayEntryTrigger (detected by the entry trigger setting a flag)
///
///   The cleanest solution: track whether the unit arrived in a room via an
///   entry trigger on the Unit itself. We expose a static flag for that here
///   and HallwayEntryTrigger sets it. The walk trigger respects it.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class HallwayWalkTrigger : MonoBehaviour
{
    /// <summary>
    /// Set by HallwayEntryTrigger immediately before it transitions the player
    /// into a room. Prevents this walk trigger from pulling the player back
    /// into the hallway when the physics collider is still overlapping.
    /// Cleared after a short delay by HallwayEntryTrigger.
    /// </summary>
    public static bool EntryTransitionInProgress { get; set; }

    private HallwayGrid hallway;
    private bool        cooling;

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

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (cooling) return;
        if (EntryTransitionInProgress) return;   // ← entry trigger just fired; don't drag back
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

    private IEnumerator HandOffAfterMove(Unit unit)
    {
        cooling = true;

        // Wait for any in-progress move animation to finish first
        var move = unit.GetMoveAction();
        if (move != null)
            while (move.IsActive) yield return null;

        yield return new WaitForSeconds(0.05f);

        // Re-check: another trigger may have acted while we waited
        if (unit.GetCurrentRoomGrid() == hallway.RoomGrid)
        {
            cooling = false;
            yield break;
        }

        // Also bail if an entry transition fired while we were waiting
        if (EntryTransitionInProgress)
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

        GridPosition? bestGP = FindNearestWalkableToWorld(
            roomGrid, floorTilemap, transform.position);

        if (bestGP == null)
            bestGP = FindAnyWalkable(roomGrid, floorTilemap);

        if (bestGP == null)
        {
            Debug.LogError($"[HallwayWalkTrigger] No walkable cell found in {hallway.name}!");
            cooling = false;
            yield break;
        }

        Debug.Log($"[HallwayWalkTrigger] Handing {unit.name} to {hallway.name} at {bestGP.Value}");

        // Place unit on the hallway grid.
        // Do NOT call RoomManager.SetCurrentRoom(null) — TilemapHighlighter
        // resolves its tilemap from unit.GetCurrentRoomGrid() each frame.
        unit.PlaceInRoom(roomGrid, bestGP.Value);

        yield return new WaitForSeconds(0.5f);
        cooling = false;
    }

    private static GridPosition? FindNearestWalkableToWorld(
        RoomGrid roomGrid, Tilemap floorTilemap, Vector3 targetWorld)
    {
        GridPosition? best     = null;
        float         bestDist = float.MaxValue;

        foreach (Vector3Int cell in floorTilemap.cellBounds.allPositionsWithin)
        {
            if (!floorTilemap.HasTile(cell)) continue;
            var   gp      = new GridPosition(cell.x, cell.y);
            if (!roomGrid.IsWalkableIgnoreOccupancy(gp)) continue;
            float dist    = Vector3.Distance(floorTilemap.GetCellCenterWorld(cell), targetWorld);
            if (dist < bestDist) { bestDist = dist; best = gp; }
        }

        return best;
    }

    private static GridPosition? FindAnyWalkable(RoomGrid roomGrid, Tilemap floorTilemap)
    {
        foreach (Vector3Int cell in floorTilemap.cellBounds.allPositionsWithin)
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
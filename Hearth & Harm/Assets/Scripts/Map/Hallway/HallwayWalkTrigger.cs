using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Collider2D))]
public class HallwayWalkTrigger : MonoBehaviour
{
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

        if (unit.GetCurrentRoomGrid() == hallway.RoomGrid) return;

        StartCoroutine(HandOffAfterMove(unit));
    }

    private IEnumerator HandOffAfterMove(Unit unit)
    {
        cooling = true;

        var move = unit.GetMoveAction();
        if (move != null)
            while (move.IsActive) yield return null;

        yield return new WaitForSeconds(0.05f);

        if (unit.GetCurrentRoomGrid() == hallway.RoomGrid)
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

        // Find the walkable hallway cell whose world position is closest
        // to this trigger — bypasses any tilemap coordinate offset issues
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

        Debug.Log($"[HallwayWalkTrigger] Placing {unit.name} in {hallway.name} " +
                  $"at {bestGP.Value} " +
                  $"| valid={roomGrid.IsValidGridPosition(bestGP.Value)} " +
                  $"| walkable={roomGrid.IsWalkableIgnoreOccupancy(bestGP.Value)}");

        unit.PlaceInRoom(roomGrid, bestGP.Value);
        RoomManager.Instance?.SetCurrentRoom(null);

        yield return new WaitForSeconds(0.5f);
        cooling = false;
    }

    /// <summary>
    /// Iterates actual floor tilemap cells and finds the walkable one
    /// whose world position is closest to targetWorld.
    /// Bypasses coordinate-system mismatches entirely.
    /// </summary>
    private static GridPosition? FindNearestWalkableToWorld(
        RoomGrid roomGrid, Tilemap floorTilemap, Vector3 targetWorld)
    {
        GridPosition? best     = null;
        float         bestDist = float.MaxValue;

        foreach (Vector3Int cell in floorTilemap.cellBounds.allPositionsWithin)
        {
            if (!floorTilemap.HasTile(cell)) continue;

            var gp = new GridPosition(cell.x, cell.y);
            if (!roomGrid.IsWalkableIgnoreOccupancy(gp)) continue;

            Vector3 worldPos = floorTilemap.GetCellCenterWorld(cell);
            float   dist     = Vector3.Distance(worldPos, targetWorld);

            if (dist < bestDist)
            {
                bestDist = dist;
                best     = gp;
            }
        }

        return best;
    }

    /// <summary>
    /// Fallback — returns the first walkable cell found anywhere in the tilemap.
    /// </summary>
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
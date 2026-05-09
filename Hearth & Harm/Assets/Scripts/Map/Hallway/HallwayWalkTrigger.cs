using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Collider2D))]
public class HallwayWalkTrigger : MonoBehaviour
{
    private HallwayGrid hallway;
    private bool        cooling;
    private bool        locked;

    public GameObject DoorStripObject { get; set; }

    public void Initialize(HallwayGrid hg)
    {
        hallway = hg;
        GetComponent<Collider2D>().isTrigger = true;
    }

    public void SetLocked(bool isLocked)
    {
        locked = isLocked;
        if (DoorStripObject != null) DoorStripObject.SetActive(isLocked);
    }

    public void DisableTemporarily(float seconds) => StartCoroutine(TemporaryDisableRoutine(seconds));

    private IEnumerator TemporaryDisableRoutine(float seconds)
    {
        cooling = true;
        yield return new WaitForSeconds(seconds);
        cooling = false;
    }

    private void OnTriggerEnter2D(Collider2D other) => TryHandOff(other);
    private void OnTriggerStay2D(Collider2D other)  => TryHandOff(other);

    private void TryHandOff(Collider2D other)
    {
        if (cooling || locked) return;
        if (!other.CompareTag("Player")) return;
        
        if (hallway == null || !hallway.IsReady) return;

        var unit = other.GetComponent<Unit>() ?? other.GetComponentInParent<Unit>();
        if (unit == null) return;

        if (unit.GetCurrentRoomGrid() == hallway.RoomGrid) return;

        var move = unit.GetMoveAction();
        if (move != null && move.IsActive) return;

        if (RoomManager.Instance != null && RoomManager.Instance.CurrentRoomHasEnemies())
        {
            return; 
        }

        StartCoroutine(HandOffAfterMove(unit));
    }

    private IEnumerator HandOffAfterMove(Unit unit)
    {
        cooling = true;

        var move = unit.GetMoveAction();
        if (move != null && move.IsActive)
        {
            while (move.IsActive) yield return null;
        }

        yield return new WaitForFixedUpdate();
        yield return new WaitForEndOfFrame();

        var roomGrid = hallway.RoomGrid;
        if (roomGrid == null || !roomGrid.IsInitialized())
        {
            cooling = false;
            yield break;
        }

        Vector3? bestWorld = FindNearestWalkableWorldPos(roomGrid, roomGrid.GetFloorTilemap(), transform.position);
        if (bestWorld == null)
        {
            cooling = false;
            yield break;
        }

        GridPosition gridPos = roomGrid.GetGridPosition(bestWorld.Value);

        if (move != null)
        {
            move.ForceSyncGridPosition(roomGrid, gridPos);
        }
        else
        {
            unit.PlaceInRoom(roomGrid, gridPos);
        }

        ApplyHallwayCameraBounds();
        RoomManager.Instance?.SetInHallway();
        
        unit.transform.position = new Vector3(bestWorld.Value.x, bestWorld.Value.y, unit.transform.position.z);

        if (move != null) move.RefreshValidTargets();

        Debug.Log($"[Hallway Hand-off] {unit.name} adopted by {hallway.name} at {gridPos}");

        yield return new WaitForSeconds(0.2f); 
        cooling = false;
    }

    private static Vector3? FindNearestWalkableWorldPos(RoomGrid roomGrid, Tilemap floorTilemap, Vector3 targetWorld)
    {
        if (floorTilemap == null) return null;
        Vector3? best = null;
        float bestDist = float.MaxValue;

        foreach (var cell in floorTilemap.cellBounds.allPositionsWithin)
        {
            if (!floorTilemap.HasTile(cell)) continue;
            Vector3 worldCentre = floorTilemap.GetCellCenterWorld(cell);
            GridPosition gp = roomGrid.GetGridPosition(worldCentre);
            if (!roomGrid.IsWalkableIgnoreOccupancy(gp)) continue;

            float dist = Vector3.Distance(worldCentre, targetWorld);
            if (dist < bestDist) { bestDist = dist; best = worldCentre; }
        }
        return best;
    }

    private void ApplyHallwayCameraBounds()
    {
        var cam = CameraController2D.Instance;
        if (cam == null || hallway == null) return;
        
        var floor = hallway.FloorTilemap;
        if (floor == null) return;
        
        var cb = floor.cellBounds;
        Vector3 worldMin = floor.GetCellCenterWorld(new Vector3Int(cb.xMin, cb.yMin, 0));
        Vector3 worldMax = floor.GetCellCenterWorld(new Vector3Int(cb.xMax - 1, cb.yMax - 1, 0));
        
        Vector3 center = (worldMin + worldMax) * 0.5f;
        float width = Mathf.Abs(worldMax.x - worldMin.x);
        float height = Mathf.Abs(worldMax.y - worldMin.y);

        float horizontalPadding = 16f; 
        float verticalPadding = 64f;
        float minCameraWidth = 15f;  // Prevents zooming in too far in narrow halls
        float minCameraHeight = 10f;

        float finalWidth = Mathf.Max(width + horizontalPadding, minCameraWidth);
        float finalHeight = Mathf.Max(height + verticalPadding, minCameraHeight);

        // Apply the smoother, larger bounds
        Bounds hallwayBounds = new Bounds(center, new Vector3(finalWidth, finalHeight, 10f));
        cam.SetRoomBounds(hallwayBounds);
    }
}
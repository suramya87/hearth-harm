using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Collider2D))]
public class HallwayWalkTrigger : MonoBehaviour
{
    private HallwayGrid hallway;
    private bool        cooling;
    private bool        locked;

    // Track units currently being handed off so we don't double-trigger
    private Unit pendingUnit;

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

        // Already on the hallway grid — nothing to do
        if (unit.GetCurrentRoomGrid() == hallway.RoomGrid) return;

        // Already processing this unit — don't start a second coroutine
        if (pendingUnit == unit) return;

        if (RoomManager.Instance != null && RoomManager.Instance.CurrentRoomHasEnemies()) return;

        // FIX: don't bail if the move is active — HandOffAfterMove waits for it to finish.
        // Previously returning here meant fast players would exit the collider while still
        // moving and the handoff would never fire.
        pendingUnit = unit;
        StartCoroutine(HandOffAfterMove(unit));
    }

    private IEnumerator HandOffAfterMove(Unit unit)
    {
        cooling = true;

        // Wait for any in-progress move to complete before doing the grid swap
        var move = unit.GetMoveAction();
        if (move != null)
        {
            // FIX: use a timeout so a stuck move can't lock this forever
            float timeout = 3f;
            float elapsed = 0f;
            while (move.IsActive)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= timeout)
                {
                    Debug.LogWarning($"[HallwayWalkTrigger] Move timed out waiting for {unit.name}. Forcing handoff.");
                    break;
                }
                yield return null;
            }
        }

        yield return new WaitForFixedUpdate();
        yield return new WaitForEndOfFrame();

        // FIX: re-check after waiting — the player may have already transitioned
        // via the entry trigger during the move, making this handoff redundant
        if (unit.GetCurrentRoomGrid() == hallway.RoomGrid)
        {
            pendingUnit = null;
            cooling     = false;
            yield break;
        }

        var roomGrid = hallway.RoomGrid;
        if (roomGrid == null || !roomGrid.IsInitialized())
        {
            pendingUnit = null;
            cooling     = false;
            yield break;
        }

        Vector3? bestWorld = FindNearestWalkableWorldPos(
            roomGrid, roomGrid.GetFloorTilemap(), transform.position);

        if (bestWorld == null)
        {
            Debug.LogWarning($"[HallwayWalkTrigger] No walkable cell found near {transform.position}");
            pendingUnit = null;
            cooling     = false;
            yield break;
        }

        GridPosition gridPos = roomGrid.GetGridPosition(bestWorld.Value);

        if (move != null)
            move.ForceSyncGridPosition(roomGrid, gridPos);
        else
            unit.PlaceInRoom(roomGrid, gridPos);

        ApplyHallwayCameraBounds();
        RoomManager.Instance?.SetInHallway();

        unit.transform.position = new Vector3(
            bestWorld.Value.x, bestWorld.Value.y, unit.transform.position.z);

        if (move != null) move.RefreshValidTargets();

        Debug.Log($"[HallwayWalkTrigger] {unit.name} adopted by {hallway.name} at {gridPos}");

        pendingUnit = null;
        yield return new WaitForSeconds(0.2f);
        cooling = false;
    }

    private static Vector3? FindNearestWalkableWorldPos(
        RoomGrid roomGrid, Tilemap floorTilemap, Vector3 targetWorld)
    {
        if (floorTilemap == null) return null;

        Vector3? best     = null;
        float    bestDist = float.MaxValue;

        foreach (var cell in floorTilemap.cellBounds.allPositionsWithin)
        {
            if (!floorTilemap.HasTile(cell)) continue;

            Vector3      worldCentre = floorTilemap.GetCellCenterWorld(cell);
            GridPosition gp          = roomGrid.GetGridPosition(worldCentre);

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

        var     cb       = floor.cellBounds;
        Vector3 worldMin = floor.GetCellCenterWorld(new Vector3Int(cb.xMin, cb.yMin, 0));
        Vector3 worldMax = floor.GetCellCenterWorld(new Vector3Int(cb.xMax - 1, cb.yMax - 1, 0));

        Vector3 center = (worldMin + worldMax) * 0.5f;
        float   width  = Mathf.Abs(worldMax.x - worldMin.x);
        float   height = Mathf.Abs(worldMax.y - worldMin.y);

        float horizontalPadding = 64f;
        float verticalPadding   = 64f;
        float minCameraWidth    = 32f;
        float minCameraHeight   = 10f;

        float finalWidth  = Mathf.Max(width  + horizontalPadding, minCameraWidth);
        float finalHeight = Mathf.Max(height + verticalPadding,   minCameraHeight);

        Bounds hallwayBounds = new Bounds(center, new Vector3(finalWidth, finalHeight, 10f));
        cam.SetRoomBounds(hallwayBounds);
    }
}
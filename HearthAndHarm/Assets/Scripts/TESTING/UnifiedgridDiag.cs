using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;


public class UnifiedGridDiagnostic : MonoBehaviour
{
    [ContextMenu("Run Diagnostics")]
    private void Start() => RunDiagnostics();

    private void RunDiagnostics()
    {
        Debug.Log("═══════════ UnifiedWorldGrid Diagnostic ═══════════");

        var uwg = UnifiedWorldGrid.Instance;
        if (uwg == null)
        {
            Debug.LogError("[DIAG] UnifiedWorldGrid.Instance is NULL. " +
                           "No GameObject in the scene has the UnifiedWorldGrid component.");
            return;
        }
        Debug.Log($"[DIAG] UnifiedWorldGrid found: {uwg.gameObject.name}");
        Debug.Log($"[DIAG] Total cells registered: {uwg.AllCells.Count}");

        if (uwg.AllCells.Count == 0)
        {
            Debug.LogError("[DIAG] Grid is EMPTY. Rooms/hallways haven't registered yet, " +
                           "or RoomTilemapSetup.Initialize() is called before " +
                           "UnifiedWorldGrid is in the scene.");
        }

        int shown = 0;
        foreach (var kvp in uwg.AllCells)
        {
            if (shown++ >= 5) break;
            Debug.Log($"[DIAG]   Registered key: {kvp.Key}  " +
                      $"world: {kvp.Value.WorldCentre}  " +
                      $"floor: {kvp.Value.IsFloor}  " +
                      $"owner: {kvp.Value.OwnerGrid?.gameObject.name ?? "null"}");
        }

        // ── 3. Find the player unit ────────────────────────────────────────
        var unit = FindAnyObjectByType<Unit>();
        if (unit == null)
        {
            Debug.LogWarning("[DIAG] No Unit found in scene.");
            return;
        }

        Debug.Log($"[DIAG] Unit: {unit.name}  " +
                  $"transform.position: {unit.transform.position}");

        var roomGrid = unit.GetCurrentRoomGrid();
        if (roomGrid == null)
        {
            Debug.LogError("[DIAG] unit.GetCurrentRoomGrid() is NULL. " +
                           "The unit hasn't been placed in a room yet.");
            return;
        }
        Debug.Log($"[DIAG] Unit's RoomGrid: {roomGrid.gameObject.name}");

        GridPosition gp        = unit.GetGridPosition();
        Vector3      gpWorld   = roomGrid.GetWorldPosition(gp);
        Vector3Int   gpKey     = UnifiedWorldGrid.WorldKey(gpWorld);

        Debug.Log($"[DIAG] Unit GridPosition: {gp}  → world: {gpWorld}  → key: {gpKey}");

        bool foundByGP = uwg.AllCells.ContainsKey(gpKey);
        Debug.Log(foundByGP
            ? $"[DIAG] ✓ Key {gpKey} IS in UnifiedWorldGrid."
            : $"[DIAG] ✗ Key {gpKey} is NOT in UnifiedWorldGrid.");

        // ── 4. Check unit's transform position ────────────────────────────
        Vector3    tWorld = unit.transform.position;
        Vector3Int tKey   = UnifiedWorldGrid.WorldKey(tWorld);
        bool foundByTrans = uwg.AllCells.ContainsKey(tKey);
        Debug.Log($"[DIAG] Unit transform.position: {tWorld}  → key: {tKey}  " +
                  $"in grid: {foundByTrans}");

        // ── 5. Nearest registered key ──────────────────────────────────────
        Vector3Int nearest  = default;
        float      nearDist = float.MaxValue;
        foreach (var key in uwg.AllCells.Keys)
        {
            float d = Vector3Int.Distance(key, gpKey);
            if (d < nearDist) { nearDist = d; nearest = key; }
        }
        Debug.Log($"[DIAG] Nearest registered key to unit: {nearest}  " +
                  $"(distance: {nearDist:F2} cells)");

        if (nearDist > 0.5f)
        {
            Debug.LogError("[DIAG] *** COORDINATE MISMATCH DETECTED ***\n" +
                           $"Unit key {gpKey} doesn't match any registered cell.\n" +
                           $"Nearest is {nearest} ({nearDist:F2} cells away).\n" +
                           "This means the Tilemap cell centres don't round to the same " +
                           "integers as the unit's GridPosition→World conversion.\n" +
                           "Check: does your Grid component use cell size (1,1,0)? " +
                           "And does TilemapRoomGrid.GetWorldPosition return the cell CENTRE?");
        }

        // ── 6. Check whether the floor tilemap keys match ──────────────────
        var floor = roomGrid.GetFloorTilemap();
        if (floor != null)
        {
            Debug.Log($"[DIAG] Floor tilemap: {floor.gameObject.name}  " +
                      $"cellBounds: {floor.cellBounds}");

            // Sample first 3 floor cells and show their world centres + keys.
            int sampleCount = 0;
            foreach (Vector3Int cell in floor.cellBounds.allPositionsWithin)
            {
                if (!floor.HasTile(cell)) continue;
                if (sampleCount++ >= 3) break;
                Vector3    centre = floor.GetCellCenterWorld(cell);
                Vector3Int key    = UnifiedWorldGrid.WorldKey(centre);
                bool       inUWG  = uwg.AllCells.ContainsKey(key);
                Debug.Log($"[DIAG]   Floor cell {cell}  centre: {centre}  " +
                          $"key: {key}  in UWG: {inUWG}");
            }
        }

        // ── 7. Check all registered tilemaps ──────────────────────────────
        Debug.Log("[DIAG] All registered Tilemaps:");
        foreach (var tm in FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            if (tm.gameObject.name != "Floor") continue;
            Vector3 sample = tm.GetCellCenterWorld(tm.cellBounds.min);
            Debug.Log($"[DIAG]   {tm.transform.parent?.name}/{tm.gameObject.name}  " +
                      $"sample centre: {sample}  " +
                      $"key: {UnifiedWorldGrid.WorldKey(sample)}");
        }

        Debug.Log("═══════════════════════════════════════════════════");
    }
}
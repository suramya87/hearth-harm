using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Attach this to any GameObject in your scene and run the game.
/// It logs a full diagnostic of every hallway trigger, collider, and grid
/// so you can see exactly where the chain is breaking.
///
/// Also draws Gizmos in the Scene view (select this GameObject to see them):
///   GREEN  = trigger collider that looks correct
///   RED    = trigger collider with a problem
///   YELLOW = hallway floor tile bounds
///   CYAN   = nearest valid hallway cell to the player
/// </summary>
public class HallwayDebugger : MonoBehaviour
{
    [Header("Run diagnostics automatically on Start")]
    [SerializeField] private bool runOnStart = true;

    [Header("Continuous — log trigger overlaps every N seconds (0 = off)")]
    [SerializeField] private float continuousInterval = 0f;

    private float nextCheck;

    private void Start()
    {
        if (runOnStart)
            Invoke(nameof(RunDiagnostics), 0.5f); // slight delay so level finishes generating
    }

    private void Update()
    {
        if (continuousInterval > 0f && Time.time >= nextCheck)
        {
            nextCheck = Time.time + continuousInterval;
            CheckPlayerOverlap();
        }
    }

    // ── Main diagnostic ────────────────────────────────────────────────────

    [ContextMenu("Run Hallway Diagnostics")]
    public void RunDiagnostics()
    {
        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log("[HallwayDebugger] ▶ START DIAGNOSTICS");
        Debug.Log("═══════════════════════════════════════════════════");

        CheckPlayerRigidbody();
        CheckHallwayGrids();
        CheckTriggers();
        CheckPlayerReachability();

        Debug.Log("═══════════════════════════════════════════════════");
        Debug.Log("[HallwayDebugger] ▶ END DIAGNOSTICS");
        Debug.Log("═══════════════════════════════════════════════════");
    }

    // ── 1. Player must have Rigidbody2D for OnTriggerEnter2D to fire ──────

    private void CheckPlayerRigidbody()
    {
        Debug.Log("─── [1] Player Rigidbody2D check ───");

        var player = GameObject.FindWithTag("Player");
        if (player == null) { Debug.LogError("  ✗ No GameObject tagged 'Player' found!"); return; }

        var rb = player.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            Debug.LogError("  ✗ Player has NO Rigidbody2D! " +
                           "OnTriggerEnter2D will NEVER fire without one. " +
                           "Add a Rigidbody2D (Body Type = Kinematic) to your player prefab.");
        }
        else
        {
            Debug.Log($"  ✓ Player has Rigidbody2D. BodyType={rb.bodyType} " +
                      $"IsKinematic={rb.isKinematic}");
            if (rb.bodyType == RigidbodyType2D.Static)
                Debug.LogWarning("  ⚠ Rigidbody2D is Static — triggers may not fire. Use Kinematic.");
        }

        var col = player.GetComponent<Collider2D>();
        if (col == null)
            Debug.LogWarning("  ⚠ Player has no Collider2D on root — trigger overlap may miss.");
        else
            Debug.Log($"  ✓ Player has Collider2D ({col.GetType().Name}) isTrigger={col.isTrigger}");
    }

    // ── 2. Hallway grids ───────────────────────────────────────────────────

    private void CheckHallwayGrids()
    {
        Debug.Log("─── [2] HallwayGrid check ───");

        var hallways = FindObjectsByType<HallwayGrid>(FindObjectsSortMode.None);
        if (hallways.Length == 0) { Debug.LogError("  ✗ No HallwayGrid components found in scene!"); return; }

        Debug.Log($"  Found {hallways.Length} HallwayGrid(s).");

        foreach (var h in hallways)
        {
            bool ready = h.IsReady;
            string status = ready ? "✓" : "✗";
            Debug.Log($"  {status} {h.gameObject.name} — IsReady={ready}");

            if (!ready)
            {
                Debug.LogError($"    HallwayGrid '{h.gameObject.name}' is NOT ready. " +
                               "HallwayGrid.Initialize() may have failed. " +
                               "Check that HallwayTilemapPainter painted at least one floor tile.");
                continue;
            }

            var rg = h.RoomGrid;
            int w = rg.GetWidth(), ht = rg.GetHeight();
            var floor = rg.GetFloorTilemap();

            int tileCount = 0;
            if (floor != null)
                foreach (var pos in floor.cellBounds.allPositionsWithin)
                    if (floor.HasTile(pos)) tileCount++;

            Debug.Log($"    RoomGrid size: {w}x{ht}  Floor tiles: {tileCount}");

            if (tileCount == 0)
                Debug.LogError($"    ✗ NO floor tiles painted! HallwayTilemapPainter produced nothing. " +
                               "Check HallwayTileSet has floor tiles assigned and " +
                               "that exitWorld/entryWorld positions are correct.");

            // Sample walkability at the grid centre
            var centre = new GridPosition(w / 2, ht / 2);
            bool valid    = rg.IsValidGridPosition(centre);
            bool walkable = rg.IsWalkable(centre);
            Debug.Log($"    Centre cell {centre}: IsValid={valid} IsWalkable={walkable}");
        }
    }

    // ── 3. Triggers ────────────────────────────────────────────────────────

    private void CheckTriggers()
    {
        Debug.Log("─── [3] HallwayEntryTrigger check ───");

        var triggers = FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None);
        if (triggers.Length == 0)
        {
            Debug.LogError("  ✗ No HallwayEntryTrigger components found! " +
                           "HallwayBuilder.Build() may have failed before placing triggers.");
            return;
        }

        Debug.Log($"  Found {triggers.Length} trigger(s).");

        foreach (var t in triggers)
        {
            var col = t.GetComponent<Collider2D>();
            bool hasTrigger = col != null && col.isTrigger;
            string status = hasTrigger ? "✓" : "✗";

            Debug.Log($"  {status} '{t.gameObject.name}' " +
                      $"IsHallwayEntry={t.IsHallwayEntry} " +
                      $"pos={t.transform.position} " +
                      $"HallwayReady={t.Hallway?.IsReady} " +
                      $"HasCollider={col != null} " +
                      $"isTrigger={col?.isTrigger}");

            if (col == null)
                Debug.LogError($"    ✗ NO Collider2D on trigger '{t.gameObject.name}'!");
            else if (!col.isTrigger)
                Debug.LogError($"    ✗ Collider2D is NOT a trigger on '{t.gameObject.name}'!");

            // Check that a hallway-entry trigger actually has hallway tiles nearby
            if (t.IsHallwayEntry && t.Hallway != null && t.Hallway.IsReady)
            {
                var floor = t.Hallway.RoomGrid.GetFloorTilemap();
                if (floor != null)
                {
                    Vector3Int trigCell = floor.WorldToCell(t.transform.position);
                    bool hasTileAtTrigger = floor.HasTile(trigCell);

                    // Check a 3x3 area around the trigger
                    bool hasTileNearby = false;
                    for (int dx = -2; dx <= 2 && !hasTileNearby; dx++)
                    for (int dy = -2; dy <= 2 && !hasTileNearby; dy++)
                        if (floor.HasTile(trigCell + new Vector3Int(dx, dy, 0)))
                            hasTileNearby = true;

                    if (!hasTileNearby)
                        Debug.LogWarning($"    ⚠ No hallway floor tiles within 2 cells of trigger " +
                                         $"'{t.gameObject.name}' at {t.transform.position}. " +
                                         "PlaceInHallway will fail to find a valid cell.");
                    else if (!hasTileAtTrigger)
                        Debug.Log($"    ℹ No tile AT trigger position but tiles nearby — " +
                                  "FindNearestValidCell fallback should handle this.");
                    else
                        Debug.Log($"    ✓ Floor tile exists at trigger position.");
                }
            }

            // Check BoxCollider2D size makes sense
            var box = col as BoxCollider2D;
            if (box != null)
            {
                Debug.Log($"    Collider size: {box.size}  offset: {box.offset}");
                if (box.size.x < 0.1f || box.size.y < 0.1f)
                    Debug.LogError($"    ✗ Collider size is nearly zero — player will never overlap it!");
            }
        }
    }

    // ── 4. Can the player actually reach a trigger? ────────────────────────

    private void CheckPlayerReachability()
    {
        Debug.Log("─── [4] Player reachability check ───");

        var player = GameObject.FindWithTag("Player");
        if (player == null) return;

        var unit = player.GetComponent<Unit>();
        if (unit == null || !unit.IsInitialized())
        {
            Debug.LogWarning("  ⚠ Player Unit not initialized yet.");
            return;
        }

        var currentGrid = unit.GetCurrentRoomGrid();
        if (currentGrid == null)
        {
            Debug.LogWarning("  ⚠ Unit has no current RoomGrid.");
            return;
        }

        var playerPos = unit.GetGridPosition();
        Debug.Log($"  Player is at grid {playerPos} in '{currentGrid.gameObject.name}'");

        // Find all entry triggers and check if any are inside the player's current grid
        var triggers = FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None);
        bool anyReachable = false;

        foreach (var t in triggers)
        {
            if (!t.IsHallwayEntry) continue;

            // Is this trigger's world position within the current room's grid?
            bool inCurrentGrid = currentGrid.IsPositionInRoom(t.transform.position);
            if (inCurrentGrid)
            {
                var triggerGridPos = currentGrid.GetGridPosition(t.transform.position);
                Debug.Log($"  Trigger '{t.gameObject.name}' is at room grid pos {triggerGridPos}");
                anyReachable = true;
            }
        }

        if (!anyReachable)
        {
            Debug.LogWarning("  ⚠ No hallway-entry triggers overlap the player's current room grid. " +
                             "This is EXPECTED — triggers sit at the door mouth, not inside the room. " +
                             "The player must physically walk to the door to enter the hallway. " +
                             "See the DEADLOCK note below.");
        }

        // ── THE CORE DEADLOCK WARNING ─────────────────────────────────────
        Debug.Log("─── [4b] Turn-based movement deadlock check ───");
        Debug.LogWarning(
            "IMPORTANT — POSSIBLE GAMEPLAY DEADLOCK:\n" +
            "MoveAction.GetValidTargets() only queries the current room's RoomGrid.\n" +
            "Hallway tiles are on a SEPARATE RoomGrid. The player cannot click on\n" +
            "hallway tiles to walk into them because they are never highlighted.\n" +
            "The HallwayEntryTrigger only fires when the player PHYSICALLY overlaps it.\n\n" +
            "In a turn-based grid game this creates a deadlock:\n" +
            "  • Player can't click hallway tiles (not in room grid)\n" +
            "  • Trigger only fires when player reaches the tile physically\n" +
            "  • Player can't reach the tile without clicking it\n\n" +
            "FIX OPTIONS:\n" +
            "  A) Extend GetValidTargets() to also include door-mouth tiles from\n" +
            "     the current room's RoomConnector/SpawnPoints so the player CAN\n" +
            "     click the door tile. When they move there, MoveAction calls\n" +
            "     RoomManager.SetCurrentHallway() after the move completes.\n\n" +
            "  B) Make the HallwayEntryTrigger large enough (2-3 tiles deep into\n" +
            "     the room) so the player overlaps it on a normal in-room move.\n\n" +
            "  C) Use a click-to-enter door object (RoomDoor style) instead of a\n" +
            "     physics trigger for the room→hallway transition.");
    }

    // ── Continuous overlap check ───────────────────────────────────────────

    private void CheckPlayerOverlap()
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null) return;

        var triggers = FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None);
        foreach (var t in triggers)
        {
            var col = t.GetComponent<Collider2D>();
            if (col == null) continue;

            var playerCol = player.GetComponent<Collider2D>();
            if (playerCol == null) continue;

            if (col.bounds.Intersects(playerCol.bounds))
                Debug.Log($"[HallwayDebugger] Player overlaps trigger '{t.gameObject.name}' " +
                          $"IsHallwayEntry={t.IsHallwayEntry}");
        }
    }

    // ── Gizmos ─────────────────────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        // Triggers
        var triggers = FindObjectsByType<HallwayEntryTrigger>(FindObjectsSortMode.None);
        foreach (var t in triggers)
        {
            var col = t.GetComponent<BoxCollider2D>();
            if (col == null) continue;

            bool ok = col.isTrigger && t.Hallway != null && t.Hallway.IsReady;
            Gizmos.color = ok ? new Color(0, 1, 0, 0.4f) : new Color(1, 0, 0, 0.4f);
            Gizmos.DrawCube(t.transform.position + (Vector3)col.offset,
                            new Vector3(col.size.x, col.size.y, 0.1f));

            Gizmos.color = ok ? Color.green : Color.red;
            Gizmos.DrawWireCube(t.transform.position + (Vector3)col.offset,
                                new Vector3(col.size.x, col.size.y, 0.1f));

#if UNITY_EDITOR
            UnityEditor.Handles.Label(t.transform.position + Vector3.up * 0.3f,
                t.IsHallwayEntry ? "ENTRY" : "EXIT");
#endif
        }

        // Hallway floor bounds
        var hallways = FindObjectsByType<HallwayGrid>(FindObjectsSortMode.None);
        foreach (var h in hallways)
        {
            if (!h.IsReady) continue;
            var floor = h.RoomGrid.GetFloorTilemap();
            if (floor == null) continue;

            Gizmos.color = new Color(1, 1, 0, 0.15f);
            var bounds = floor.localBounds;
            Gizmos.DrawCube(floor.transform.TransformPoint(bounds.center),
                            bounds.size);
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(floor.transform.TransformPoint(bounds.center),
                                bounds.size);
        }
    }
}